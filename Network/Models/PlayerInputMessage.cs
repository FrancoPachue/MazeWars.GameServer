using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

// Input Messages (Client → Server)
[MessagePackObject(keyAsPropertyName: false)]
public class PlayerInputMessage
{
    // ⭐ SYNC CRITICAL: Sequence number for input ordering
    [Key(0)]
    public uint SequenceNumber { get; set; }

    // ⭐ SYNC: Last server update the client received (for reconciliation)
    [Key(1)]
    public uint AckSequenceNumber { get; set; }

    // ⭐ SYNC: Client timestamp for lag compensation
    [Key(2)]
    public float ClientTimestamp { get; set; }

    // Movement input
    [Key(3)]
    public Vector2 MoveInput { get; set; }

    [Key(4)]
    public bool IsSprinting { get; set; }

    // Combat input
    [Key(5)]
    public float AimDirection { get; set; }

    [Key(6)]
    public bool IsAttacking { get; set; }

    [Key(7)]
    public string AbilityType { get; set; } = string.Empty;

    [Key(8)]
    public Vector2 AbilityTarget { get; set; }

    // Click-to-move
    [Key(9)]
    public Vector2 MoveTarget { get; set; }

    [Key(10)]
    public bool HasMoveTarget { get; set; }

    [Key(11)]
    public bool StopMovement { get; set; }

    // Selected target entity for auto-attacks
    [Key(12)]
    public string TargetEntityId { get; set; } = string.Empty;
}
