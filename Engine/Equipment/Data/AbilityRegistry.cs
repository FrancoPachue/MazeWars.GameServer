using MazeWars.GameServer.Engine.Equipment.Models;

namespace MazeWars.GameServer.Engine.Equipment.Data;

public static class AbilityRegistry
{
    private static readonly Dictionary<string, AbilityDefinition> _abilities = new();

    static AbilityRegistry() => RegisterAll();

    public static AbilityDefinition? Get(string abilityId) =>
        _abilities.TryGetValue(abilityId, out var def) ? def : null;

    public static IReadOnlyDictionary<string, AbilityDefinition> GetAll() => _abilities;

    private static void Register(AbilityDefinition ability) =>
        _abilities[ability.AbilityId] = ability;

    private static void RegisterAll()
    {
        // ── Sword (Weapon) ──────────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "sword_slash", DisplayName = "Slash",
            Type = AbilityType.MeleeDamage, SlotIndex = 0,
            BaseDamage = 20, ManaCost = 0, CooldownMs = 0, Range = 1.5f,
            AttackSpeedMs = 700
        });
        Register(new AbilityDefinition
        {
            AbilityId = "sword_heavy_strike", DisplayName = "Heavy Strike",
            Type = AbilityType.MeleeDamage, SlotIndex = 1,
            BaseDamage = 40, ManaCost = 15, CooldownMs = 3000, Range = 1.5f
        });
        Register(new AbilityDefinition
        {
            AbilityId = "sword_whirlwind", DisplayName = "Whirlwind",
            Type = AbilityType.AreaDamage, SlotIndex = 2,
            BaseDamage = 30, ManaCost = 30, CooldownMs = 8000, Range = 0, AreaRadius = 2.0f
        });

        // ── Fire Staff (Weapon) ─────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "fire_staff_bolt", DisplayName = "Fire Bolt",
            Type = AbilityType.ProjectileDamage, SlotIndex = 0,
            BaseDamage = 25, ManaCost = 10, CooldownMs = 0, Range = 8f,
            ProjectileSpeed = 15f, ProjectileRadius = 0.3f, AttackSpeedMs = 1200
        });
        Register(new AbilityDefinition
        {
            AbilityId = "fire_staff_fireball", DisplayName = "Fireball",
            Type = AbilityType.ProjectileDamage, SlotIndex = 1,
            BaseDamage = 35, ManaCost = 25, CooldownMs = 5000, Range = 6f, AreaRadius = 2f,
            ProjectileSpeed = 10f, ProjectileRadius = 0.5f
        });
        Register(new AbilityDefinition
        {
            AbilityId = "fire_staff_meteor", DisplayName = "Meteor",
            Type = AbilityType.AreaDamage, SlotIndex = 2,
            BaseDamage = 60, ManaCost = 50, CooldownMs = 15000, Range = 8f, AreaRadius = 3f,
            CastTimeMs = 1500
        });

        // ── Bow (Weapon) ────────────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "bow_shot", DisplayName = "Shot",
            Type = AbilityType.ProjectileDamage, SlotIndex = 0,
            BaseDamage = 22, ManaCost = 5, CooldownMs = 0, Range = 10f,
            ProjectileSpeed = 20f, ProjectileRadius = 0.25f, AttackSpeedMs = 900
        });
        Register(new AbilityDefinition
        {
            AbilityId = "bow_multishot", DisplayName = "Multi Shot",
            Type = AbilityType.ProjectileDamage, SlotIndex = 1,
            BaseDamage = 15, ManaCost = 20, CooldownMs = 4000, Range = 8f,
            ProjectileSpeed = 18f, ProjectileRadius = 0.25f
        });
        Register(new AbilityDefinition
        {
            AbilityId = "bow_rain", DisplayName = "Arrow Rain",
            Type = AbilityType.AreaDamage, SlotIndex = 2,
            BaseDamage = 25, ManaCost = 35, CooldownMs = 12000, Range = 10f, AreaRadius = 3f,
            CastTimeMs = 1000
        });

        // ── Holy Staff (Weapon) ─────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "holy_staff_smite", DisplayName = "Smite",
            Type = AbilityType.ProjectileDamage, SlotIndex = 0,
            BaseDamage = 18, ManaCost = 8, CooldownMs = 0, Range = 7f,
            ProjectileSpeed = 12f, ProjectileRadius = 0.35f, AttackSpeedMs = 1100
        });
        Register(new AbilityDefinition
        {
            AbilityId = "holy_staff_heal", DisplayName = "Holy Light",
            Type = AbilityType.Heal, SlotIndex = 1,
            BaseHealing = 40, ManaCost = 25, CooldownMs = 4000, Range = 8f
        });
        Register(new AbilityDefinition
        {
            AbilityId = "holy_staff_aoe_heal", DisplayName = "Divine Blessing",
            Type = AbilityType.Heal, SlotIndex = 2,
            BaseHealing = 30, ManaCost = 45, CooldownMs = 15000, Range = 0, AreaRadius = 4f,
            CastTimeMs = 1000
        });

        // ── Shadow Dagger (Weapon) ─────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "dagger_stab", DisplayName = "Stab",
            Type = AbilityType.MeleeDamage, SlotIndex = 0,
            BaseDamage = 15, ManaCost = 0, CooldownMs = 0, Range = 1.2f,
            AttackSpeedMs = 500
        });
        Register(new AbilityDefinition
        {
            AbilityId = "dagger_poison_strike", DisplayName = "Poison Strike",
            Type = AbilityType.MeleeDamage, SlotIndex = 1,
            BaseDamage = 25, ManaCost = 15, CooldownMs = 4000, Range = 1.2f,
            AppliesEffect = "poison", EffectValue = 8, EffectDurationMs = 5000
        });
        Register(new AbilityDefinition
        {
            AbilityId = "dagger_fan_of_knives", DisplayName = "Fan of Knives",
            Type = AbilityType.AreaDamage, SlotIndex = 2,
            BaseDamage = 20, ManaCost = 25, CooldownMs = 10000, Range = 0, AreaRadius = 2.5f,
            AppliesEffect = "bleed", EffectValue = 5, EffectDurationMs = 4000
        });

        // ── Ice Staff (Weapon) ────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "ice_staff_frostbolt", DisplayName = "Frost Bolt",
            Type = AbilityType.ProjectileDamage, SlotIndex = 0,
            BaseDamage = 22, ManaCost = 10, CooldownMs = 0, Range = 9f,
            ProjectileSpeed = 14f, ProjectileRadius = 0.3f, AttackSpeedMs = 1000,
            AppliesEffect = "slow", EffectValue = 20, EffectDurationMs = 2000
        });
        Register(new AbilityDefinition
        {
            AbilityId = "ice_staff_blizzard", DisplayName = "Blizzard",
            Type = AbilityType.AreaDamage, SlotIndex = 1,
            BaseDamage = 30, ManaCost = 30, CooldownMs = 8000, Range = 7f, AreaRadius = 3f,
            CastTimeMs = 800,
            AppliesEffect = "slow", EffectValue = 40, EffectDurationMs = 3000
        });
        Register(new AbilityDefinition
        {
            AbilityId = "ice_staff_frozen_orb", DisplayName = "Frozen Orb",
            Type = AbilityType.ProjectileDamage, SlotIndex = 2,
            BaseDamage = 50, ManaCost = 45, CooldownMs = 14000, Range = 8f, AreaRadius = 2.5f,
            ProjectileSpeed = 8f, ProjectileRadius = 0.6f,
            AppliesEffect = "slow", EffectValue = 50, EffectDurationMs = 4000
        });

        // ── Head Abilities ──────────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "cloth_hood_purge", DisplayName = "Purge",
            Type = AbilityType.StatusApply, SlotIndex = 0,
            ManaCost = 25, CooldownMs = 20000, Range = 0,
            AppliesEffect = "purge"
        });
        Register(new AbilityDefinition
        {
            AbilityId = "plate_helm_stun", DisplayName = "Headbutt",
            Type = AbilityType.StatusApply, SlotIndex = 0,
            BaseDamage = 10, ManaCost = 30, CooldownMs = 25000, Range = 1.5f,
            AppliesEffect = "stun", EffectDurationMs = 1500
        });
        Register(new AbilityDefinition
        {
            AbilityId = "leather_hood_stealth", DisplayName = "Ambush",
            Type = AbilityType.Stealth, SlotIndex = 0,
            ManaCost = 30, CooldownMs = 30000, DurationMs = 5000
        });

        // ── Chest Abilities ─────────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "leather_vest_dodge", DisplayName = "Dodge Roll",
            Type = AbilityType.Dash, SlotIndex = 0,
            ManaCost = 20, CooldownMs = 15000, DashDistance = 5f, DashSpeed = 15f
        });
        Register(new AbilityDefinition
        {
            AbilityId = "plate_chest_shield_wall", DisplayName = "Shield Wall",
            Type = AbilityType.Shield, SlotIndex = 0,
            BaseHealing = 50, ManaCost = 40, CooldownMs = 25000, DurationMs = 5000
        });
        Register(new AbilityDefinition
        {
            AbilityId = "cloth_robe_mana_shield", DisplayName = "Mana Shield",
            Type = AbilityType.Shield, SlotIndex = 0,
            BaseHealing = 40, ManaCost = 35, CooldownMs = 20000, DurationMs = 4000
        });

        // ── Boots Abilities ─────────────────────────────────────
        Register(new AbilityDefinition
        {
            AbilityId = "leather_boots_sprint", DisplayName = "Sprint",
            Type = AbilityType.Buff, SlotIndex = 0,
            ManaCost = 15, CooldownMs = 20000, DurationMs = 4000,
            AppliesEffect = "speed_boost", EffectValue = 50
        });
        Register(new AbilityDefinition
        {
            AbilityId = "plate_boots_stomp", DisplayName = "Ground Slam",
            Type = AbilityType.AreaDamage, SlotIndex = 0,
            BaseDamage = 20, ManaCost = 25, CooldownMs = 15000, AreaRadius = 2f,
            AppliesEffect = "slow", EffectValue = 30, EffectDurationMs = 3000
        });
        Register(new AbilityDefinition
        {
            AbilityId = "cloth_boots_blink", DisplayName = "Blink",
            Type = AbilityType.Dash, SlotIndex = 0,
            ManaCost = 20, CooldownMs = 15000, DashDistance = 8f, DashSpeed = 100f
        });
    }
}
