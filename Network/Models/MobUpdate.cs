using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class MobUpdate
{
    [Key(0)]
    public string MobId { get; set; } = string.Empty;

    [Key(1)]
    public Vector2 Position { get; set; }

    [Key(2)]
    public string State { get; set; } = string.Empty;

    [Key(3)]
    public int Health { get; set; }

    [Key(4)]
    public string RoomId { get; set; } = string.Empty;

    [Key(5)]
    public string MobType { get; set; } = string.Empty;

    [Key(6)]
    public int MaxHealth { get; set; }

    [Key(7)]
    public int Phase { get; set; }
}
