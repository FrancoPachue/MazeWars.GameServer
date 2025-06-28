namespace MazeWars.GameServer.Network.Models;

public class InventoryItem
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public int Rarity { get; set; }
    public int Quantity { get; set; } = 1;
    public Dictionary<string, object> Properties { get; set; } = new();
}
