using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class PlayerStateUpdate
{
    public string PlayerId { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Direction { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public bool IsAlive { get; set; }
    public bool IsMoving { get; set; }
    public bool IsCasting { get; set; }
}
