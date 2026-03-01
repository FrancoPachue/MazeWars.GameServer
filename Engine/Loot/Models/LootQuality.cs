namespace MazeWars.GameServer.Engine.Loot.Models;

public enum ItemRarity
{
    Trash = 0,       // Gris    — 1.0x stats
    Common = 1,      // Blanco  — 1.3x
    Uncommon = 2,    // Verde   — 1.7x
    Rare = 3,        // Azul    — 2.2x
    Epic = 4,        // Morado  — 2.8x
    Legendary = 5,   // Naranja — 3.5x
    Mythic = 6       // Dorado  — 4.5x
}

public enum ItemQualityTier
{
    Normal = 0,       // ×0.85
    Good = 1,         // ×0.95
    Outstanding = 2,  // ×1.05
    Excellent = 3,    // ×1.15
    Masterpiece = 4   // ×1.30
}

/// <summary>
/// Probabilistic rarity roll system. Room type and mob type shift weights
/// from Normal toward higher rarities. Nothing is ever guaranteed.
/// Also provides discrete quality tier rolls with meaningful stat variance.
/// </summary>
public static class ItemRaritySystem
{
    // Stat multipliers per rarity (Trash→Mythic)
    private static readonly float[] StatMultipliers =
        [1.0f, 1.3f, 1.7f, 2.2f, 2.8f, 3.5f, 4.5f];

    // Quality tier multipliers (Normal→Masterpiece)
    private static readonly float[] QualityMultipliers =
        [0.85f, 0.95f, 1.05f, 1.15f, 1.30f];

    // Quality tier drop weights (must sum to 1.0)
    private static readonly float[] QualityWeights =
        [0.50f, 0.25f, 0.15f, 0.08f, 0.02f];

    // Quality tier display names
    private static readonly string[] QualityNames =
        ["Normal", "Good", "Outstanding", "Excellent", "Masterpiece"];

    // Rarity display names
    private static readonly string[] RarityNames =
        ["Trash", "Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];

    // Base rarity drop weights (must sum to 1.0)
    private static readonly float[] BaseWeights =
        [0.60f, 0.22f, 0.10f, 0.05f, 0.02f, 0.008f, 0.002f];

    // Room type loot shift values (higher = shifts weight toward rare)
    private static readonly Dictionary<string, float> RoomShifts = new()
    {
        ["empty"] = 0f,
        ["patrol"] = 0f,
        ["guard_post"] = 0.05f,
        ["ambush"] = 0.03f,
        ["elite_chamber"] = 0.15f,
        ["boss_arena"] = 0.25f,
        ["treasure_vault"] = 0.20f,
    };

    // Mob type loot shift values
    private static readonly Dictionary<string, float> MobShifts = new()
    {
        ["patrol"] = 0f,
        ["guard"] = 0.08f,
        ["elite"] = 0.18f,
        ["boss"] = 0.30f,
    };

    public static float GetStatMultiplier(int rarity) =>
        rarity >= 0 && rarity < StatMultipliers.Length ? StatMultipliers[rarity] : 1.0f;

    public static float GetStatMultiplier(ItemRarity rarity) =>
        GetStatMultiplier((int)rarity);

    public static float GetQualityMultiplier(int qualityTier) =>
        qualityTier >= 0 && qualityTier < QualityMultipliers.Length ? QualityMultipliers[qualityTier] : 1.0f;

    public static string GetQualityName(int qualityTier) =>
        qualityTier >= 0 && qualityTier < QualityNames.Length ? QualityNames[qualityTier] : "";

    public static string GetRarityName(int rarity) =>
        rarity >= 0 && rarity < RarityNames.Length ? RarityNames[rarity] : "";

    public static float GetRoomShift(string roomType) =>
        RoomShifts.GetValueOrDefault(roomType.ToLower(), 0f);

    public static float GetMobShift(string mobType) =>
        MobShifts.GetValueOrDefault(mobType.ToLower(), 0f);

    /// <summary>
    /// Roll a quality tier using weighted random (Normal 50%, Good 25%, Outstanding 15%, Excellent 8%, Masterpiece 2%).
    /// </summary>
    public static ItemQualityTier RollQualityTier(Random random)
    {
        var roll = (float)random.NextDouble();
        var cumulative = 0f;
        for (int i = 0; i < QualityWeights.Length; i++)
        {
            cumulative += QualityWeights[i];
            if (roll <= cumulative)
                return (ItemQualityTier)i;
        }
        return ItemQualityTier.Normal;
    }

    /// <summary>
    /// Roll a rarity based on combined shift from room + mob + bonus.
    /// Shift redistributes weight from Normal toward higher rarities.
    /// </summary>
    public static ItemRarity RollRarity(Random random, float totalShift = 0f)
    {
        var weights = new float[BaseWeights.Length];
        Array.Copy(BaseWeights, weights, BaseWeights.Length);

        if (totalShift > 0f)
        {
            // Take weight from Normal and distribute proportionally to higher rarities
            var normalReduction = Math.Min(weights[0] * 0.85f, weights[0] * totalShift);
            weights[0] -= normalReduction;

            // Distribute to higher rarities with diminishing shares
            // Each rarity gets a proportional share based on its base weight
            var totalHigherWeight = 0f;
            for (int i = 1; i < weights.Length; i++)
                totalHigherWeight += BaseWeights[i];

            for (int i = 1; i < weights.Length; i++)
            {
                var share = BaseWeights[i] / totalHigherWeight;
                weights[i] += normalReduction * share;
            }
        }

        // Roll
        var roll = (float)random.NextDouble();
        var cumulative = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
                return (ItemRarity)i;
        }

        return ItemRarity.Trash;
    }

    /// <summary>
    /// Roll rarity using room type and mob type as modifiers.
    /// </summary>
    public static ItemRarity RollRarity(Random random, string roomType, string mobType, float bonusShift = 0f)
    {
        var totalShift = GetRoomShift(roomType) + GetMobShift(mobType) + bonusShift;
        return RollRarity(random, totalShift);
    }
}
