namespace MazeWars.GameServer.Models;

public class LootItem
{
    public string LootId { get; set; } = string.Empty;

    /// <summary>
    /// Alias for LootId (for backward compatibility with SessionManager)
    /// </summary>
    public string ItemId
    {
        get => LootId;
        set => LootId = value;
    }

    public string ItemName { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public int Rarity { get; set; } = 1;
    public Vector2 Position { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public DateTime SpawnedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Properties { get; set; } = new();

    /// <summary>
    /// Item stats (strength, magic, etc.) for backward compatibility
    /// </summary>
    public Dictionary<string, int> Stats { get; set; } = new();

    /// <summary>
    /// Gold value of the item
    /// </summary>
    public int Value { get; set; }
}
