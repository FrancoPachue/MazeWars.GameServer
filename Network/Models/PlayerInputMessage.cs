using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

// Input Messages (Client → Server)
public class PlayerInputMessage
{
    public Vector2 MoveInput { get; set; }
    public bool IsSprinting { get; set; }
    public float AimDirection { get; set; }
    public bool IsAttacking { get; set; }
    public string AbilityType { get; set; } = string.Empty;
    public Vector2 AbilityTarget { get; set; }
}
