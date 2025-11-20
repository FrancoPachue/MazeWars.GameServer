using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class PlayerStateUpdate
{
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(1)]
    public Vector2 Position { get; set; }

    [Key(2)]
    public Vector2 Velocity { get; set; }

    [Key(3)]
    public float Direction { get; set; }

    [Key(4)]
    public int Health { get; set; }

    [Key(5)]
    public int MaxHealth { get; set; }

    [Key(6)]
    public bool IsAlive { get; set; }

    [Key(7)]
    public bool IsMoving { get; set; }

    [Key(8)]
    public bool IsCasting { get; set; }

    [Key(9)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(10)]
    public string PlayerClass { get; set; } = string.Empty;
}
