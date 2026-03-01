using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Combat.Models;

public class Projectile
{
    public string ProjectileId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerTeamId { get; set; } = string.Empty;
    public string AbilityId { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public Vector2 Direction { get; set; }
    public float Speed { get; set; }
    public float HitRadius { get; set; } = 0.3f;
    public float MaxRange { get; set; }
    public float DistanceTraveled { get; set; }
    public float Damage { get; set; }
    public float AreaRadius { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string WorldId { get; set; } = string.Empty;
    public bool IsMobProjectile { get; set; }
}
