using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class InsureItemMessage
{
    [Key(0)] public string LootId { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: false)]
public class InsureItemResponseData
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string Error { get; set; } = string.Empty;
    [Key(2)] public int Cost { get; set; }
    [Key(3)] public long RemainingGold { get; set; }
    [Key(4)] public string ItemName { get; set; } = string.Empty;
}
