using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class CombatEvent
{
    public string EventType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int Value { get; set; }
    public Vector2 Position { get; set; }
    public Dictionary<string, object> AdditionalData { get; internal set; }
}
