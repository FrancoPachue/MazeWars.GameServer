using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Server configuration information sent on connection.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ServerInfoData
{
    [Key(0)]
    public int TickRate { get; set; }

    [Key(1)]
    public WorldBoundsData WorldBounds { get; set; } = new();

    [Key(2)]
    public int MaxPlayersPerWorld { get; set; }

    [Key(3)]
    public int MinPlayersToStart { get; set; }
}
