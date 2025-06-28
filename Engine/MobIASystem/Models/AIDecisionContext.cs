using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public class AIDecisionContext
{
    public Mob Mob { get; set; } = null!;
    public GameWorld World { get; set; } = null!;
    public List<RealTimePlayer> NearbyPlayers { get; set; } = new();
    public List<Mob> NearbyMobs { get; set; } = new();
    public Room CurrentRoom { get; set; } = null!;
    public float DeltaTime { get; set; }
    public Dictionary<string, object> ContextData { get; set; } = new();
}
