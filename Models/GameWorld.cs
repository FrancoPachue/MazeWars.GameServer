using System.Collections.Concurrent;
using MazeWars.GameServer.Configuration;

namespace MazeWars.GameServer.Models;

public class GameWorld
{
    public string WorldId { get; set; } = string.Empty;
    public string GameMode { get; set; } = "trios";
    public string DifficultyTier { get; set; } = "normal";
    public GameModeConfig? ModeConfig { get; set; }
    public ConcurrentDictionary<string, RealTimePlayer> Players { get; set; } = new();
    public ConcurrentDictionary<string, Room> Rooms { get; set; } = new();
    public Dictionary<string, ExtractionPoint> ExtractionPoints { get; set; } = new();
    public ConcurrentDictionary<string, LootItem> AvailableLoot { get; set; } = new();
    public ConcurrentDictionary<string, LootContainer> LootContainers { get; set; } = new();
    public ConcurrentDictionary<string, Mob> Mobs { get; set; } = new();
    public ConcurrentDictionary<string, LockedDoor> LockedDoors { get; set; } = new();
    public ConcurrentDictionary<string, RoomDoor> Doors { get; set; } = new();
    public ConcurrentDictionary<string, RevivalAltar> RevivalAltars { get; set; } = new();
    public List<LootTable> LootTables { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string WinningTeam { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsLobbyWorld { get; set; }
    public int TotalLootSpawned { get; set; } = 0;
    public DateTime LastLootSpawn { get; set; } = DateTime.UtcNow;

    // ── Corruption Zone ──
    public int CorruptionWave { get; set; }
    public DateTime CorruptionStartTime { get; set; }
    public DateTime NextWaveTime { get; set; }
    public int GridCenterX { get; set; }
    public int GridCenterY { get; set; }
    public int MaxChebyshevDistance { get; set; }
    /// <summary>Thread-safe: use lock(CorruptionLock) when iterating.</summary>
    public HashSet<string> CorruptedRooms { get; set; } = new();
    /// <summary>Thread-safe: use lock(CorruptionLock) when iterating.</summary>
    public HashSet<string> WarningRooms { get; set; } = new();
    public readonly object CorruptionLock = new();
    public DateTime LastCorruptionDamageTick { get; set; }

    // ── Empty World Grace Period ──
    /// <summary>When the last player disconnected. Null if players are present.</summary>
    public DateTime? EmptySince { get; set; }

    // ── Match End ──
    public bool MatchEnded { get; set; }
    public DateTime? MatchEndTime { get; set; }
    public string MatchEndReason { get; set; } = string.Empty;

    // ── Win Condition Tracking ──
    public DateTime GameStartedAt { get; set; } = DateTime.UtcNow;
    public int InitialTeamCount { get; set; }
}
