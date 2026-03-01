namespace MazeWars.GameServer.Models;

public class LootContainer
{
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerType { get; set; } = "chest"; // "chest", "mob_corpse", "player_corpse"
    public Vector2 Position { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public List<LootItem> Contents { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public float DespawnAfterSeconds { get; set; } // 0 = permanent (chests), >0 = despawn timer (corpses)
    public string SourceId { get; set; } = string.Empty; // MobId or PlayerId that died
    public string DisplayName { get; set; } = string.Empty; // "Treasure Chest", "Fallen Guard", etc.
}
