using MazeWars.GameServer.Models;
// =============================================
// LOOT SPAWNING CONTEXT
// =============================================

public class LootSpawnContext
{
    public string WorldId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public LootTrigger Trigger { get; set; }
    public string? TriggeredBy { get; set; } // Player or mob ID
    public RealTimePlayer? Player { get; set; }
    public Mob? Mob { get; set; }
    public Room? Room { get; set; }
    public float LuckModifier { get; set; } = 1.0f;
    public Dictionary<string, object> AdditionalContext { get; set; } = new();
}