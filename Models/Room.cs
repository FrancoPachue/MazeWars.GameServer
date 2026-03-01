namespace MazeWars.GameServer.Models;

public class Room
{
    public string RoomId { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; }
    public List<string> Connections { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string CompletedByTeam { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public List<string> SpawnedLootIds { get; set; } = new();

    /// <summary>
    /// Room type determines mob composition and loot rarity modifiers.
    /// Values: empty, patrol, guard_post, ambush, elite_chamber, boss_arena, treasure_vault
    /// </summary>
    public string RoomType { get; set; } = "patrol";
}
