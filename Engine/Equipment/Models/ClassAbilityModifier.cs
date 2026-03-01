namespace MazeWars.GameServer.Engine.Equipment.Models;

public class ClassAbilityModifier
{
    public string ClassId { get; set; } = string.Empty;
    public string AbilityId { get; set; } = string.Empty; // "*" for global class modifier

    // Multipliers (1.0 = no change)
    public float DamageMultiplier { get; set; } = 1f;
    public float HealingMultiplier { get; set; } = 1f;
    public float CooldownMultiplier { get; set; } = 1f;
    public float ManaCostMultiplier { get; set; } = 1f;
    public float RangeMultiplier { get; set; } = 1f;
    public float DurationMultiplier { get; set; } = 1f;
    public float CastTimeMultiplier { get; set; } = 1f;
    public float AttackSpeedMultiplier { get; set; } = 1f;

    // Extra effect added by class
    public string ExtraEffect { get; set; } = string.Empty;
    public float ExtraEffectChance { get; set; }
    public int ExtraEffectValue { get; set; }
    public int ExtraEffectDurationMs { get; set; }
}
