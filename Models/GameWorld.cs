using System.Collections.Concurrent;

namespace MazeWars.GameServer.Models;

public class GameWorld
{
    public string WorldId { get; set; } = string.Empty;
    public ConcurrentDictionary<string, RealTimePlayer> Players { get; set; } = new();
    public Dictionary<string, Room> Rooms { get; set; } = new();
    public Dictionary<string, ExtractionPoint> ExtractionPoints { get; set; } = new();
    public ConcurrentDictionary<string, LootItem> AvailableLoot { get; set; } = new();
    public ConcurrentDictionary<string, Mob> Mobs { get; set; } = new();
    public List<LootTable> LootTables { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string WinningTeam { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int TotalLootSpawned { get; set; } = 0;
    public DateTime LastLootSpawn { get; set; } = DateTime.UtcNow;
}
