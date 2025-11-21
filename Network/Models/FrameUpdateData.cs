using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Frame synchronization marker for chunked updates.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class FrameUpdateData
{
    [Key(0)]
    public ulong FrameNumber { get; set; }
}
