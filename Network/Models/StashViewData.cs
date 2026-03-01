using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class StashViewData
{
    [Key(0)] public List<StashItemData> Items { get; set; } = new();
    [Key(1)] public int MaxItems { get; set; } = 50;
}

[MessagePackObject(keyAsPropertyName: false)]
public class StashItemData
{
    [Key(0)] public string ItemName { get; set; } = string.Empty;
    [Key(1)] public string ItemType { get; set; } = string.Empty;
    [Key(2)] public int Rarity { get; set; }
    [Key(3)] public float Weight { get; set; }
    [Key(4)] public int Value { get; set; }
    [Key(5)] public int StackCount { get; set; } = 1;
    [Key(6)] public string LootId { get; set; } = string.Empty;
}
