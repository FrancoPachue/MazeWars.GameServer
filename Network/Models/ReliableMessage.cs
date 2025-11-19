using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Reliable messaging with retry and acknowledgement.
/// </summary>
[MessagePackObject]
public class ReliableMessage
{
    [Key(0)]
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    [Key(1)]
    public string Type { get; set; } = string.Empty;

    [Key(2)]
    public object Data { get; set; } = null!;

    [Key(3)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Key(4)]
    public int RetryCount { get; set; } = 0;

    [Key(5)]
    public bool RequiresAck { get; set; } = true;
}
