namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public class MobStats
{
    public int Health { get; set; } = 50;
    public int MaxHealth { get; set; } = 50;
    public int Damage { get; set; } = 20;
    public float Speed { get; set; } = 2.0f;
    public float AttackRange { get; set; } = 3.0f;
    public float DetectionRange { get; set; } = 8.0f;
    public float AttackCooldown { get; set; } = 2.0f;
    public int Armor { get; set; } = 0;
    public int MagicResistance { get; set; } = 0;
    public float CriticalChance { get; set; } = 0.05f;
    public int ExperienceReward { get; set; } = 50;
}
