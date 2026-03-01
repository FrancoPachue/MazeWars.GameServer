using MazeWars.GameServer.Engine.Equipment.Models;
using MazeWars.GameServer.Engine.Loot.Models;

namespace MazeWars.GameServer.Engine.Equipment.Data;

public static class EquipmentRegistry
{
    private static readonly Dictionary<string, EquipmentDefinition> _equipment = new();

    static EquipmentRegistry() => RegisterAll();

    public static EquipmentDefinition? Get(string itemId) =>
        _equipment.TryGetValue(itemId, out var def) ? def : null;

    /// <summary>
    /// Get a rarity-scaled version of an equipment definition.
    /// Rarity 0 (Trash) = base stats. Higher rarities multiply all numeric stats.
    /// </summary>
    public static EquipmentDefinition? GetWithRarity(string baseItemId, int rarity)
    {
        var baseDef = Get(baseItemId);
        if (baseDef == null) return null;
        if (rarity <= 0) return baseDef;

        rarity = Math.Clamp(rarity, 0, 6);
        var mult = ItemRaritySystem.GetStatMultiplier(rarity);
        var rarityName = ItemRaritySystem.GetRarityName(rarity);

        return new EquipmentDefinition
        {
            ItemId = baseDef.ItemId,
            DisplayName = $"{rarityName} {baseDef.DisplayName}",
            Slot = baseDef.Slot,
            Tier = baseDef.Tier,
            RequiredLevel = baseDef.RequiredLevel,
            IsTwoHanded = baseDef.IsTwoHanded,
            BonusHealth = (int)(baseDef.BonusHealth * mult),
            BonusMana = (int)(baseDef.BonusMana * mult),
            BonusDamagePercent = baseDef.BonusDamagePercent * mult,
            BonusHealingPercent = baseDef.BonusHealingPercent * mult,
            DamageReduction = baseDef.DamageReduction * mult,
            MovementSpeedBonus = baseDef.MovementSpeedBonus * mult,
            CooldownReduction = baseDef.CooldownReduction * mult,
            AttackSpeedBonus = baseDef.AttackSpeedBonus * mult,
            CritChance = baseDef.CritChance * mult,
            HealthRegenPerSecond = baseDef.HealthRegenPerSecond * mult,
            ManaRegenPerSecond = baseDef.ManaRegenPerSecond * mult,
            Weight = baseDef.Weight, // Weight doesn't scale with rarity
            GrantedAbilities = baseDef.GrantedAbilities // Same abilities regardless of rarity
        };
    }

    /// <summary>
    /// Get all base (T1) equipment IDs.
    /// </summary>
    public static List<string> GetAllBaseIds() => _equipment.Keys.ToList();

    /// <summary>
    /// Get base equipment IDs filtered by slot.
    /// </summary>
    public static List<string> GetBaseIdsBySlot(EquipmentSlot slot) =>
        _equipment.Values.Where(e => e.Slot == slot).Select(e => e.ItemId).ToList();

    public static IReadOnlyDictionary<string, EquipmentDefinition> GetAll() => _equipment;

    public static List<EquipmentDefinition> GetBySlot(EquipmentSlot slot) =>
        _equipment.Values.Where(e => e.Slot == slot).ToList();

    private static void Register(EquipmentDefinition equip) =>
        _equipment[equip.ItemId] = equip;

    private static void RegisterAll()
    {
        // ── Weapons ─────────────────────────────────────────────
        Register(new EquipmentDefinition
        {
            ItemId = "iron_sword", DisplayName = "Iron Sword",
            Slot = EquipmentSlot.Weapon, Tier = 1, Weight = 3.0f,
            BonusHealth = 10,
            GrantedAbilities = ["sword_slash", "sword_heavy_strike", "sword_whirlwind"]
        });
        Register(new EquipmentDefinition
        {
            ItemId = "fire_staff", DisplayName = "Fire Staff",
            Slot = EquipmentSlot.Weapon, Tier = 1, IsTwoHanded = true, Weight = 2.5f,
            BonusHealth = 5, BonusMana = 20, BonusDamagePercent = 0.03f,
            GrantedAbilities = ["fire_staff_bolt", "fire_staff_fireball", "fire_staff_meteor"]
        });
        Register(new EquipmentDefinition
        {
            ItemId = "hunting_bow", DisplayName = "Hunting Bow",
            Slot = EquipmentSlot.Weapon, Tier = 1, IsTwoHanded = true, Weight = 2.0f,
            BonusHealth = 5, BonusDamagePercent = 0.03f,
            GrantedAbilities = ["bow_shot", "bow_multishot", "bow_rain"]
        });
        Register(new EquipmentDefinition
        {
            ItemId = "holy_staff", DisplayName = "Holy Staff",
            Slot = EquipmentSlot.Weapon, Tier = 1, IsTwoHanded = true, Weight = 2.5f,
            BonusHealth = 5, BonusMana = 30, BonusHealingPercent = 0.05f, ManaRegenPerSecond = 1f,
            GrantedAbilities = ["holy_staff_smite", "holy_staff_heal", "holy_staff_aoe_heal"]
        });

        // Dagger: fast melee, poison, high crit
        Register(new EquipmentDefinition
        {
            ItemId = "shadow_dagger", DisplayName = "Shadow Dagger",
            Slot = EquipmentSlot.Weapon, Tier = 1, Weight = 2.0f,
            BonusHealth = 5, CritChance = 0.05f, AttackSpeedBonus = 0.05f,
            GrantedAbilities = ["dagger_stab", "dagger_poison_strike", "dagger_fan_of_knives"]
        });
        // Ice Staff: ranged CC, slow, freeze
        Register(new EquipmentDefinition
        {
            ItemId = "ice_staff", DisplayName = "Ice Staff",
            Slot = EquipmentSlot.Weapon, Tier = 1, IsTwoHanded = true, Weight = 2.5f,
            BonusHealth = 5, BonusMana = 25, BonusDamagePercent = 0.02f,
            GrantedAbilities = ["ice_staff_frostbolt", "ice_staff_blizzard", "ice_staff_frozen_orb"]
        });

        // ── Head ────────────────────────────────────────────────

        // Plate Head: tanky + HP regen
        Register(new EquipmentDefinition
        {
            ItemId = "plate_helmet", DisplayName = "Plate Helmet",
            Slot = EquipmentSlot.Head, Tier = 1, Weight = 1.5f,
            BonusHealth = 30, BonusMana = 5, DamageReduction = 0.04f,
            HealthRegenPerSecond = 0.5f,
            GrantedAbilities = ["plate_helm_stun"]
        });
        // Leather Head: DPS + crit + attack speed
        Register(new EquipmentDefinition
        {
            ItemId = "leather_hood", DisplayName = "Leather Hood",
            Slot = EquipmentSlot.Head, Tier = 1, Weight = 0.8f,
            BonusHealth = 15, BonusMana = 10,
            BonusDamagePercent = 0.03f, CritChance = 0.03f, AttackSpeedBonus = 0.03f,
            GrantedAbilities = ["leather_hood_stealth"]
        });
        // Cloth Head: caster + CDR + mana regen
        Register(new EquipmentDefinition
        {
            ItemId = "cloth_hood", DisplayName = "Cloth Hood",
            Slot = EquipmentSlot.Head, Tier = 1, Weight = 0.5f,
            BonusHealth = 10, BonusMana = 25,
            BonusDamagePercent = 0.04f, BonusHealingPercent = 0.04f,
            CooldownReduction = 0.05f, ManaRegenPerSecond = 1f,
            GrantedAbilities = ["cloth_hood_purge"]
        });

        // ── Chest ───────────────────────────────────────────────

        // Plate Chest: maximum tankiness
        Register(new EquipmentDefinition
        {
            ItemId = "plate_chest", DisplayName = "Plate Chestplate",
            Slot = EquipmentSlot.Chest, Tier = 1, Weight = 3.0f,
            BonusHealth = 60, BonusMana = 10, DamageReduction = 0.10f,
            HealthRegenPerSecond = 1.0f,
            GrantedAbilities = ["plate_chest_shield_wall"]
        });
        // Leather Chest: balanced DPS
        Register(new EquipmentDefinition
        {
            ItemId = "leather_vest", DisplayName = "Leather Vest",
            Slot = EquipmentSlot.Chest, Tier = 1, Weight = 2.0f,
            BonusHealth = 25, BonusMana = 15, DamageReduction = 0.03f,
            BonusDamagePercent = 0.05f, CritChance = 0.05f, AttackSpeedBonus = 0.05f,
            MovementSpeedBonus = 0.03f,
            GrantedAbilities = ["leather_vest_dodge"]
        });
        // Cloth Chest: caster power
        Register(new EquipmentDefinition
        {
            ItemId = "cloth_robe", DisplayName = "Cloth Robe",
            Slot = EquipmentSlot.Chest, Tier = 1, Weight = 1.5f,
            BonusHealth = 15, BonusMana = 40,
            BonusDamagePercent = 0.05f, BonusHealingPercent = 0.08f,
            CooldownReduction = 0.05f, ManaRegenPerSecond = 1.5f,
            GrantedAbilities = ["cloth_robe_mana_shield"]
        });

        // ── Boots ───────────────────────────────────────────────

        // Plate Boots: sturdy
        Register(new EquipmentDefinition
        {
            ItemId = "plate_boots", DisplayName = "Plate Boots",
            Slot = EquipmentSlot.Boots, Tier = 1, Weight = 1.5f,
            BonusHealth = 25, BonusMana = 5, DamageReduction = 0.03f,
            HealthRegenPerSecond = 0.5f,
            GrantedAbilities = ["plate_boots_stomp"]
        });
        // Leather Boots: speed + crit
        Register(new EquipmentDefinition
        {
            ItemId = "leather_boots", DisplayName = "Leather Boots",
            Slot = EquipmentSlot.Boots, Tier = 1, Weight = 1.0f,
            BonusHealth = 10, BonusMana = 10,
            CritChance = 0.02f, MovementSpeedBonus = 0.06f,
            GrantedAbilities = ["leather_boots_sprint"]
        });
        // Cloth Boots: mobility + CDR
        Register(new EquipmentDefinition
        {
            ItemId = "cloth_boots", DisplayName = "Cloth Sandals",
            Slot = EquipmentSlot.Boots, Tier = 1, Weight = 0.5f,
            BonusHealth = 5, BonusMana = 15,
            CooldownReduction = 0.03f, MovementSpeedBonus = 0.04f,
            GrantedAbilities = ["cloth_boots_blink"]
        });

        // ── Offhand (passive only) ──────────────────────────────

        // Shield: pure defense
        Register(new EquipmentDefinition
        {
            ItemId = "wooden_shield", DisplayName = "Wooden Shield",
            Slot = EquipmentSlot.Offhand, Tier = 1, Weight = 2.0f,
            BonusHealth = 20, DamageReduction = 0.06f
        });
        // Tome: caster offhand
        Register(new EquipmentDefinition
        {
            ItemId = "tome_of_wisdom", DisplayName = "Tome of Wisdom",
            Slot = EquipmentSlot.Offhand, Tier = 1, Weight = 1.0f,
            BonusHealth = 5, BonusMana = 25,
            BonusHealingPercent = 0.05f, ManaRegenPerSecond = 2f
        });
        // Torch: offensive offhand
        Register(new EquipmentDefinition
        {
            ItemId = "torch", DisplayName = "Torch",
            Slot = EquipmentSlot.Offhand, Tier = 1, Weight = 0.8f,
            BonusHealth = 5,
            BonusDamagePercent = 0.05f, AttackSpeedBonus = 0.05f
        });

        // ── Cape (passive only) ─────────────────────────────────

        // Battle Banner: frontline
        Register(new EquipmentDefinition
        {
            ItemId = "battle_banner", DisplayName = "Battle Banner",
            Slot = EquipmentSlot.Cape, Tier = 1, Weight = 0.8f,
            BonusHealth = 15, BonusDamagePercent = 0.03f
        });
        // Traveler's Cloak: mobility
        Register(new EquipmentDefinition
        {
            ItemId = "traveler_cloak", DisplayName = "Traveler's Cloak",
            Slot = EquipmentSlot.Cape, Tier = 1, Weight = 0.5f,
            BonusHealth = 10, MovementSpeedBonus = 0.05f
        });
        // Scholar's Mantle: caster
        Register(new EquipmentDefinition
        {
            ItemId = "scholars_mantle", DisplayName = "Scholar's Mantle",
            Slot = EquipmentSlot.Cape, Tier = 1, Weight = 0.5f,
            BonusHealth = 5, BonusMana = 10,
            BonusHealingPercent = 0.05f, ManaRegenPerSecond = 1f
        });
    }
}
