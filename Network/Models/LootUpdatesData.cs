using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Loot updates that occurred during the frame.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class LootUpdatesData
{
    [Key(0)]
    public List<LootUpdate> LootUpdates { get; set; } = new();
}
