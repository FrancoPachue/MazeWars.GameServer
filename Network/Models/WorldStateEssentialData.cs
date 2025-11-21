using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Essential world state updates sent periodically.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class WorldStateEssentialData
{
    [Key(0)]
    public WorldInfoEssentialData WorldInfo { get; set; } = new();
}
