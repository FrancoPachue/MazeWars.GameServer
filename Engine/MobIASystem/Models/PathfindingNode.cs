using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

// =============================================
// PATHFINDING AND NAVIGATION
// =============================================

public class PathfindingNode
{
    public Vector2 Position { get; set; }
    public float GCost { get; set; } // Distance from start
    public float HCost { get; set; } // Distance to end
    public float FCost => GCost + HCost;
    public PathfindingNode? Parent { get; set; }
    public bool IsWalkable { get; set; } = true;
}
