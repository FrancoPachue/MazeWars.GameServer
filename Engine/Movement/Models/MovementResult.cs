using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Movement.Models;

public class MovementResult
{
    public bool Success { get; set; }
    public Vector2 NewPosition { get; set; }
    public Vector2 NewVelocity { get; set; }
    public string? ErrorMessage { get; set; }
    public bool PositionChanged { get; set; }
    public bool CollisionDetected { get; set; }
    public List<string> CollidedWith { get; set; } = new();
}
