namespace MazeWars.GameServer.Models;

public class LockedDoor
{
    public string ConnectionId { get; set; } = string.Empty;
    public string RoomIdA { get; set; } = string.Empty;
    public string RoomIdB { get; set; } = string.Empty;
    public string RequiredKeyType { get; set; } = string.Empty;
    public bool IsLocked { get; set; } = true;
    public string? UnlockedByPlayerId { get; set; }
    public DateTime? UnlockedAt { get; set; }
}
