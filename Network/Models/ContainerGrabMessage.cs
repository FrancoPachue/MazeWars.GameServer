using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Client request to grab a specific item from a loot container (chest/corpse).
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ContainerGrabMessage
{
    [Key(0)]
    public string ContainerId { get; set; } = string.Empty;

    [Key(1)]
    public string LootId { get; set; } = string.Empty;
}
