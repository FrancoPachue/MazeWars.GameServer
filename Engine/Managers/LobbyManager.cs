using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Models;
using Microsoft.Extensions.Options;

namespace MazeWars.GameServer.Engine.Managers;

/// <summary>
/// ⭐ REFACTORED: Manages lobby lifecycle, matchmaking, and game start conditions.
/// Extracted from GameEngine to reduce complexity and improve maintainability.
/// </summary>
public class LobbyManager
{
    private readonly ILogger<LobbyManager> _logger;
    private readonly GameServerSettings _settings;
    private readonly Dictionary<string, WorldLobby> _worldLobbies = new();
    private readonly Timer _lobbyCleanupTimer;
    private readonly Timer _lobbyStartTimer;
    private readonly object _lobbiesLock = new object();

    /// <summary>
    /// Event triggered when a lobby is ready to start a game.
    /// Provides (lobbyId, playerIds) for the NetworkService to notify clients.
    /// </summary>
    public event Action<WorldLobby>? OnLobbyReadyToStart;

    public LobbyManager(
        ILogger<LobbyManager> logger,
        IOptions<GameServerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;

        // Timer para limpiar lobbies vacíos (cada 30 segundos)
        _lobbyCleanupTimer = new Timer(CleanupEmptyLobbies, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Timer para iniciar partidas cuando el lobby lleva tiempo esperando (cada 5 segundos)
        _lobbyStartTimer = new Timer(CheckLobbyStartConditions, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _logger.LogInformation("LobbyManager initialized with {MinPlayers}-{MaxPlayers} players per lobby",
            _settings.MinPlayersPerWorld, _settings.MaxPlayersPerWorld);
    }

    // =============================================
    // MATCHMAKING SYSTEM
    // =============================================

    /// <summary>
    /// Find an available lobby for a team or create a new one.
    /// </summary>
    public string FindOrCreateLobby(string teamId)
    {
        lock (_lobbiesLock)
        {
            // Try to find an available lobby
            foreach (var lobby in _worldLobbies.Values.Where(l => l.Status == LobbyStatus.WaitingForPlayers))
            {
                if (CanJoinLobby(lobby, teamId))
                {
                    _logger.LogInformation("Found available lobby {LobbyId} for team {TeamId}",
                        lobby.LobbyId, teamId);
                    return lobby.LobbyId;
                }
            }

            // Create new lobby if none available
            var newLobbyId = CreateNewLobby();
            _logger.LogInformation("Created new lobby {LobbyId} for team {TeamId}", newLobbyId, teamId);
            return newLobbyId;
        }
    }

    /// <summary>
    /// Check if a team can join a specific lobby.
    /// </summary>
    private bool CanJoinLobby(WorldLobby lobby, string teamId)
    {
        // Check lobby capacity
        if (lobby.TotalPlayers >= _settings.MaxPlayersPerWorld)
            return false;

        // Check team capacity
        if (!lobby.TeamPlayerCounts.TryGetValue(teamId, out var teamCount))
            teamCount = 0;

        if (teamCount >= _settings.GameBalance.MaxTeamSize)
            return false;

        // Prevent new teams from joining if other teams are already at max size
        var maxTeamSize = lobby.TeamPlayerCounts.Values.DefaultIfEmpty(0).Max();
        if (teamCount == 0 && maxTeamSize >= _settings.GameBalance.MaxTeamSize)
            return false;

        return true;
    }

    /// <summary>
    /// Create a new lobby.
    /// </summary>
    private string CreateNewLobby()
    {
        var lobbyId = Guid.NewGuid().ToString();

        var lobby = new WorldLobby
        {
            LobbyId = lobbyId,
            Status = LobbyStatus.WaitingForPlayers,
            CreatedAt = DateTime.UtcNow,
            LastPlayerJoined = DateTime.UtcNow,
            MinPlayersToStart = Math.Max(2, _settings.MinPlayersPerWorld),
            MaxPlayers = _settings.MaxPlayersPerWorld,
            TeamPlayerCounts = new Dictionary<string, int>(),
            TotalPlayers = 0
        };

        _worldLobbies[lobbyId] = lobby;

        _logger.LogDebug("Created lobby {LobbyId} (Min: {Min}, Max: {Max})",
            lobbyId, lobby.MinPlayersToStart, lobby.MaxPlayers);

        return lobbyId;
    }

    // =============================================
    // PLAYER MANAGEMENT
    // =============================================

    /// <summary>
    /// Add a player to a lobby.
    /// </summary>
    public bool AddPlayerToLobby(string lobbyId, RealTimePlayer player)
    {
        lock (_lobbiesLock)
        {
            if (!_worldLobbies.TryGetValue(lobbyId, out var lobby))
            {
                _logger.LogWarning("Attempted to add player to non-existent lobby {LobbyId}", lobbyId);
                return false;
            }

            if (lobby.Status != LobbyStatus.WaitingForPlayers)
            {
                _logger.LogDebug("Cannot join lobby {LobbyId} - status: {Status}",
                    lobbyId, lobby.Status);
                return false;
            }

            if (!CanJoinLobby(lobby, player.TeamId))
            {
                _logger.LogDebug("Cannot join lobby {LobbyId} - team restrictions", lobbyId);
                return false;
            }

            // Add player to lobby
            if (!lobby.TeamPlayerCounts.ContainsKey(player.TeamId))
            {
                lobby.TeamPlayerCounts[player.TeamId] = 0;
            }

            lobby.TeamPlayerCounts[player.TeamId]++;
            lobby.TotalPlayers++;
            lobby.LastPlayerJoined = DateTime.UtcNow;
            lobby.Players[player.PlayerId] = player;

            _logger.LogInformation(
                "Player {PlayerName} joined lobby {LobbyId}. Players: {Count}/{Max}, Team {TeamId}: {TeamCount}",
                player.PlayerName, lobbyId, lobby.TotalPlayers, lobby.MaxPlayers,
                player.TeamId, lobby.TeamPlayerCounts[player.TeamId]);

            // Check if lobby can start
            CheckIfLobbyCanStart(lobby);

            return true;
        }
    }

    /// <summary>
    /// Remove a player from a lobby (e.g., disconnect before game starts).
    /// </summary>
    public bool RemovePlayerFromLobby(string lobbyId, string playerId)
    {
        lock (_lobbiesLock)
        {
            if (!_worldLobbies.TryGetValue(lobbyId, out var lobby))
                return false;

            if (!lobby.Players.TryGetValue(playerId, out var player))
                return false;

            // Remove player (ConcurrentDictionary uses TryRemove)
            lobby.Players.TryRemove(playerId, out _);
            lobby.TotalPlayers--;

            if (lobby.TeamPlayerCounts.TryGetValue(player.TeamId, out var teamCount))
            {
                lobby.TeamPlayerCounts[player.TeamId] = Math.Max(0, teamCount - 1);
            }

            _logger.LogInformation("Player {PlayerName} removed from lobby {LobbyId}. Remaining: {Count}/{Max}",
                player.PlayerName, lobbyId, lobby.TotalPlayers, lobby.MaxPlayers);

            return true;
        }
    }

    /// <summary>
    /// Get a lobby by ID.
    /// </summary>
    public WorldLobby? GetLobby(string lobbyId)
    {
        lock (_lobbiesLock)
        {
            return _worldLobbies.TryGetValue(lobbyId, out var lobby) ? lobby : null;
        }
    }

    /// <summary>
    /// Check if a world ID corresponds to a lobby.
    /// </summary>
    public bool IsLobby(string worldId)
    {
        lock (_lobbiesLock)
        {
            return _worldLobbies.ContainsKey(worldId);
        }
    }

    /// <summary>
    /// Get available lobbies that are accepting players.
    /// </summary>
    public List<string> GetAvailableLobbies(int maxPlayersPerWorld)
    {
        lock (_lobbiesLock)
        {
            return _worldLobbies.Values
                .Where(l => l.Status == LobbyStatus.WaitingForPlayers)
                .Where(l => l.TotalPlayers < maxPlayersPerWorld)
                .Select(l => l.LobbyId)
                .ToList();
        }
    }

    /// <summary>
    /// Get lobby info for client display.
    /// </summary>
    public LobbyInfo? GetLobbyInfo(string lobbyId)
    {
        lock (_lobbiesLock)
        {
            if (!_worldLobbies.TryGetValue(lobbyId, out var lobby))
                return null;

            return new LobbyInfo
            {
                LobbyId = lobby.LobbyId,
                Status = lobby.Status.ToString(),
                CurrentPlayers = lobby.TotalPlayers,
                MinPlayersToStart = lobby.MinPlayersToStart,
                MaxPlayers = lobby.MaxPlayers,
                LastPlayerJoined = lobby.LastPlayerJoined,
                TeamCounts = new Dictionary<string, int>(lobby.TeamPlayerCounts)
            };
        }
    }

    // =============================================
    // GAME START LOGIC
    // =============================================

    /// <summary>
    /// Check if a lobby meets start conditions and trigger game start.
    /// </summary>
    private void CheckIfLobbyCanStart(WorldLobby lobby)
    {
        if (lobby.Status != LobbyStatus.WaitingForPlayers)
            return;

        // RULE 1: Start immediately if lobby is full
        if (lobby.TotalPlayers >= lobby.MaxPlayers)
        {
            _logger.LogInformation("Starting lobby {LobbyId} - FULL ({Players} players)",
                lobby.LobbyId, lobby.TotalPlayers);
            RequestLobbyStart(lobby);
            return;
        }

        // RULE 2: Start after wait time if minimum players + multiple teams
        var teamsCount = lobby.TeamPlayerCounts.Count;
        if (lobby.TotalPlayers >= lobby.MinPlayersToStart && teamsCount >= 2)
        {
            var timeSinceLastJoin = DateTime.UtcNow - lobby.LastPlayerJoined;

            if (timeSinceLastJoin.TotalSeconds >= _settings.LobbySettings.MaxWaitTimeSeconds)
            {
                _logger.LogInformation("Starting lobby {LobbyId} - TIMEOUT ({Players} players, {Teams} teams)",
                    lobby.LobbyId, lobby.TotalPlayers, teamsCount);
                RequestLobbyStart(lobby);
                return;
            }
        }
    }

    /// <summary>
    /// Timer callback to check all lobbies for start conditions.
    /// </summary>
    private void CheckLobbyStartConditions(object? state)
    {
        try
        {
            lock (_lobbiesLock)
            {
                var lobbiesReadyToStart = _worldLobbies.Values
                    .Where(l => l.Status == LobbyStatus.WaitingForPlayers)
                    .Where(ShouldStartLobby)
                    .ToList();

                foreach (var lobby in lobbiesReadyToStart)
                {
                    CheckIfLobbyCanStart(lobby);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking lobby start conditions");
        }
    }

    /// <summary>
    /// Determine if a lobby should be started based on time conditions.
    /// </summary>
    private bool ShouldStartLobby(WorldLobby lobby)
    {
        if (lobby.TotalPlayers >= lobby.MinPlayersToStart)
        {
            var timeSinceCreated = DateTime.UtcNow - lobby.CreatedAt;
            var timeSinceLastJoin = DateTime.UtcNow - lobby.LastPlayerJoined;

            // Start if lobby has been waiting too long OR no new players in a while
            return timeSinceCreated.TotalSeconds >= _settings.LobbySettings.MaxWaitTimeSeconds * 2 ||
                   timeSinceLastJoin.TotalSeconds >= _settings.LobbySettings.MaxWaitTimeSeconds;
        }

        return false;
    }

    /// <summary>
    /// Request lobby to start game (triggers event for GameEngine to create world).
    /// </summary>
    private void RequestLobbyStart(WorldLobby lobby)
    {
        lobby.Status = LobbyStatus.Starting;
        OnLobbyReadyToStart?.Invoke(lobby);
    }

    /// <summary>
    /// Complete lobby transition to game world (called by GameEngine after world creation).
    /// </summary>
    public void CompleteLobbyStart(string lobbyId)
    {
        lock (_lobbiesLock)
        {
            if (_worldLobbies.Remove(lobbyId))
            {
                _logger.LogInformation("Lobby {LobbyId} successfully transitioned to game world", lobbyId);
            }
        }
    }

    /// <summary>
    /// Mark lobby as error state (called by GameEngine if world creation fails).
    /// </summary>
    public void MarkLobbyError(string lobbyId, string error)
    {
        lock (_lobbiesLock)
        {
            if (_worldLobbies.TryGetValue(lobbyId, out var lobby))
            {
                lobby.Status = LobbyStatus.Error;
                _logger.LogError("Lobby {LobbyId} marked as error: {Error}", lobbyId, error);
            }
        }
    }

    // =============================================
    // CLEANUP AND MAINTENANCE
    // =============================================

    /// <summary>
    /// Timer callback to clean up empty or error lobbies.
    /// </summary>
    private void CleanupEmptyLobbies(object? state)
    {
        try
        {
            lock (_lobbiesLock)
            {
                // Remove empty lobbies older than 5 minutes
                var emptyLobbies = _worldLobbies.Values
                    .Where(l => l.TotalPlayers == 0)
                    .Where(l => DateTime.UtcNow - l.CreatedAt > TimeSpan.FromMinutes(5))
                    .ToList();

                foreach (var lobby in emptyLobbies)
                {
                    _worldLobbies.Remove(lobby.LobbyId);
                    _logger.LogInformation("Removed empty lobby {LobbyId}", lobby.LobbyId);
                }

                // Remove error lobbies older than 1 minute
                var errorLobbies = _worldLobbies.Values
                    .Where(l => l.Status == LobbyStatus.Error)
                    .Where(l => DateTime.UtcNow - l.CreatedAt > TimeSpan.FromMinutes(1))
                    .ToList();

                foreach (var lobby in errorLobbies)
                {
                    _worldLobbies.Remove(lobby.LobbyId);
                    _logger.LogInformation("Removed error lobby {LobbyId}", lobby.LobbyId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up lobbies");
        }
    }

    // =============================================
    // STATISTICS AND DIAGNOSTICS
    // =============================================

    /// <summary>
    /// Get lobby statistics for monitoring and admin dashboard.
    /// </summary>
    public Dictionary<string, object> GetLobbyStats()
    {
        lock (_lobbiesLock)
        {
            var totalLobbies = _worldLobbies.Count;
            var totalLobbyPlayers = _worldLobbies.Values.Sum(l => l.TotalPlayers);

            return new Dictionary<string, object>
            {
                ["TotalLobbies"] = totalLobbies,
                ["LobbyPlayers"] = totalLobbyPlayers,
                ["LobbiesByStatus"] = _worldLobbies.Values
                    .GroupBy(l => l.Status.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                ["AveragePlayersPerLobby"] = totalLobbies > 0 ? (double)totalLobbyPlayers / totalLobbies : 0,
                ["WaitingLobbies"] = _worldLobbies.Values.Count(l => l.Status == LobbyStatus.WaitingForPlayers),
                ["StartingLobbies"] = _worldLobbies.Values.Count(l => l.Status == LobbyStatus.Starting)
            };
        }
    }

    // =============================================
    // DISPOSAL
    // =============================================

    public void Dispose()
    {
        _lobbyCleanupTimer?.Dispose();
        _lobbyStartTimer?.Dispose();
        _logger.LogInformation("LobbyManager disposed");
    }
}

/// <summary>
/// Lobby information for client display.
/// </summary>
public class LobbyInfo
{
    public string LobbyId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CurrentPlayers { get; set; }
    public int MinPlayersToStart { get; set; }
    public int MaxPlayers { get; set; }
    public DateTime LastPlayerJoined { get; set; }
    public Dictionary<string, int> TeamCounts { get; set; } = new();
}
