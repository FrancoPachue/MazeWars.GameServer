using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Movement.Models;

public class CollisionInfo
{
    public CollisionType Type { get; set; }
    public string? ObjectId { get; set; }
    public Vector2 CollisionPoint { get; set; }
    public Vector2 CollisionNormal { get; set; }
    public float PenetrationDepth { get; set; }
}
