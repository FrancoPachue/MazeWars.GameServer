using MazeWars.GameServer.Engine.Loot.Models;

// =============================================
// LOOT DISTRIBUTION WEIGHTS
// =============================================

public class LootDistribution
{
    public Dictionary<ItemRarity, float> QualityWeights { get; set; } = new()
    {
        { ItemRarity.Trash, 0.4f },
        { ItemRarity.Common, 0.3f },
        { ItemRarity.Uncommon, 0.2f },
        { ItemRarity.Rare, 0.08f },
        { ItemRarity.Epic, 0.015f },
        { ItemRarity.Legendary, 0.004f },
        { ItemRarity.Mythic, 0.001f }
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
