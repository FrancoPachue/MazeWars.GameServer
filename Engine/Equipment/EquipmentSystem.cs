using MazeWars.GameServer.Engine.Equipment.Data;
using MazeWars.GameServer.Engine.Equipment.Interface;
using MazeWars.GameServer.Engine.Equipment.Models;
using MazeWars.GameServer.Engine.Loot.Models;
using MazeWars.GameServer.Models;
using Microsoft.Extensions.Logging;

namespace MazeWars.GameServer.Engine.Equipment;

public class EquipmentSystem : IEquipmentSystem
{
    private readonly ILogger<EquipmentSystem> _logger;

    public EquipmentSystem(ILogger<EquipmentSystem> logger)
    {
        _logger = logger;
    }

    public EquipResult EquipItem(RealTimePlayer player, string itemId)
    {
        lock (player.InventoryLock)
        {
            // Find item by LootId first (unique), fallback to equipment_id for backwards compat
            var inventoryItem = player.Inventory.FirstOrDefault(i => i.LootId == itemId)
                ?? player.Inventory.FirstOrDefault(i =>
                    i.Properties.TryGetValue("equipment_id", out var eid) &&
                    eid?.ToString() == itemId);
            if (inventoryItem == null)
                return EquipResult.Fail("Item not in inventory");

            var equipId = GetEquipmentId(inventoryItem);
            if (equipId == null)
                return EquipResult.Fail("Not an equipment item");

            var equipDef = EquipmentRegistry.Get(equipId);
            if (equipDef == null)
                return EquipResult.Fail("Unknown equipment item");

            if (player.Level < equipDef.RequiredLevel)
                return EquipResult.Fail($"Requires level {equipDef.RequiredLevel}");

            var slot = equipDef.Slot;

            // Albion-style 2H weapon rules:
            // Equipping a 2H weapon → auto-unequip offhand
            // Equipping an offhand → auto-unequip 2H weapon
            if (slot == EquipmentSlot.Weapon && equipDef.IsTwoHanded)
            {
                if (player.Equipment.TryGetValue(EquipmentSlot.Offhand, out var offhand))
                {
                    player.Inventory.Add(offhand);
                    player.Equipment.TryRemove(EquipmentSlot.Offhand, out _);
                }
            }
            else if (slot == EquipmentSlot.Offhand)
            {
                if (player.Equipment.TryGetValue(EquipmentSlot.Weapon, out var weapon))
                {
                    var weaponDef = ResolveEquipmentDef(weapon);
                    if (weaponDef?.IsTwoHanded == true)
                    {
                        player.Inventory.Add(weapon);
                        player.Equipment.TryRemove(EquipmentSlot.Weapon, out _);
                    }
                }
            }

            // If slot already occupied, unequip first (swap to inventory)
            if (player.Equipment.TryGetValue(slot, out var currentItem))
            {
                player.Inventory.Add(currentItem);
                player.Equipment.TryRemove(slot, out _);
            }

            // Move from inventory to equipment
            player.Inventory.Remove(inventoryItem);
            player.Equipment[slot] = inventoryItem;

            RecalculateEquipmentStats(player);
            player.ForceNextUpdate();

            _logger.LogDebug("Player {PlayerId} equipped {ItemId} in {Slot}",
                player.PlayerId, itemId, slot);

            return EquipResult.Ok();
        }
    }

    public EquipResult UnequipItem(RealTimePlayer player, EquipmentSlot slot)
    {
        lock (player.InventoryLock)
        {
            if (!player.Equipment.TryGetValue(slot, out var item))
                return EquipResult.Fail("Nothing equipped in that slot");

            player.Equipment.TryRemove(slot, out _);
            player.Inventory.Add(item);

            RecalculateEquipmentStats(player);
            player.ForceNextUpdate();

            _logger.LogDebug("Player {PlayerId} unequipped {Slot}", player.PlayerId, slot);

            return EquipResult.Ok();
        }
    }

    public List<AbilityDefinition> GetAvailableAbilities(RealTimePlayer player)
    {
        var abilities = new List<AbilityDefinition>();

        foreach (var (slot, lootItem) in player.Equipment)
        {
            var equipDef = ResolveEquipmentDef(lootItem);
            if (equipDef == null) continue;

            foreach (var abilityId in equipDef.GrantedAbilities)
            {
                var ability = AbilityRegistry.Get(abilityId);
                if (ability != null)
                    abilities.Add(ability);
            }
        }

        return abilities;
    }

    public AbilityDefinition? ResolveAbility(RealTimePlayer player, string abilityId)
    {
        // Check if the player actually has this ability through their equipment
        foreach (var (slot, lootItem) in player.Equipment)
        {
            var equipDef = ResolveEquipmentDef(lootItem);
            if (equipDef == null) continue;

            if (equipDef.GrantedAbilities.Contains(abilityId))
                return AbilityRegistry.Get(abilityId);
        }

        return null;
    }

    public (AbilityDefinition ability, ClassAbilityModifier? modifier)? ResolveAbilityWithModifiers(
        RealTimePlayer player, string abilityId)
    {
        var ability = ResolveAbility(player, abilityId);
        if (ability == null) return null;

        // Get specific modifier for this class + ability
        var specificMod = ClassModifierRegistry.Get(player.PlayerClass, abilityId);
        var globalMod = ClassModifierRegistry.GetGlobal(player.PlayerClass);

        // Merge global and specific modifiers
        var finalMod = MergeModifiers(globalMod, specificMod);

        return (ability, finalMod);
    }

    public void RecalculateEquipmentStats(RealTimePlayer player)
    {
        int bonusHealth = 0, bonusMana = 0;
        float dmgReduction = 0f, speedBonus = 0f, manaRegen = 0f;
        float bonusDmgPct = 0f, bonusHealPct = 0f, cdr = 0f;
        float atkSpeedBonus = 0f, critChance = 0f, hpRegen = 0f;

        foreach (var (slot, lootItem) in player.Equipment)
        {
            var equipDef = ResolveEquipmentDef(lootItem);
            if (equipDef == null) continue;

            bonusHealth += equipDef.BonusHealth;
            bonusMana += equipDef.BonusMana;
            dmgReduction += equipDef.DamageReduction;
            speedBonus += equipDef.MovementSpeedBonus;
            manaRegen += equipDef.ManaRegenPerSecond;
            bonusDmgPct += equipDef.BonusDamagePercent;
            bonusHealPct += equipDef.BonusHealingPercent;
            cdr += equipDef.CooldownReduction;
            atkSpeedBonus += equipDef.AttackSpeedBonus;
            critChance += equipDef.CritChance;
            hpRegen += equipDef.HealthRegenPerSecond;

            // Apply affix stats from item (stored as percentage * 100 in Stats dict)
            foreach (var (statKey, statVal) in lootItem.Stats)
            {
                var pct = statVal / 100f;
                switch (statKey)
                {
                    case "bonus_damage_pct": bonusDmgPct += pct; break;
                    case "bonus_healing_pct": bonusHealPct += pct; break;
                    case "bonus_health": bonusHealth += statVal; break;
                    case "bonus_mana": bonusMana += statVal; break;
                    case "bonus_speed_pct": speedBonus += pct; break;
                    case "bonus_dr": dmgReduction += pct; break;
                    case "bonus_cdr": cdr += pct; break;
                    case "bonus_crit": critChance += pct; break;
                    case "bonus_atk_speed": atkSpeedBonus += pct; break;
                }
            }
        }

        // Adjust max HP/MP (remove old bonus, add new)
        var prevBonusHealth = player.EquipmentBonusHealth;
        var prevBonusMana = player.EquipmentBonusMana;
        player.MaxHealth = player.MaxHealth - prevBonusHealth + bonusHealth;
        player.MaxMana = player.MaxMana - prevBonusMana + bonusMana;
        player.Health = Math.Min(player.Health, player.MaxHealth);
        player.Mana = Math.Min(player.Mana, player.MaxMana);

        // Store equipment bonuses
        player.EquipmentBonusHealth = bonusHealth;
        player.EquipmentBonusMana = bonusMana;
        player.EquipmentDamageReduction = dmgReduction;
        player.EquipmentSpeedBonus = speedBonus;
        player.EquipmentManaRegen = manaRegen;

        // Store direct bonus fields (with caps to prevent exploits)
        player.BonusDamagePercent = Math.Clamp(bonusDmgPct, 0f, 1.0f);    // Cap: +100% bonus damage
        player.BonusHealingPercent = Math.Clamp(bonusHealPct, 0f, 0.75f);  // Cap: +75% bonus healing
        player.CooldownReduction = Math.Clamp(cdr, 0f, 0.50f);         // Cap: 50% CDR max
        player.AttackSpeedBonus = Math.Clamp(atkSpeedBonus, 0f, 1.0f);  // Cap: +100% attack speed
        player.CritChance = Math.Clamp(critChance, 0f, 0.75f);         // Cap: 75% crit
        player.HealthRegenPerSecond = Math.Min(hpRegen, 10f);

        player.MovementSpeedModifier = 1f + speedBonus;
        player.DamageReduction = Math.Clamp(dmgReduction, 0f, 0.75f);   // Cap: 75% DR max

        // Apply class HP modifier: Assassin gets -15% HP (glass cannon penalty)
        if (player.PlayerClass == "assassin")
        {
            player.MaxHealth = (int)(player.MaxHealth * 0.85f);
            player.Health = Math.Min(player.Health, player.MaxHealth);
        }
    }

    public void EquipStartingGear(RealTimePlayer player)
    {
        var gear = player.PlayerClass switch
        {
            "scout" => new[] { "hunting_bow", "leather_hood", "leather_vest", "leather_boots", "traveler_cloak" },
            "tank" => new[] { "iron_sword", "plate_helmet", "plate_chest", "plate_boots", "wooden_shield", "battle_banner" },
            "support" => new[] { "holy_staff", "cloth_hood", "cloth_robe", "cloth_boots", "tome_of_wisdom", "scholars_mantle" },
            "assassin" => new[] { "shadow_dagger", "leather_hood", "leather_vest", "leather_boots", "traveler_cloak" },
            "warlock" => new[] { "fire_staff", "cloth_hood", "cloth_robe", "cloth_boots", "scholars_mantle" },
            _ => new[] { "iron_sword", "leather_vest", "leather_boots" }
        };

        foreach (var itemId in gear)
        {
            var equipDef = EquipmentRegistry.Get(itemId);
            if (equipDef == null) continue;

            // Create a LootItem for the equipment piece (Trash rarity = base stats)
            var lootItem = new LootItem
            {
                LootId = $"starting_{itemId}_{Guid.NewGuid():N}",
                ItemName = equipDef.DisplayName,
                ItemType = equipDef.Slot is EquipmentSlot.Weapon ? "weapon" : "armor",
                Rarity = 0,
                Properties = new Dictionary<string, object>
                {
                    ["equipment_id"] = itemId,
                    ["rarity"] = 0,
                    ["quality"] = 0 // Normal quality
                }
            };

            // Directly equip (not via inventory)
            player.Equipment[equipDef.Slot] = lootItem;
        }

        RecalculateEquipmentStats(player);

        // Start at full health/mana with equipment bonuses applied
        player.Health = player.MaxHealth;
        player.Mana = player.MaxMana;

        _logger.LogDebug("Equipped starting gear for {PlayerClass} player {PlayerId} (HP: {HP}/{MaxHP})",
            player.PlayerClass, player.PlayerId, player.Health, player.MaxHealth);
    }

    public Dictionary<EquipmentSlot, string> GetEquippedItemIds(RealTimePlayer player)
    {
        var result = new Dictionary<EquipmentSlot, string>();
        foreach (var (slot, lootItem) in player.Equipment)
        {
            var eqId = GetEquipmentId(lootItem);
            if (eqId != null)
                result[slot] = eqId;
        }
        return result;
    }

    private static ClassAbilityModifier? MergeModifiers(ClassAbilityModifier? global, ClassAbilityModifier? specific)
    {
        if (global == null && specific == null) return null;
        if (global == null) return specific;
        if (specific == null) return global;

        // Specific overrides take priority; multiply with global multipliers
        return new ClassAbilityModifier
        {
            ClassId = specific.ClassId,
            AbilityId = specific.AbilityId,
            DamageMultiplier = global.DamageMultiplier * specific.DamageMultiplier,
            HealingMultiplier = global.HealingMultiplier * specific.HealingMultiplier,
            CooldownMultiplier = global.CooldownMultiplier * specific.CooldownMultiplier,
            ManaCostMultiplier = global.ManaCostMultiplier * specific.ManaCostMultiplier,
            RangeMultiplier = global.RangeMultiplier * specific.RangeMultiplier,
            DurationMultiplier = global.DurationMultiplier * specific.DurationMultiplier,
            CastTimeMultiplier = global.CastTimeMultiplier * specific.CastTimeMultiplier,
            // Extra effect comes from specific modifier only
            ExtraEffect = specific.ExtraEffect,
            ExtraEffectChance = specific.ExtraEffectChance,
            ExtraEffectValue = specific.ExtraEffectValue,
            ExtraEffectDurationMs = specific.ExtraEffectDurationMs
        };
    }

    /// <summary>
    /// Resolves the EquipmentDefinition from a LootItem by checking Properties["equipment_id"], rarity, and quality tier.
    /// </summary>
    public static EquipmentDefinition? ResolveEquipmentDef(LootItem lootItem)
    {
        var eqId = GetEquipmentId(lootItem);
        if (eqId == null) return null;

        var rarity = GetItemRarity(lootItem);
        var def = rarity > 0
            ? EquipmentRegistry.GetWithRarity(eqId, rarity)
            : EquipmentRegistry.Get(eqId);

        if (def == null) return null;

        // Apply discrete quality tier modifier if present
        var qualityTier = GetItemQualityTier(lootItem);
        if (qualityTier < 0) return def; // No quality stored → return as-is (backward compatible)

        var qualityMod = ItemRaritySystem.GetQualityMultiplier(qualityTier);
        if (Math.Abs(qualityMod - 1.0f) < 0.001f) return def;

        return ApplyQualityModifier(def, qualityMod);
    }

    /// <summary>
    /// Gets the equipment registry ID from a LootItem's properties.
    /// </summary>
    public static string? GetEquipmentId(LootItem lootItem)
    {
        if (lootItem.Properties.TryGetValue("equipment_id", out var eid))
            return eid?.ToString();
        return null;
    }

    /// <summary>
    /// Gets the rarity from a LootItem's properties. Defaults to 0 (Trash).
    /// </summary>
    public static int GetItemRarity(LootItem lootItem)
    {
        if (lootItem.Properties.TryGetValue("rarity", out var rarityObj))
        {
            if (rarityObj is int r) return r;
            if (rarityObj is long l) return (int)l;
            if (int.TryParse(rarityObj?.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    /// <summary>
    /// Gets the quality tier (0-4) from a LootItem's properties. Returns -1 if not present.
    /// Backward compatible: old float values (0.0-1.0) are mapped to the nearest discrete tier.
    /// </summary>
    public static int GetItemQualityTier(LootItem lootItem)
    {
        if (!lootItem.Properties.TryGetValue("quality", out var qObj))
            return -1;

        // New format: integer 0-4
        if (qObj is int i) return Math.Clamp(i, 0, 4);
        if (qObj is long l) return Math.Clamp((int)l, 0, 4);

        var str = qObj?.ToString();
        if (string.IsNullOrEmpty(str)) return -1;

        // Try int first (new format)
        if (int.TryParse(str, out var parsed) && parsed >= 0 && parsed <= 4)
            return parsed;

        // Fallback: old float format (0.0-1.0) → map to nearest tier
        if (float.TryParse(str, out var floatVal))
        {
            floatVal = Math.Clamp(floatVal, 0f, 1f);
            if (floatVal < 0.2f) return 0;       // Normal
            if (floatVal < 0.45f) return 1;       // Good
            if (floatVal < 0.7f) return 2;        // Outstanding
            if (floatVal < 0.9f) return 3;        // Excellent
            return 4;                              // Masterpiece
        }

        return -1;
    }

    private static EquipmentDefinition ApplyQualityModifier(EquipmentDefinition def, float qualityMod)
    {
        return new EquipmentDefinition
        {
            ItemId = def.ItemId,
            DisplayName = def.DisplayName,
            Slot = def.Slot,
            Tier = def.Tier,
            RequiredLevel = def.RequiredLevel,
            IsTwoHanded = def.IsTwoHanded,
            BonusHealth = (int)(def.BonusHealth * qualityMod),
            BonusMana = (int)(def.BonusMana * qualityMod),
            BonusDamagePercent = def.BonusDamagePercent * qualityMod,
            BonusHealingPercent = def.BonusHealingPercent * qualityMod,
            DamageReduction = def.DamageReduction * qualityMod,
            MovementSpeedBonus = def.MovementSpeedBonus * qualityMod,
            CooldownReduction = def.CooldownReduction * qualityMod,
            AttackSpeedBonus = def.AttackSpeedBonus * qualityMod,
            CritChance = def.CritChance * qualityMod,
            HealthRegenPerSecond = def.HealthRegenPerSecond * qualityMod,
            ManaRegenPerSecond = def.ManaRegenPerSecond * qualityMod,
            GrantedAbilities = def.GrantedAbilities
        };
    }
}
