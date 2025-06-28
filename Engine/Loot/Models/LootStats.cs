
// =============================================
// LOOT STATISTICS
// =============================================

public class LootStats
{
    public int TotalLootSpawned { get; set; }
    public int TotalLootTaken { get; set; }
    public int TotalLootExpired { get; set; }
    public int CurrentActiveLoot { get; set; }
    public Dictionary<string, int> LootByType { get; set; } = new();
    public Dictionary<int, int> LootByRarity { get; set; } = new();
    public Dictionary<string, int> LootByRoom { get; set; } = new();
    public float AverageTimeToPickup { get; set; }
    public string MostPopularItem { get; set; } = string.Empty;
    public string RarestItemDropped { get; set; } = string.Empty;
    public DateTime LastStatsReset { get; set; } = DateTime.UtcNow;
}
