using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject]
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
}
