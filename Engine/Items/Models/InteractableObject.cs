using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Items.Models;

public class InteractableObject
{
    public string ObjectId { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public Vector2 Position { get; set; }
    public bool IsLocked { get; set; }
    public string RequiredKeyType { get; set; } = "";
    public string? UnlockedBy { get; set; }
    public DateTime? UnlockedAt { get; set; }
}
