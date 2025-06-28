namespace MazeWars.GameServer.Models;

public class LootDrop
{
    public string ItemName { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public int Rarity { get; set; } = 1;
    public float DropChance { get; set; } = 0.1f;
    public int MinQuantity { get; set; } = 1;
    public int MaxQuantity { get; set; } = 1;
    public Dictionary<string, object> Properties { get; set; } = new();
}
