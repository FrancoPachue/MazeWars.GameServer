namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public class AIBehaviorSettings
{
    public float PatrolRadius { get; set; } = 20.0f;
    public float AggressionLevel { get; set; } = 1.0f;
    public float FleeThreshold { get; set; } = 0.2f; // Health % when mob flees
    public bool CanCallForHelp { get; set; } = true;
    public float HelpCallRadius { get; set; } = 15.0f;
    public bool PrefersMelee { get; set; } = true;
    public float AbilityCooldown { get; set; } = 10.0f;
    public List<string> PreferredTargets { get; set; } = new(); // Player classes
    public bool AvoidsPvP { get; set; } = false; // Stays away from player combat
}
