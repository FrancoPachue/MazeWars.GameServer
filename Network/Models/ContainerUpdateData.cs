using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Container update sent from server to client.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ContainerUpdate
{
    [Key(0)]
    public string UpdateType { get; set; } = string.Empty; // "spawned", "updated", "removed"

    [Key(1)]
    public string ContainerId { get; set; } = string.Empty;

    [Key(2)]
    public string ContainerType { get; set; } = string.Empty; // "chest", "mob_corpse", "player_corpse"

    [Key(3)]
    public float PositionX { get; set; }

    [Key(4)]
    public float PositionY { get; set; }

    [Key(5)]
    public string RoomId { get; set; } = string.Empty;

    [Key(6)]
    public string DisplayName { get; set; } = string.Empty;

    [Key(7)]
    public List<ContainerItemInfo> Items { get; set; } = new();
}

[MessagePackObject(keyAsPropertyName: false)]
public class ContainerItemInfo
{
    [Key(0)]
    public string LootId { get; set; } = string.Empty;

    [Key(1)]
    public string ItemName { get; set; } = string.Empty;

    [Key(2)]
    public string ItemType { get; set; } = string.Empty;

    [Key(3)]
    public int Rarity { get; set; }

    [Key(4)]
    public int Quality { get; set; }
}

[MessagePackObject(keyAsPropertyName: false)]
public class ContainerUpdatesData
{
    [Key(0)]
    public List<ContainerUpdate> ContainerUpdates { get; set; } = new();
}
