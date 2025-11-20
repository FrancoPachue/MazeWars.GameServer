using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// World boundary dimensions.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class WorldBoundsData
{
    [Key(0)]
    public int X { get; set; }

    [Key(1)]
    public int Y { get; set; }
}
