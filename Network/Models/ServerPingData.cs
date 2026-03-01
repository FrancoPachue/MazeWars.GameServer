using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class ServerPingData
{
    [Key(0)]
    public uint PingId { get; set; }

    [Key(1)]
    public float ServerTime { get; set; }
}
