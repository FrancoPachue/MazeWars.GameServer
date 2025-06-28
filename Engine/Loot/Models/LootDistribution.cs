
// =============================================
// LOOT DISTRIBUTION WEIGHTS
// =============================================

public class LootDistribution
{
    public Dictionary<LootQuality, float> QualityWeights { get; set; } = new()
    {
        { LootQuality.Trash, 0.4f },
        { LootQuality.Common, 0.3f },
        { LootQuality.Uncommon, 0.2f },
        { LootQuality.Rare, 0.08f },
        { LootQuality.Epic, 0.015f },
        { LootQuality.Legendary, 0.004f },
        { LootQuality.Mythic, 0.001f }
    };

    public Dictionary<ItemCategory, float> CategoryWeights { get; set; } = new()
    {
        { ItemCategory.Weapon, 0.25f },
        { ItemCategory.Armor, 0.2f },
        { ItemCategory.Consumable, 0.35f },
        { ItemCategory.Key, 0.05f },
        { ItemCategory.Material, 0.1f },
        { ItemCategory.Currency, 0.05f }
    };
}
