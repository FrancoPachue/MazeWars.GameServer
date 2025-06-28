using MazeWars.GameServer.Models;
// =============================================
// ENHANCED LOOT ITEM (if needed)
// =============================================

public class EnhancedLootItem : LootItem
{
    public DateTime SpawnedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string SpawnReason { get; set; } = string.Empty; // "mob_death", "room_clear", etc.
    public string? SpawnedBy { get; set; } // Player or mob ID
    public bool IsMagnetized { get; set; }
    public Vector2 TargetPosition { get; set; } // For magnetism
    public float MagnetSpeed { get; set; } = 2.0f;
    public bool IsQuest { get; set; }
    public string? QuestId { get; set; }
    public Dictionary<string, object> ExtendedProperties { get; set; } = new();
}
