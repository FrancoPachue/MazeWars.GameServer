using MazeWars.GameServer.Engine.Equipment.Models;

namespace MazeWars.GameServer.Engine.Equipment.Data;

public static class ClassModifierRegistry
{
    // Key: "classId:abilityId" or "classId:*" for global
    private static readonly Dictionary<string, ClassAbilityModifier> _modifiers = new();

    static ClassModifierRegistry() => RegisterAll();

    public static ClassAbilityModifier? Get(string classId, string abilityId)
    {
        var key = $"{classId}:{abilityId}";
        return _modifiers.TryGetValue(key, out var mod) ? mod : null;
    }

    public static ClassAbilityModifier? GetGlobal(string classId)
    {
        var key = $"{classId}:*";
        return _modifiers.TryGetValue(key, out var mod) ? mod : null;
    }

    private static void Register(ClassAbilityModifier mod) =>
        _modifiers[$"{mod.ClassId}:{mod.AbilityId}"] = mod;

    private static void RegisterAll()
    {
        // ═══════════════════════════════════════════════════════
        // SCOUT - Fast, agile, crit/bleed oriented
        // ═══════════════════════════════════════════════════════

        // Global: -10% cooldowns, +10% range, +15% attack speed
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "*",
            CooldownMultiplier = 0.9f, RangeMultiplier = 1.1f,
            AttackSpeedMultiplier = 0.85f
        });

        // Scout + Sword
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "sword_slash",
            CooldownMultiplier = 0.7f,
            ExtraEffect = "bleed", ExtraEffectChance = 0.20f,
            ExtraEffectValue = 5, ExtraEffectDurationMs = 3000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "sword_heavy_strike",
            RangeMultiplier = 1.2f,
            ExtraEffect = "bleed", ExtraEffectChance = 0.50f,
            ExtraEffectValue = 8, ExtraEffectDurationMs = 4000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "sword_whirlwind",
            DamageMultiplier = 1.15f
        });

        // Scout + Bow
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "bow_shot",
            DamageMultiplier = 1.1f,
            ExtraEffect = "bleed", ExtraEffectChance = 0.15f,
            ExtraEffectValue = 4, ExtraEffectDurationMs = 3000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "bow_multishot",
            CooldownMultiplier = 0.8f, DamageMultiplier = 1.1f
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "bow_rain",
            DamageMultiplier = 1.2f, CastTimeMultiplier = 0.8f
        });

        // Scout + Fire Staff
        Register(new ClassAbilityModifier
        {
            ClassId = "scout", AbilityId = "fire_staff_bolt",
            CooldownMultiplier = 0.8f, RangeMultiplier = 1.15f
        });

        // ═══════════════════════════════════════════════════════
        // TANK - Slow, durable, stun/knockback oriented
        // ═══════════════════════════════════════════════════════

        // Global: +10% damage, -15% attack speed, +20% status duration
        Register(new ClassAbilityModifier
        {
            ClassId = "tank", AbilityId = "*",
            DamageMultiplier = 1.1f, CooldownMultiplier = 1.15f,
            DurationMultiplier = 1.2f, AttackSpeedMultiplier = 1.15f
        });

        // Tank + Sword
        Register(new ClassAbilityModifier
        {
            ClassId = "tank", AbilityId = "sword_slash",
            ExtraEffect = "stun", ExtraEffectChance = 0.25f,
            ExtraEffectDurationMs = 500
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "tank", AbilityId = "sword_heavy_strike",
            DamageMultiplier = 1.5f,
            ExtraEffect = "knockback", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 3
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "tank", AbilityId = "sword_whirlwind",
            DamageMultiplier = 1.1f,
            ExtraEffect = "slow", ExtraEffectChance = 0.5f,
            ExtraEffectValue = 20, ExtraEffectDurationMs = 2000
        });

        // Tank + Bow (unconventional but functional)
        Register(new ClassAbilityModifier
        {
            ClassId = "tank", AbilityId = "bow_shot",
            DamageMultiplier = 0.9f,
            ExtraEffect = "slow", ExtraEffectChance = 0.30f,
            ExtraEffectValue = 15, ExtraEffectDurationMs = 2000
        });

        // ═══════════════════════════════════════════════════════
        // SUPPORT - Healing boost, buff duration, team utility
        // ═══════════════════════════════════════════════════════

        // Global: +25% healing, +10% buff duration, -10% damage
        Register(new ClassAbilityModifier
        {
            ClassId = "support", AbilityId = "*",
            HealingMultiplier = 1.25f, DurationMultiplier = 1.1f,
            DamageMultiplier = 0.9f
        });

        // Support + Sword (life steal theme)
        Register(new ClassAbilityModifier
        {
            ClassId = "support", AbilityId = "sword_slash",
            ExtraEffect = "life_steal", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 10 // heals 10% of damage dealt
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "support", AbilityId = "sword_heavy_strike",
            ExtraEffect = "team_heal", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 20 // heals nearby allies 20% of damage
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "support", AbilityId = "sword_whirlwind",
            ExtraEffect = "speed_boost_allies", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 20, ExtraEffectDurationMs = 3000
        });

        // Support + Holy Staff
        Register(new ClassAbilityModifier
        {
            ClassId = "support", AbilityId = "holy_staff_heal",
            HealingMultiplier = 1.3f, CooldownMultiplier = 0.85f
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "support", AbilityId = "holy_staff_aoe_heal",
            HealingMultiplier = 1.4f, RangeMultiplier = 1.2f,
            ExtraEffect = "regen", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 5, ExtraEffectDurationMs = 5000
        });

        // Support + Fire Staff (support mage hybrid)
        Register(new ClassAbilityModifier
        {
            ClassId = "support", AbilityId = "fire_staff_fireball",
            ExtraEffect = "weaken", ExtraEffectChance = 0.4f,
            ExtraEffectValue = 15, ExtraEffectDurationMs = 3000
        });

        // ═══════════════════════════════════════════════════════
        // ASSASSIN - Burst damage, poison, glass cannon
        // ═══════════════════════════════════════════════════════

        // Global: +20% damage, -20% cooldowns, -15% HP (applied via equipment bonuses)
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "*",
            DamageMultiplier = 1.2f, CooldownMultiplier = 0.8f,
            AttackSpeedMultiplier = 0.85f
        });

        // Assassin + Sword
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "sword_slash",
            DamageMultiplier = 1.15f,
            ExtraEffect = "poison", ExtraEffectChance = 0.35f,
            ExtraEffectValue = 6, ExtraEffectDurationMs = 4000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "sword_heavy_strike",
            DamageMultiplier = 1.5f,
            ExtraEffect = "poison", ExtraEffectChance = 0.60f,
            ExtraEffectValue = 10, ExtraEffectDurationMs = 5000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "sword_whirlwind",
            DamageMultiplier = 1.3f
        });

        // Assassin + Shadow Dagger (best synergy)
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "dagger_stab",
            DamageMultiplier = 1.25f, CooldownMultiplier = 0.7f,
            ExtraEffect = "poison", ExtraEffectChance = 0.40f,
            ExtraEffectValue = 8, ExtraEffectDurationMs = 4000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "dagger_poison_strike",
            DamageMultiplier = 1.3f, DurationMultiplier = 1.5f
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "dagger_fan_of_knives",
            DamageMultiplier = 1.2f,
            ExtraEffect = "bleed", ExtraEffectChance = 0.50f,
            ExtraEffectValue = 6, ExtraEffectDurationMs = 3000
        });

        // Assassin + Bow (sniper variant)
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "bow_shot",
            DamageMultiplier = 1.3f, RangeMultiplier = 1.2f
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "assassin", AbilityId = "bow_multishot",
            DamageMultiplier = 1.15f
        });

        // ═══════════════════════════════════════════════════════
        // WARLOCK - Curse/debuff specialist, drain life, mana-heavy
        // ═══════════════════════════════════════════════════════

        // Global: +15% damage, +20% status duration, -10% attack speed
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "*",
            DamageMultiplier = 1.15f, DurationMultiplier = 1.2f,
            AttackSpeedMultiplier = 1.1f, ManaCostMultiplier = 0.9f
        });

        // Warlock + Fire Staff (best synergy — fire curses)
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "fire_staff_bolt",
            DamageMultiplier = 1.2f,
            ExtraEffect = "weaken", ExtraEffectChance = 0.30f,
            ExtraEffectValue = 20, ExtraEffectDurationMs = 4000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "fire_staff_fireball",
            DamageMultiplier = 1.25f,
            ExtraEffect = "life_steal", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 15 // heals 15% of damage dealt
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "fire_staff_meteor",
            DamageMultiplier = 1.3f, RangeMultiplier = 1.15f
        });

        // Warlock + Ice Staff (frost curse variant)
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "ice_staff_frostbolt",
            ExtraEffect = "weaken", ExtraEffectChance = 0.40f,
            ExtraEffectValue = 15, ExtraEffectDurationMs = 5000
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "ice_staff_blizzard",
            DamageMultiplier = 1.2f, DurationMultiplier = 1.3f,
            ExtraEffect = "life_steal", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 10
        });

        // Warlock + Sword (melee drain build)
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "sword_slash",
            ExtraEffect = "life_steal", ExtraEffectChance = 1.0f,
            ExtraEffectValue = 15
        });
        Register(new ClassAbilityModifier
        {
            ClassId = "warlock", AbilityId = "sword_heavy_strike",
            DamageMultiplier = 1.2f,
            ExtraEffect = "weaken", ExtraEffectChance = 0.50f,
            ExtraEffectValue = 20, ExtraEffectDurationMs = 3000
        });
    }
}
