namespace MazeWars.GameServer.Engine.Loot.Models;

/// <summary>
/// Represents a rolled affix on an equipment item.
/// Affixes modify stats and are stored in LootItem.Properties["affixes"].
/// </summary>
public class ItemAffix
{
    public string AffixId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AffixType Type { get; set; }
    public string StatKey { get; set; } = string.Empty;
    public float Value { get; set; }
    public int MinRarity { get; set; } // Minimum item rarity to roll this affix
}

public enum AffixType
{
    Prefix,
    Suffix
}

/// <summary>
/// Static affix generation system. Rolls random prefixes/suffixes based on item rarity.
/// </summary>
public static class AffixSystem
{
    private static readonly ItemAffix[] PrefixPool = new[]
    {
        // Damage affixes
        new ItemAffix { AffixId = "sharp", DisplayName = "Sharp", Type = AffixType.Prefix, StatKey = "bonus_damage_pct", Value = 0.08f, MinRarity = 2 },
        new ItemAffix { AffixId = "keen", DisplayName = "Keen", Type = AffixType.Prefix, StatKey = "bonus_damage_pct", Value = 0.15f, MinRarity = 3 },
        new ItemAffix { AffixId = "vicious", DisplayName = "Vicious", Type = AffixType.Prefix, StatKey = "bonus_damage_pct", Value = 0.22f, MinRarity = 4 },
        // Health affixes
        new ItemAffix { AffixId = "sturdy", DisplayName = "Sturdy", Type = AffixType.Prefix, StatKey = "bonus_health", Value = 15, MinRarity = 1 },
        new ItemAffix { AffixId = "fortified", DisplayName = "Fortified", Type = AffixType.Prefix, StatKey = "bonus_health", Value = 30, MinRarity = 3 },
        new ItemAffix { AffixId = "titanic", DisplayName = "Titanic", Type = AffixType.Prefix, StatKey = "bonus_health", Value = 50, MinRarity = 5 },
        // Speed affixes
        new ItemAffix { AffixId = "swift", DisplayName = "Swift", Type = AffixType.Prefix, StatKey = "bonus_speed_pct", Value = 0.05f, MinRarity = 2 },
        new ItemAffix { AffixId = "fleet", DisplayName = "Fleet", Type = AffixType.Prefix, StatKey = "bonus_speed_pct", Value = 0.10f, MinRarity = 4 },
        // Defense affixes
        new ItemAffix { AffixId = "hardened", DisplayName = "Hardened", Type = AffixType.Prefix, StatKey = "bonus_dr", Value = 0.05f, MinRarity = 2 },
        new ItemAffix { AffixId = "ironclad", DisplayName = "Ironclad", Type = AffixType.Prefix, StatKey = "bonus_dr", Value = 0.10f, MinRarity = 4 },
    };

    private static readonly ItemAffix[] SuffixPool = new[]
    {
        // Mana affixes
        new ItemAffix { AffixId = "of_wisdom", DisplayName = "of Wisdom", Type = AffixType.Suffix, StatKey = "bonus_mana", Value = 15, MinRarity = 1 },
        new ItemAffix { AffixId = "of_brilliance", DisplayName = "of Brilliance", Type = AffixType.Suffix, StatKey = "bonus_mana", Value = 30, MinRarity = 3 },
        new ItemAffix { AffixId = "of_arcana", DisplayName = "of Arcana", Type = AffixType.Suffix, StatKey = "bonus_mana", Value = 50, MinRarity = 5 },
        // Healing affixes
        new ItemAffix { AffixId = "of_mending", DisplayName = "of Mending", Type = AffixType.Suffix, StatKey = "bonus_healing_pct", Value = 0.08f, MinRarity = 2 },
        new ItemAffix { AffixId = "of_restoration", DisplayName = "of Restoration", Type = AffixType.Suffix, StatKey = "bonus_healing_pct", Value = 0.15f, MinRarity = 4 },
        // Cooldown affixes
        new ItemAffix { AffixId = "of_haste", DisplayName = "of Haste", Type = AffixType.Suffix, StatKey = "bonus_cdr", Value = 0.05f, MinRarity = 2 },
        new ItemAffix { AffixId = "of_alacrity", DisplayName = "of Alacrity", Type = AffixType.Suffix, StatKey = "bonus_cdr", Value = 0.10f, MinRarity = 4 },
        // Crit affixes
        new ItemAffix { AffixId = "of_precision", DisplayName = "of Precision", Type = AffixType.Suffix, StatKey = "bonus_crit", Value = 0.05f, MinRarity = 2 },
        new ItemAffix { AffixId = "of_lethality", DisplayName = "of Lethality", Type = AffixType.Suffix, StatKey = "bonus_crit", Value = 0.12f, MinRarity = 4 },
        // Attack speed
        new ItemAffix { AffixId = "of_fury", DisplayName = "of Fury", Type = AffixType.Suffix, StatKey = "bonus_atk_speed", Value = 0.08f, MinRarity = 3 },
    };

    /// <summary>
    /// Roll affixes for an equipment item based on its rarity.
    /// Higher rarity = more affixes possible.
    /// Rarity 0-1: 0 affixes, 2: 0-1 prefix, 3: 1 prefix, 4: 1 prefix + 0-1 suffix,
    /// 5+: 1 prefix + 1 suffix, 6+: up to 2 of each
    /// </summary>
    public static List<ItemAffix> RollAffixes(Random rng, int rarity)
    {
        var result = new List<ItemAffix>();
        if (rarity < 2) return result;

        int maxPrefixes = rarity switch
        {
            2 => rng.NextDouble() < 0.5 ? 1 : 0,
            3 => 1,
            4 => 1,
            5 => 1,
            _ => rng.NextDouble() < 0.3 ? 2 : 1
        };

        int maxSuffixes = rarity switch
        {
            2 or 3 => 0,
            4 => rng.NextDouble() < 0.5 ? 1 : 0,
            5 => 1,
            _ => rng.NextDouble() < 0.3 ? 2 : 1
        };

        // Roll prefixes
        var eligiblePrefixes = PrefixPool.Where(a => a.MinRarity <= rarity).ToArray();
        var usedStats = new HashSet<string>();
        for (int i = 0; i < maxPrefixes && eligiblePrefixes.Length > 0; i++)
        {
            var available = eligiblePrefixes.Where(a => !usedStats.Contains(a.StatKey)).ToArray();
            if (available.Length == 0) break;
            var affix = available[rng.Next(available.Length)];
            result.Add(affix);
            usedStats.Add(affix.StatKey);
        }

        // Roll suffixes
        var eligibleSuffixes = SuffixPool.Where(a => a.MinRarity <= rarity).ToArray();
        for (int i = 0; i < maxSuffixes && eligibleSuffixes.Length > 0; i++)
        {
            var available = eligibleSuffixes.Where(a => !usedStats.Contains(a.StatKey)).ToArray();
            if (available.Length == 0) break;
            var affix = available[rng.Next(available.Length)];
            result.Add(affix);
            usedStats.Add(affix.StatKey);
        }

        return result;
    }

    /// <summary>
    /// Build display name with affix names: "Prefix BaseName Suffix"
    /// </summary>
    public static string BuildAffixName(string baseName, List<ItemAffix> affixes)
    {
        var prefix = affixes.FirstOrDefault(a => a.Type == AffixType.Prefix)?.DisplayName ?? "";
        var suffix = affixes.FirstOrDefault(a => a.Type == AffixType.Suffix)?.DisplayName ?? "";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(prefix)) parts.Add(prefix);
        parts.Add(baseName);
        if (!string.IsNullOrEmpty(suffix)) parts.Add(suffix);
        return string.Join(" ", parts);
    }
}
