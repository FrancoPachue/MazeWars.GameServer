using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class EquipmentUpdateData
{
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(1)]
    public Dictionary<int, string> EquippedItemIds { get; set; } = new();

    [Key(2)]
    public List<string> AvailableAbilities { get; set; } = new();

    [Key(3)]
    public List<InventoryItemData> InventoryItems { get; set; } = new();

    [Key(4)]
    public List<InventoryItemData> EquippedItemDetails { get; set; } = new();

    [Key(5)]
    public float CurrentWeight { get; set; }

    [Key(6)]
    public float MaxWeight { get; set; }
}

[MessagePackObject(keyAsPropertyName: false)]
public class InventoryItemData
{
    [Key(0)]
    public string LootId { get; set; } = string.Empty;

    [Key(1)]
    public string ItemName { get; set; } = string.Empty;

    [Key(2)]
    public string EquipmentId { get; set; } = string.Empty;

    [Key(3)]
    public int Rarity { get; set; }

    [Key(4)]
    public string ItemType { get; set; } = string.Empty;

    [Key(5)]
    public int SlotIndex { get; set; }

    [Key(6)]
    public int Quality { get; set; }

    [Key(7)]
    public int Quantity { get; set; } = 1;

    [Key(8)]
    public float Weight { get; set; }
}
