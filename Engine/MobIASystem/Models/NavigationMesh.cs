using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public class NavigationMesh
{
    public Dictionary<string, List<PathfindingNode>> RoomNodes { get; set; } = new();
    public Dictionary<string, List<Vector2>> RoomConnections { get; set; } = new();
    public float NodeSpacing { get; set; } = 5.0f;
}
