using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Combat.Models;

/// <summary>
/// Represents a projectile (skillshot) in the game world
/// Used for Battlerite-style combat with aim-based abilities
/// </summary>
public class Projectile
{
    public string ProjectileId { get; set; } = Guid.NewGuid().ToString();
    public string WorldId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;

    // Position and movement
    public Vector2 Position { get; set; }
    public Vector2 Direction { get; set; }
    public float Speed { get; set; } = 15f; // Units per second
    public float MaxRange { get; set; } = 20f; // Maximum travel distance
    public float TraveledDistance { get; set; }

    // Hitbox
    public float Radius { get; set; } = 0.5f; // Collision radius
    public bool PierceTargets { get; set; } = false;
    public int MaxPierceCount { get; set; } = 1;
    public HashSet<string> HitTargets { get; set; } = new();

    // Damage and effects
    public int BaseDamage { get; set; } = 30;
    public string DamageType { get; set; } = "physical"; // physical, magical, true
    public string? StatusEffect { get; set; } // Optional status effect to apply
    public float StatusEffectDuration { get; set; } = 0f;
    public int StatusEffectValue { get; set; } = 0;

    // Lifetime
    public DateTime SpawnTime { get; set; } = DateTime.UtcNow;
    public float MaxLifetime { get; set; } = 3f; // Seconds before auto-destroy
    public bool IsExpired => (DateTime.UtcNow - SpawnTime).TotalSeconds > MaxLifetime || TraveledDistance >= MaxRange;
    public bool IsDestroyed { get; set; }

    // Visual/Type
    public string ProjectileType { get; set; } = "arrow"; // arrow, fireball, bolt, etc.

    // Lag compensation - store spawn time info for hit validation
    public float ClientTimestamp { get; set; }
    public uint InputSequence { get; set; }

    /// <summary>
    /// Update projectile position based on delta time
    /// </summary>
    public void UpdatePosition(float deltaTime)
    {
        var movement = Direction * Speed * deltaTime;
        Position += movement;
        TraveledDistance += movement.Magnitude;
    }

    /// <summary>
    /// Check if this projectile can hit a target
    /// </summary>
    public bool CanHitTarget(string targetId, string targetTeamId)
    {
        // Don't hit owner
        if (targetId == OwnerId) return false;

        // Don't hit teammates (unless friendly fire enabled)
        if (targetTeamId == TeamId) return false;

        // Don't hit same target twice (unless piercing)
        if (HitTargets.Contains(targetId) && !PierceTargets) return false;

        // Check pierce limit
        if (HitTargets.Count >= MaxPierceCount) return false;

        return true;
    }

    /// <summary>
    /// Record a hit on a target
    /// </summary>
    public void RecordHit(string targetId)
    {
        HitTargets.Add(targetId);

        if (!PierceTargets || HitTargets.Count >= MaxPierceCount)
        {
            IsDestroyed = true;
        }
    }
}

/// <summary>
/// Result of a projectile hit
/// </summary>
public class ProjectileHitResult
{
    public bool Hit { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public int DamageDealt { get; set; }
    public Vector2 HitPosition { get; set; }
    public bool TargetKilled { get; set; }
    public string? StatusEffectApplied { get; set; }
}

/// <summary>
/// Configuration for different projectile types
/// </summary>
public static class ProjectilePresets
{
    public static Projectile CreateArrow(string ownerId, string teamId, Vector2 position, Vector2 direction)
    {
        return new Projectile
        {
            OwnerId = ownerId,
            TeamId = teamId,
            Position = position,
            Direction = direction.GetNormalized(),
            ProjectileType = "arrow",
            Speed = 20f,
            MaxRange = 25f,
            BaseDamage = 25,
            Radius = 0.3f,
            PierceTargets = false
        };
    }

    public static Projectile CreateFireball(string ownerId, string teamId, Vector2 position, Vector2 direction)
    {
        return new Projectile
        {
            OwnerId = ownerId,
            TeamId = teamId,
            Position = position,
            Direction = direction.GetNormalized(),
            ProjectileType = "fireball",
            Speed = 12f,
            MaxRange = 18f,
            BaseDamage = 45,
            Radius = 0.8f,
            DamageType = "magical",
            StatusEffect = "burn",
            StatusEffectDuration = 3f,
            StatusEffectValue = 5
        };
    }

    public static Projectile CreateIceBolt(string ownerId, string teamId, Vector2 position, Vector2 direction)
    {
        return new Projectile
        {
            OwnerId = ownerId,
            TeamId = teamId,
            Position = position,
            Direction = direction.GetNormalized(),
            ProjectileType = "ice_bolt",
            Speed = 18f,
            MaxRange = 22f,
            BaseDamage = 30,
            Radius = 0.4f,
            DamageType = "magical",
            StatusEffect = "slow",
            StatusEffectDuration = 2f,
            StatusEffectValue = -50
        };
    }

    public static Projectile CreatePiercingArrow(string ownerId, string teamId, Vector2 position, Vector2 direction)
    {
        return new Projectile
        {
            OwnerId = ownerId,
            TeamId = teamId,
            Position = position,
            Direction = direction.GetNormalized(),
            ProjectileType = "piercing_arrow",
            Speed = 25f,
            MaxRange = 30f,
            BaseDamage = 20,
            Radius = 0.25f,
            PierceTargets = true,
            MaxPierceCount = 3
        };
    }
}
