using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Loot.Interface;
using MazeWars.GameServer.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MazeWars.GameServer.Engine.Managers;

/// <summary>
/// ⭐ REFACTORED: Manages active game worlds, world generation, and world lifecycle.
/// Extracted from GameEngine to reduce complexity and improve maintainability.
/// </summary>
public class WorldManager
{
    private readonly ILogger<WorldManager> _logger;
    private readonly GameServerSettings _settings;
    private readonly ILootSystem _lootSystem;
    private readonly Dictionary<string, GameWorld> _worlds = new();
    private readonly Dictionary<string, LootTable> _baseLootTables = new();
    private readonly object _worldsLock = new object();
    private readonly Random _random = new();

    public WorldManager(
        ILogger<WorldManager> _logger,
        IOptions<GameServerSettings> settings,
        ILootSystem lootSystem)
    {
        this._logger = _logger;
        _settings = settings.Value;
        _lootSystem = lootSystem;

        // Initialize loot tables
        InitializeLootTables();

        _logger.LogInformation("WorldManager initialized with {WorldSizeX}x{WorldSizeY} grid",
            _settings.WorldGeneration.WorldSizeX, _settings.WorldGeneration.WorldSizeY);
    }

    // =============================================
    // WORLD GENERATION
    // =============================================

    /// <summary>
    /// Create a new game world with rooms, extraction points, and initial loot.
    /// </summary>
    public GameWorld CreateWorld(string worldId, Dictionary<string, RealTimePlayer> lobbyPlayers)
    {
        lock (_worldsLock)
        {
            var world = new GameWorld
            {
                WorldId = worldId,
                Rooms = GenerateWorldRooms(),
                ExtractionPoints = GenerateExtractionPoints(),
                LootTables = new List<LootTable>(_baseLootTables.Values),
                CreatedAt = DateTime.UtcNow,
                Players = new ConcurrentDictionary<string, RealTimePlayer>(lobbyPlayers)
            };

            SpawnInitialLoot(world);

            _worlds[worldId] = world;

            _logger.LogInformation("Created world {WorldId} with {RoomCount} rooms, {PlayerCount} players",
                worldId, world.Rooms.Count, world.Players.Count);

            return world;
        }
    }

    /// <summary>
    /// Generate rooms in a grid layout with connections.
    /// </summary>
    private Dictionary<string, Room> GenerateWorldRooms()
    {
        var rooms = new Dictionary<string, Room>();
        var settings = _settings.WorldGeneration;

        for (int x = 0; x < settings.WorldSizeX; x++)
        {
            for (int y = 0; y < settings.WorldSizeY; y++)
            {
                var roomId = $"room_{x}_{y}";
                var room = new Room
                {
                    RoomId = roomId,
                    Position = new Vector2(x * settings.RoomSpacing, y * settings.RoomSpacing),
                    Size = new Vector2(settings.RoomSizeX, settings.RoomSizeY),
                    Connections = new List<string>()
                };

                // Add connections to adjacent rooms
                if (x > 0) room.Connections.Add($"room_{x - 1}_{y}");
                if (x < settings.WorldSizeX - 1) room.Connections.Add($"room_{x + 1}_{y}");
                if (y > 0) room.Connections.Add($"room_{x}_{y - 1}");
                if (y < settings.WorldSizeY - 1) room.Connections.Add($"room_{x}_{y + 1}");

                rooms[roomId] = room;
            }
        }

        _logger.LogDebug("Generated {RoomCount} rooms for world", rooms.Count);
        return rooms;
    }

    /// <summary>
    /// Generate extraction points in the corners of the world.
    /// </summary>
    private Dictionary<string, ExtractionPoint> GenerateExtractionPoints()
    {
        var extractionPoints = new Dictionary<string, ExtractionPoint>();
        var settings = _settings.WorldGeneration;

        var cornerPositions = new[]
        {
            new { Id = "extract_0_0", Position = new Vector2(0, 0), RoomId = "room_0_0" },
            new { Id = "extract_3_0", Position = new Vector2((settings.WorldSizeX - 1) * settings.RoomSpacing, 0), RoomId = $"room_{settings.WorldSizeX - 1}_0" },
            new { Id = "extract_0_3", Position = new Vector2(0, (settings.WorldSizeY - 1) * settings.RoomSpacing), RoomId = $"room_0_{settings.WorldSizeY - 1}" },
            new { Id = "extract_3_3", Position = new Vector2((settings.WorldSizeX - 1) * settings.RoomSpacing, (settings.WorldSizeY - 1) * settings.RoomSpacing), RoomId = $"room_{settings.WorldSizeX - 1}_{settings.WorldSizeY - 1}" }
        };

        foreach (var corner in cornerPositions)
        {
            extractionPoints[corner.Id] = new ExtractionPoint
            {
                ExtractionId = corner.Id,
                Position = corner.Position,
                RoomId = corner.RoomId,
                IsActive = false,
                ExtractionTimeSeconds = _settings.GameBalance.ExtractionTimeSeconds
            };
        }

        _logger.LogDebug("Generated {Count} extraction points", extractionPoints.Count);
        return extractionPoints;
    }

    /// <summary>
    /// Spawn initial loot in random rooms using the LootSystem.
    /// </summary>
    private void SpawnInitialLoot(GameWorld world)
    {
        var settings = _settings.WorldGeneration;
        var lootCount = settings.InitialLootCount;

        for (int i = 0; i < lootCount; i++)
        {
            var room = world.Rooms.Values.ElementAt(_random.Next(world.Rooms.Count));
            var lootTable = _baseLootTables.Values.ElementAt(_random.Next(_baseLootTables.Count));

            _lootSystem.SpawnLootFromTable(world, room, lootTable);
        }

        _logger.LogInformation("Spawned {Count} initial loot items in world {WorldId}",
            lootCount, world.WorldId);
    }

    /// <summary>
    /// Initialize base loot tables from configuration.
    /// </summary>
    private void InitializeLootTables()
    {
        // Common loot table
        var commonTable = new LootTable
        {
            TableId = "common",
            Items = new List<LootTableEntry>
            {
                new() { ItemName = "Health Potion", Weight = 40, Rarity = 1 },
                new() { ItemName = "Mana Potion", Weight = 35, Rarity = 1 },
                new() { ItemName = "Basic Sword", Weight = 15, Rarity = 1 },
                new() { ItemName = "Leather Armor", Weight = 10, Rarity = 1 }
            }
        };

        // Rare loot table
        var rareTable = new LootTable
        {
            TableId = "rare",
            Items = new List<LootTableEntry>
            {
                new() { ItemName = "Magic Ring", Weight = 30, Rarity = 3 },
                new() { ItemName = "Enchanted Bow", Weight = 25, Rarity = 3 },
                new() { ItemName = "Steel Plate", Weight = 25, Rarity = 2 },
                new() { ItemName = "Speed Boots", Weight = 20, Rarity = 2 }
            }
        };

        _baseLootTables["common"] = commonTable;
        _baseLootTables["rare"] = rareTable;

        _logger.LogInformation("Initialized {Count} loot tables", _baseLootTables.Count);
    }

    // =============================================
    // WORLD ACCESS AND QUERIES
    // =============================================

    /// <summary>
    /// Get a world by ID.
    /// </summary>
    public GameWorld? GetWorld(string worldId)
    {
        lock (_worldsLock)
        {
            return _worlds.TryGetValue(worldId, out var world) ? world : null;
        }
    }

    /// <summary>
    /// Get all active worlds.
    /// </summary>
    public List<GameWorld> GetAllWorlds()
    {
        lock (_worldsLock)
        {
            return _worlds.Values.ToList();
        }
    }

    /// <summary>
    /// Get world IDs that have available space.
    /// </summary>
    public List<string> GetAvailableWorlds(int maxPlayersPerWorld)
    {
        lock (_worldsLock)
        {
            return _worlds.Values
                .Where(w => w.Players.Count < maxPlayersPerWorld)
                .Select(w => w.WorldId)
                .ToList();
        }
    }

    /// <summary>
    /// Find world containing a specific player.
    /// </summary>
    public GameWorld? FindWorldByPlayer(string playerId)
    {
        lock (_worldsLock)
        {
            return _worlds.Values.FirstOrDefault(w => w.Players.ContainsKey(playerId));
        }
    }

    /// <summary>
    /// Find world ID containing a specific player.
    /// </summary>
    public string? FindWorldIdByPlayer(string playerId)
    {
        lock (_worldsLock)
        {
            return _worlds.Values
                .FirstOrDefault(w => w.Players.ContainsKey(playerId))
                ?.WorldId;
        }
    }

    /// <summary>
    /// Find player across all worlds.
    /// </summary>
    public RealTimePlayer? FindPlayer(string playerId)
    {
        lock (_worldsLock)
        {
            foreach (var world in _worlds.Values)
            {
                if (world.Players.TryGetValue(playerId, out var player))
                {
                    return player;
                }
            }
            return null;
        }
    }

    // =============================================
    // WORLD MODIFICATION
    // =============================================

    /// <summary>
    /// Add a player to an existing world.
    /// </summary>
    public bool AddPlayerToWorld(string worldId, RealTimePlayer player)
    {
        lock (_worldsLock)
        {
            if (!_worlds.TryGetValue(worldId, out var world))
                return false;

            world.Players[player.PlayerId] = player;
            _logger.LogInformation("Added player {PlayerName} to world {WorldId}",
                player.PlayerName, worldId);
            return true;
        }
    }

    /// <summary>
    /// Remove a player from their world.
    /// </summary>
    public bool RemovePlayerFromWorld(string worldId, string playerId)
    {
        lock (_worldsLock)
        {
            if (!_worlds.TryGetValue(worldId, out var world))
                return false;

            if (world.Players.TryRemove(playerId, out var player))
            {
                _logger.LogInformation("Removed player {PlayerName} from world {WorldId}",
                    player.PlayerName, worldId);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Remove a world (e.g., when game ends).
    /// </summary>
    public bool RemoveWorld(string worldId)
    {
        lock (_worldsLock)
        {
            if (_worlds.Remove(worldId))
            {
                _logger.LogInformation("Removed world {WorldId}", worldId);
                return true;
            }
            return false;
        }
    }

    // =============================================
    // HELPER METHODS
    // =============================================

    /// <summary>
    /// Get spawn position based on team ID.
    /// </summary>
    public Vector2 GetTeamSpawnPosition(string teamId)
    {
        var settings = _settings.WorldGeneration;
        var spacing = settings.RoomSpacing;
        var maxX = (settings.WorldSizeX - 1) * spacing;
        var maxY = (settings.WorldSizeY - 1) * spacing;

        return teamId.ToLower() switch
        {
            "team1" => new Vector2(spacing * 0.2f, spacing * 0.2f),
            "team2" => new Vector2(maxX - spacing * 0.2f, spacing * 0.2f),
            "team3" => new Vector2(spacing * 0.2f, maxY - spacing * 0.2f),
            "team4" => new Vector2(maxX - spacing * 0.2f, maxY - spacing * 0.2f),
            _ => new Vector2(maxX / 2, maxY / 2)
        };
    }

    /// <summary>
    /// Check if a room is in a corner position.
    /// </summary>
    public bool IsCornerRoom(string roomId)
    {
        var settings = _settings.WorldGeneration;
        var cornerRooms = new[]
        {
            "room_0_0",
            $"room_{settings.WorldSizeX - 1}_0",
            $"room_0_{settings.WorldSizeY - 1}",
            $"room_{settings.WorldSizeX - 1}_{settings.WorldSizeY - 1}"
        };

        return cornerRooms.Contains(roomId);
    }

    // =============================================
    // STATISTICS AND DIAGNOSTICS
    // =============================================

    /// <summary>
    /// Get world statistics for monitoring.
    /// </summary>
    public Dictionary<string, object> GetWorldStats()
    {
        lock (_worldsLock)
        {
            var totalWorlds = _worlds.Count;
            var totalPlayers = _worlds.Values.Sum(w => w.Players.Count);
            var totalMobs = _worlds.Values.Sum(w => w.Mobs.Count);

            return new Dictionary<string, object>
            {
                ["TotalWorlds"] = totalWorlds,
                ["TotalPlayers"] = totalPlayers,
                ["TotalMobs"] = totalMobs,
                ["AveragePlayersPerWorld"] = totalWorlds > 0 ? (double)totalPlayers / totalWorlds : 0,
                ["AverageMobsPerWorld"] = totalWorlds > 0 ? (double)totalMobs / totalWorlds : 0,
                ["WorldIds"] = _worlds.Keys.ToList()
            };
        }
    }

    /// <summary>
    /// Get detailed state of a specific world.
    /// </summary>
    public Dictionary<string, object>? GetWorldState(string worldId)
    {
        lock (_worldsLock)
        {
            if (!_worlds.TryGetValue(worldId, out var world))
                return null;

            return new Dictionary<string, object>
            {
                ["WorldId"] = world.WorldId,
                ["PlayerCount"] = world.Players.Count,
                ["MobCount"] = world.Mobs.Count,
                ["RoomCount"] = world.Rooms.Count,
                ["ExtractionPointsActive"] = world.ExtractionPoints.Values.Count(e => e.IsActive),
                ["CreatedAt"] = world.CreatedAt,
                ["IsCompleted"] = world.IsCompleted
            };
        }
    }

    // =============================================
    // DISPOSAL
    // =============================================

    public void Dispose()
    {
        lock (_worldsLock)
        {
            _worlds.Clear();
        }
        _logger.LogInformation("WorldManager disposed");
    }
}
