
// =============================================
// LOOT CONFIGURATION
// =============================================

public class LootSettings
{
    public float GlobalDropRateMultiplier { get; set; } = 1.0f;
    public int MaxLootPerRoom { get; set; } = 5;
    public int LootExpirationTimeMinutes { get; set; } = 10;
    public float LootRespawnIntervalSeconds { get; set; } = 30.0f;
    public float LootGrabRange { get; set; } = 3.0f;
    public bool EnableDynamicRarity { get; set; } = true;
    public float LuckStatMultiplier { get; set; } = 0.1f;
    public int MaxDropsPerMob { get; set; } = 2;
    public int MaxDropsPerRoom { get; set; } = 5;
    public bool EnableLootMagnetism { get; set; } = false;
    public float MagnetismRange { get; set; } = 1.5f;
}
