using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Simplified player state for batch updates.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PlayerUpdateData
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
    public int Mana { get; set; }

    [Key(7)]
    public int MaxMana { get; set; }

    [Key(8)]
    public int Level { get; set; }

    [Key(9)]
    public bool IsAlive { get; set; }

    [Key(10)]
    public bool IsMoving { get; set; }

    [Key(11)]
    public bool IsCasting { get; set; }

    [Key(12)]
    public string CurrentRoomId { get; set; } = string.Empty;
}
