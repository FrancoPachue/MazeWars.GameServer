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

/// <summary>
/// Walkable corridor connecting two adjacent rooms through a wall gap.
/// </summary>
public class CorridorBounds
{
    public Vector2 MinBounds { get; set; }
    public Vector2 MaxBounds { get; set; }
    public string RoomIdA { get; set; } = string.Empty;
    public string RoomIdB { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Door position calculated from room centers midpoint (matches client visual).
    /// </summary>
    public Vector2 DoorMidpoint { get; set; }

    /// <summary>
    /// Whether the corridor runs horizontally (rooms differ in X) or vertically (rooms differ in Y).
    /// </summary>
    public bool IsHorizontal { get; set; }
}
