using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

// =============================================
// AI DECISION MAKING
// =============================================

public class AIDecision
{
    public MobAction Action { get; set; }
    public float Priority { get; set; }
    public Vector2? TargetPosition { get; set; }
    public string? TargetId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}
