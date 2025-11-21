using MessagePack;

namespace MazeWars.GameServer.Network.Models;

// Update Messages (Server → Client)
[MessagePackObject(keyAsPropertyName: false)]
public class WorldUpdateMessage
{
    // ⭐ SYNC CRITICAL: Acknowledged input sequences per player (for client reconciliation)
    [Key(0)]
    public Dictionary<string, uint> AcknowledgedInputs { get; set; } = new();

    // ⭐ SYNC: Server time for lag compensation
    [Key(1)]
    public float ServerTime { get; set; }

    // Frame counter
    [Key(2)]
    public int FrameNumber { get; set; }

    // Game state updates
    [Key(3)]
    public List<PlayerStateUpdate> Players { get; set; } = new();

    [Key(4)]
    public List<CombatEvent> CombatEvents { get; set; } = new();

    [Key(5)]
    public List<LootUpdate> LootUpdates { get; set; } = new();

    [Key(6)]
    public List<MobUpdate> MobUpdates { get; set; } = new();
}
