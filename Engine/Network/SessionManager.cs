using MazeWars.GameServer.Models;
using System.Collections.Concurrent;

namespace MazeWars.GameServer.Engine.Network;

/// <summary>
/// Manages player sessions for reconnection handling.
/// Stores player state when disconnected and allows rejoin within TTL window.
/// </summary>
public class SessionManager
{
    private readonly ConcurrentDictionary<string, PlayerSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;
    private readonly TimeSpan _sessionTTL;

    public SessionManager(ILogger<SessionManager> logger, TimeSpan? sessionTTL = null)
    {
        _logger = logger;
        _sessionTTL = sessionTTL ?? TimeSpan.FromMinutes(5); // Default: 5 minutes to reconnect
    }

    /// <summary>
    /// Creates a new session for a player on initial connection.
    /// Returns session token that client can use to reconnect.
    /// </summary>
    public string CreateSession(RealTimePlayer player, string worldId, bool isInLobby)
    {
        var sessionToken = Guid.NewGuid().ToString();

        var session = new PlayerSession
        {
            SessionToken = sessionToken,
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            WorldId = worldId,
            IsInLobby = isInLobby,
            PlayerState = null, // No saved state on initial connect
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_sessionTTL),
            IsActive = true
        };

        _sessions[sessionToken] = session;

        _logger.LogInformation("Created session {SessionToken} for player {PlayerName} in world {WorldId}",
            sessionToken, player.PlayerName, worldId);

        return sessionToken;
    }

    /// <summary>
    /// Saves player state when they disconnect (timeout or explicit disconnect).
    /// This allows them to reconnect and restore their state.
    /// </summary>
    public void SavePlayerState(string sessionToken, RealTimePlayer player, string worldId, bool isInLobby)
    {
        if (!_sessions.TryGetValue(sessionToken, out var session))
        {
            _logger.LogWarning("Attempted to save state for non-existent session {SessionToken}", sessionToken);
            return;
        }

        // Serialize player state for reconnection
        session.PlayerState = new SavedPlayerState
        {
            // Identity
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            TeamId = player.TeamId,
            PlayerClass = player.PlayerClass,

            // Position & Movement
            Position = player.Position,
            Velocity = player.Velocity,
            Direction = player.Direction,
            CurrentRoomId = player.CurrentRoomId,

            // Combat & Stats
            IsAlive = player.IsAlive,
            Health = player.Health,
            MaxHealth = player.MaxHealth,
            Mana = player.Mana,
            MaxMana = player.MaxMana,
            Shield = player.Shield,
            MaxShield = player.MaxShield,

            // Progression
            Level = player.Level,
            ExperiencePoints = player.ExperiencePoints,
            Stats = new Dictionary<string, int>(player.Stats),

            // Inventory (deep copy)
            Inventory = player.Inventory.Select(item => new LootItem
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                ItemType = item.ItemType,
                Rarity = item.Rarity,
                Stats = new Dictionary<string, int>(item.Stats),
                Value = item.Value
            }).ToList(),

            // Active effects
            StatusEffects = player.StatusEffects.Select(effect => new StatusEffect
            {
                EffectType = effect.EffectType,
                Value = effect.Value,
                Duration = effect.Duration,
                AppliedAt = effect.AppliedAt
            }).ToList(),

            SavedAt = DateTime.UtcNow
        };

        session.WorldId = worldId;
        session.IsInLobby = isInLobby;
        session.IsActive = false; // Player disconnected
        session.ExpiresAt = DateTime.UtcNow.Add(_sessionTTL);

        _logger.LogInformation("Saved state for session {SessionToken}, player {PlayerName}. Expires at {ExpiresAt}",
            sessionToken, player.PlayerName, session.ExpiresAt);
    }

    /// <summary>
    /// Validates reconnection attempt and returns saved session if valid.
    /// </summary>
    public (bool Success, PlayerSession? Session, string ErrorMessage) ValidateReconnection(string sessionToken)
    {
        if (string.IsNullOrEmpty(sessionToken))
        {
            return (false, null, "Session token is required");
        }

        if (!_sessions.TryGetValue(sessionToken, out var session))
        {
            _logger.LogWarning("Reconnection attempt with invalid session token: {SessionToken}", sessionToken);
            return (false, null, "Invalid session token");
        }

        // Check if session expired
        if (DateTime.UtcNow > session.ExpiresAt)
        {
            _logger.LogWarning("Reconnection attempt with expired session {SessionToken}. Expired at {ExpiresAt}",
                sessionToken, session.ExpiresAt);

            _sessions.TryRemove(sessionToken, out _);
            return (false, null, "Session expired. Please create a new game.");
        }

        // Check if player already reconnected (session is active)
        if (session.IsActive)
        {
            _logger.LogWarning("Reconnection attempt with already active session {SessionToken}", sessionToken);
            return (false, null, "Session is already active");
        }

        // Check if we have saved state (can't reconnect without state)
        if (session.PlayerState == null)
        {
            _logger.LogWarning("Reconnection attempt but no saved state for session {SessionToken}", sessionToken);
            return (false, null, "No saved state available");
        }

        _logger.LogInformation("Validated reconnection for session {SessionToken}, player {PlayerName}",
            sessionToken, session.PlayerName);

        return (true, session, string.Empty);
    }

    /// <summary>
    /// Marks session as active after successful reconnection.
    /// </summary>
    public void ActivateSession(string sessionToken)
    {
        if (_sessions.TryGetValue(sessionToken, out var session))
        {
            session.IsActive = true;
            session.LastActivity = DateTime.UtcNow;

            _logger.LogInformation("Activated session {SessionToken} for player {PlayerName}",
                sessionToken, session.PlayerName);
        }
    }

    /// <summary>
    /// Updates last activity timestamp for keepalive.
    /// </summary>
    public void UpdateActivity(string sessionToken)
    {
        if (_sessions.TryGetValue(sessionToken, out var session))
        {
            session.LastActivity = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Invalidates a session (explicit disconnect, kicked, banned, etc.)
    /// </summary>
    public void InvalidateSession(string sessionToken, string reason)
    {
        if (_sessions.TryRemove(sessionToken, out var session))
        {
            _logger.LogInformation("Invalidated session {SessionToken} for player {PlayerName}. Reason: {Reason}",
                sessionToken, session.PlayerName, reason);
        }
    }

    /// <summary>
    /// Gets session token by player ID (for lookups).
    /// </summary>
    public string? GetSessionTokenByPlayerId(string playerId)
    {
        return _sessions.FirstOrDefault(kvp => kvp.Value.PlayerId == playerId).Key;
    }

    /// <summary>
    /// Cleanup expired sessions (run periodically).
    /// </summary>
    public int CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var expiredSessions = _sessions
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        int removedCount = 0;
        foreach (var sessionToken in expiredSessions)
        {
            if (_sessions.TryRemove(sessionToken, out var session))
            {
                _logger.LogDebug("Cleaned up expired session {SessionToken} for player {PlayerName}",
                    sessionToken, session.PlayerName);
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired sessions", removedCount);
        }

        return removedCount;
    }

    /// <summary>
    /// Gets statistics about active sessions.
    /// </summary>
    public SessionStats GetStats()
    {
        var now = DateTime.UtcNow;
        var sessions = _sessions.Values.ToList();

        return new SessionStats
        {
            TotalSessions = sessions.Count,
            ActiveSessions = sessions.Count(s => s.IsActive),
            InactiveSessions = sessions.Count(s => !s.IsActive),
            ExpiredSessions = sessions.Count(s => s.ExpiresAt < now),
            LobbySessions = sessions.Count(s => s.IsInLobby),
            GameSessions = sessions.Count(s => !s.IsInLobby),
            AverageSessionAge = sessions.Any()
                ? TimeSpan.FromSeconds(sessions.Average(s => (now - s.CreatedAt).TotalSeconds))
                : TimeSpan.Zero
        };
    }
}

/// <summary>
/// Represents a player session for reconnection handling.
/// </summary>
public class PlayerSession
{
    public required string SessionToken { get; set; }
    public required string PlayerId { get; set; }
    public required string PlayerName { get; set; }
    public required string WorldId { get; set; }
    public required bool IsInLobby { get; set; }
    public SavedPlayerState? PlayerState { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required DateTime LastActivity { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required bool IsActive { get; set; }
}

/// <summary>
/// Saved player state for reconnection.
/// </summary>
public class SavedPlayerState
{
    // Identity
    public required string PlayerId { get; set; }
    public required string PlayerName { get; set; }
    public required string TeamId { get; set; }
    public required string PlayerClass { get; set; }

    // Position & Movement
    public required Vector2 Position { get; set; }
    public required Vector2 Velocity { get; set; }
    public required float Direction { get; set; }
    public required string CurrentRoomId { get; set; }

    // Combat & Stats
    public required bool IsAlive { get; set; }
    public required int Health { get; set; }
    public required int MaxHealth { get; set; }
    public required int Mana { get; set; }
    public required int MaxMana { get; set; }
    public required int Shield { get; set; }
    public required int MaxShield { get; set; }

    // Progression
    public required int Level { get; set; }
    public required int ExperiencePoints { get; set; }
    public required Dictionary<string, int> Stats { get; set; }

    // Inventory
    public required List<LootItem> Inventory { get; set; }

    // Active effects
    public required List<StatusEffect> StatusEffects { get; set; }

    public required DateTime SavedAt { get; set; }
}

/// <summary>
/// Session statistics.
/// </summary>
public class SessionStats
{
    public int TotalSessions { get; set; }
    public int ActiveSessions { get; set; }
    public int InactiveSessions { get; set; }
    public int ExpiredSessions { get; set; }
    public int LobbySessions { get; set; }
    public int GameSessions { get; set; }
    public TimeSpan AverageSessionAge { get; set; }
}
