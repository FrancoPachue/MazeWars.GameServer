namespace MazeWars.GameServer.Engine.Equipment.Models;

public class AbilityDefinition
{
    public string AbilityId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AbilityType Type { get; set; }
    public int SlotIndex { get; set; } // 0=Q, 1=W, 2=E for weapon; 0 for armor

    // Base parameters (modified by class)
    public float BaseDamage { get; set; }
    public float BaseHealing { get; set; }
    public int ManaCost { get; set; }
    public int CooldownMs { get; set; }
    public float Range { get; set; }
    public float AreaRadius { get; set; }
    public int DurationMs { get; set; }
    public int CastTimeMs { get; set; }

    // Status effect
    public string AppliesEffect { get; set; } = string.Empty;
    public int EffectValue { get; set; }
    public int EffectDurationMs { get; set; }

    // Projectile parameters (0 = legacy instant hit)
    public float ProjectileSpeed { get; set; }
    public float ProjectileRadius { get; set; } = 0.3f;

    // Attack speed for basic attacks (slot 0). 0 = use global default
    public int AttackSpeedMs { get; set; }

    // Multi-hit (combo abilities deal multiple damage instances per use)
    public int HitCount { get; set; } = 1;

    // Movement (for dash/charge)
    public float DashDistance { get; set; }
    public float DashSpeed { get; set; }
}
