using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Configuration;

public class GameBalance
{
    [Range(1.0f, 20.0f)]
    public float MovementSpeed { get; set; } = 5.0f;

    [Range(1.1f, 3.0f)]
    public float SprintMultiplier { get; set; } = 1.5f;

    [Range(50, 500)]
    public int BaseHealth { get; set; } = 100;

    [Range(1.0f, 10.0f)]
    public float AttackRange { get; set; } = 1.5f;

    [Range(100, 5000)]
    public int AttackCooldownMs { get; set; } = 1000;

    [Range(5, 300)]
    public int ExtractionTimeSeconds { get; set; } = 20;

    [Range(5, 50)]
    public int MaxInventorySize { get; set; } = 20;

    [Range(1, 16)]
    public int MaxTeamSize { get; set; } = 6;
    public int BaseExperiencePerLevel { get; set; } = 1000;
    public float ExperienceMultiplier { get; set; } = 1.5f;

    // ── Weight / Encumbrance ──
    public float PlayerCarryCapacity { get; set; } = 50f;
    public float OverweightSpeedPenalty { get; set; } = 0.5f; // Minimum speed multiplier at 2x capacity

    // ── Corruption Zone ──
    public float CorruptionDelaySeconds { get; set; } = 300f;
    public float CorruptionWaveIntervalSeconds { get; set; } = 120f;
    public float CorruptionWarningSeconds { get; set; } = 30f;
    public int CorruptionDamageBase { get; set; } = 5;
    public float CorruptionDamageScale { get; set; } = 2f;

    // ── Revival ──
    public float RevivalChannelSeconds { get; set; } = 3.0f;
}
