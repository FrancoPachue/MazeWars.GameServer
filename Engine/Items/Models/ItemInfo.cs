namespace MazeWars.GameServer.Engine.Items.Models;

public class ItemInfo
{
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ItemType { get; set; } = "";
    public int Rarity { get; set; }
    public bool CanUse { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}