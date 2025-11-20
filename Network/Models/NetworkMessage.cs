using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Base network message with MessagePack serialization support.
/// Data uses byte[] for pre-serialized MessagePack data following the spec.
/// The Type field acts as a discriminator for the payload type.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class NetworkMessage
{
    [Key(0)]
    public string Type { get; set; } = string.Empty;

    [Key(1)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(2)]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    [Key(3)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
