using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public class AIBehaviorResult
{
    public bool StateChanged { get; set; }
    public MobState? NewState { get; set; }
    public Vector2? NewPosition { get; set; }
    public Vector2? NewTarget { get; set; }
    public List<MobAction> ActionsPerformed { get; set; } = new();
    public string? DebugInfo { get; set; }
}
