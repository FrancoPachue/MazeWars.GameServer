using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Server response to initial connection with session token for reconnection.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ConnectResponseData
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public string ErrorMessage { get; set; } = string.Empty;

    [Key(2)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(3)]
    public string WorldId { get; set; } = string.Empty;

    [Key(4)]
    public bool IsInLobby { get; set; }

    /// <summary>
    /// ⭐ NEW: Session token for reconnection.
    /// Client should save this and use it to reconnect if disconnected.
    /// </summary>
    [Key(5)]
    public string SessionToken { get; set; } = string.Empty;

    [Key(6)]
    public float ServerTime { get; set; }

    [Key(7)]
    public string PlayerClass { get; set; } = string.Empty;

    [Key(8)]
    public string TeamId { get; set; } = string.Empty;
}
