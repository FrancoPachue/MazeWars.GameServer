namespace MazeWars.GameServer.Engine.MobIASystem.Models;

// =============================================
// AI CONFIGURATION
// =============================================

public class AISettings
{
    public float GlobalAggressionMultiplier { get; set; } = 1.0f;
    public float UpdateFrequency { get; set; } = 30.0f; // Updates per second
    public int MaxMobsPerRoom { get; set; } = 8;
    public float DifficultyScaling { get; set; } = 1.0f;
    public bool EnableGroupBehavior { get; set; } = true;
    public bool EnableDynamicSpawning { get; set; } = true;
    public float DynamicSpawnInterval { get; set; } = 60.0f; // seconds
    public int MaxDynamicMobs { get; set; } = 20;
    public bool EnablePerformanceOptimization { get; set; } = true;
    public float OptimizationDistance { get; set; } = 50.0f;
    public bool EnableBossAI { get; set; } = true;
    public float BossSpawnChance { get; set; } = 0.1f;
}
