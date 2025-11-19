namespace MazeWars.GameServer.Network.Models;

// Update Messages (Server → Client)
public class WorldUpdateMessage
{
    // ⭐ SYNC CRITICAL: Acknowledged input sequences per player (for client reconciliation)
    public Dictionary<string, uint> AcknowledgedInputs { get; set; } = new();

    // ⭐ SYNC: Server time for lag compensation
    public float ServerTime { get; set; }

    // Frame counter
    public int FrameNumber { get; set; }

    // Game state updates
    public List<PlayerStateUpdate> Players { get; set; } = new();
    public List<CombatEvent> CombatEvents { get; set; } = new();
    public List<LootUpdate> LootUpdates { get; set; } = new();
    public List<MobUpdate> MobUpdates { get; set; } = new();
}
