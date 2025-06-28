namespace MazeWars.GameServer.Network.Models;

public class InventoryUpdate
{
    public string PlayerId { get; set; } = string.Empty;
    public List<InventoryItem> Items { get; set; } = new();
    public int TotalValue { get; set; }
}
