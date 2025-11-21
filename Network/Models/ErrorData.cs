using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Error message sent from server to client.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ErrorData
{
    [Key(0)]
    public string Message { get; set; } = string.Empty;

    [Key(1)]
    public DateTime Timestamp { get; set; }
}
