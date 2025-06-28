namespace MazeWars.GameServer.Network.Models;

// Update Messages (Server → Client)
public class WorldUpdateMessage
{
    public List<PlayerStateUpdate> Players { get; set; } = new();
    public List<CombatEvent> CombatEvents { get; set; } = new();
    public List<LootUpdate> LootUpdates { get; set; } = new();
    public List<MobUpdate> MobUpdates { get; set; } = new();
    public int FrameNumber { get; set; }
}
