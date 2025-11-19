using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

// Input Messages (Client → Server)
public class PlayerInputMessage
{
    // ⭐ SYNC CRITICAL: Sequence number for input ordering
    public uint SequenceNumber { get; set; }

    // ⭐ SYNC: Last server update the client received (for reconciliation)
    public uint AckSequenceNumber { get; set; }

    // ⭐ SYNC: Client timestamp for lag compensation
    public float ClientTimestamp { get; set; }

    // Movement input
    public Vector2 MoveInput { get; set; }
    public bool IsSprinting { get; set; }

    // Combat input
    public float AimDirection { get; set; }
    public bool IsAttacking { get; set; }
    public string AbilityType { get; set; } = string.Empty;
    public Vector2 AbilityTarget { get; set; }
}
