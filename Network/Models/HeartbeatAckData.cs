using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Server acknowledgement for client heartbeat.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class HeartbeatAckData
{
    [Key(0)]
    public DateTime ServerTime { get; set; }

    [Key(1)]
    public DateTime ClientLastActivity { get; set; }
}
