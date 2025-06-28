namespace MazeWars.GameServer.Engine.MobIASystem.Models;

// =============================================
// MOB CONFIGURATION
// =============================================

public class MobTemplate
{
    public string MobType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MobStats BaseStats { get; set; } = new();
    public List<string> Abilities { get; set; } = new();
    public AIBehaviorSettings BehaviorSettings { get; set; } = new();
    public float SpawnWeight { get; set; } = 1.0f;
    public List<string> SpawnRooms { get; set; } = new(); // Empty = all rooms
    public bool IsBoss { get; set; }
    public bool IsElite { get; set; }
    public Dictionary<string, object> CustomProperties { get; set; } = new();
}
