using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class CombatEvent
{
    [Key(0)]
    public string EventType { get; set; } = string.Empty;

    [Key(1)]
    public string SourceId { get; set; } = string.Empty;

    [Key(2)]
    public string TargetId { get; set; } = string.Empty;

    [Key(3)]
    public int Value { get; set; }

    [Key(4)]
    public Vector2 Position { get; set; }

    [Key(5)]
    public string RoomId { get; set; } = string.Empty;

    [Key(6)]
    public float Direction { get; set; }

    [Key(7)]
    public float Speed { get; set; }

    [Key(8)]
    public string AbilityId { get; set; } = string.Empty;

    [IgnoreMember]
    public Dictionary<string, object> AdditionalData { get; internal set; }
}
