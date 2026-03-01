namespace MazeWars.GameServer.Models;

public class RoomDoor
{
    /// <summary>DoorId matches ConnectionId (sorted room IDs joined with "_")</summary>
    public string DoorId { get; set; } = string.Empty;
    public string RoomIdA { get; set; } = string.Empty;
    public string RoomIdB { get; set; } = string.Empty;
    public bool IsOpen { get; set; } = false;
    public DateTime OpenedAt { get; set; }
    /// <summary>Server cleanup timer: silently close abandoned doors (not gameplay auto-close)</summary>
    public float AutoCloseSeconds { get; set; } = 90f;
    /// <summary>If true, requires a key (handled by LockedDoor system)</summary>
    public bool IsLocked { get; set; } = false;
    /// <summary>Player who is currently channeling to open this door</summary>
    public string? ChannelingPlayerId { get; set; }
    public DateTime ChannelingStartTime { get; set; }
    public float ChannelingDuration { get; set; } = 1.0f; // 1.0s channel to open
    public float ClosingDuration { get; set; } = 0.5f; // 0.5s channel to close (defensive = faster)
}
