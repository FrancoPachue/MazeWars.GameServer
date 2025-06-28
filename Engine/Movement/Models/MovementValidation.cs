namespace MazeWars.GameServer.Engine.Movement.Models;

public class MovementValidation
{
    public bool IsValid { get; set; }
    public string? ValidationError { get; set; }
    public float SuspicionLevel { get; set; } // 0.0 to 1.0, higher = more suspicious
    public List<string> ValidationFlags { get; set; } = new();
}
