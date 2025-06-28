using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Movement.Models;

public class RoomBounds
{
    public string RoomId { get; set; } = string.Empty;
    public Vector2 MinBounds { get; set; }
    public Vector2 MaxBounds { get; set; }
    public Vector2 Center { get; set; }
    public Vector2 Size { get; set; }
    public List<string> ConnectedRooms { get; set; } = new();
}
