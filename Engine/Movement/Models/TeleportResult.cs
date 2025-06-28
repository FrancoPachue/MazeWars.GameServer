using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Movement.Models;

public class TeleportResult
{
    public bool Success { get; set; }
    public Vector2 FinalPosition { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RoomChanged { get; set; }
    public string? NewRoomId { get; set; }
}
