using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Base network message with MessagePack serialization support.
/// </summary>
[MessagePackObject]
public class NetworkMessage
{
    [Key(0)]
    public string Type { get; set; } = string.Empty;

    [Key(1)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(2)]
    public object Data { get; set; } = null!;

    [Key(3)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
