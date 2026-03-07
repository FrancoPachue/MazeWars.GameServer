using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class BuyFromVendorMessage
{
    [Key(0)] public string EquipmentId { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: false)]
public class SellToVendorMessage
{
    [Key(0)] public string LootId { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: false)]
public class VendorCatalogItem
{
    [Key(0)] public string EquipmentId { get; set; } = string.Empty;
    [Key(1)] public string DisplayName { get; set; } = string.Empty;
    [Key(2)] public string Slot { get; set; } = string.Empty;
    [Key(3)] public int Price { get; set; }
    [Key(4)] public int RequiredLevel { get; set; }
}

[MessagePackObject(keyAsPropertyName: false)]
public class VendorCatalogData
{
    [Key(0)] public List<VendorCatalogItem> Items { get; set; } = new();
    [Key(1)] public long PlayerGold { get; set; }
}

[MessagePackObject(keyAsPropertyName: false)]
public class VendorBuyResponseData
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string Error { get; set; } = string.Empty;
    [Key(2)] public long RemainingGold { get; set; }
    [Key(3)] public string ItemName { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: false)]
public class VendorSellResponseData
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string Error { get; set; } = string.Empty;
    [Key(2)] public int GoldEarned { get; set; }
    [Key(3)] public long TotalGold { get; set; }
}
