namespace MazeWars.GameServer.Models;

public class Mob
{
    public string MobId { get; set; } = string.Empty;
    public string MobType { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public Vector2 PatrolTarget { get; set; }
    public string State { get; set; } = "idle";
    public int Health { get; set; } = 50;
    public int MaxHealth { get; set; } = 50;
    public string RoomId { get; set; } = string.Empty;
    public DateTime LastStateChange { get; set; } = DateTime.UtcNow;
    public bool IsDirty { get; set; } = true;
}
