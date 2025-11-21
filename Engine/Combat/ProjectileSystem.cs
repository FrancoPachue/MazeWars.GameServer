using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MazeWars.GameServer.Engine.Combat;

/// <summary>
/// Manages projectiles (skillshots) for Battlerite-style combat
/// Handles spawning, movement, collision detection, and lag compensation
/// </summary>
public interface IProjectileSystem
{
    Projectile SpawnProjectile(Projectile projectile, GameWorld world);
    void UpdateProjectiles(GameWorld world, float deltaTime);
    List<Projectile> GetWorldProjectiles(string worldId);
    void CleanupWorld(string worldId);

    event Action<string, ProjectileHitResult, Projectile>? OnProjectileHit;
    event Action<string, Projectile>? OnProjectileSpawned;
    event Action<string, Projectile>? OnProjectileDestroyed;
}

public class ProjectileSystem : IProjectileSystem
{
    private readonly ILogger<ProjectileSystem> _logger;
    private readonly ICombatSystem _combatSystem;

    // Store projectiles per world for efficient processing
    private readonly ConcurrentDictionary<string, List<Projectile>> _worldProjectiles = new();

    // Lag compensation: Store recent position snapshots for hit validation
    private readonly ConcurrentDictionary<string, Queue<PositionSnapshot>> _playerPositionHistory = new();
    private const int MAX_POSITION_HISTORY = 20; // ~333ms at 60fps
    private const float LAG_COMPENSATION_MAX_MS = 200f; // Max rewind time

    public event Action<string, ProjectileHitResult, Projectile>? OnProjectileHit;
    public event Action<string, Projectile>? OnProjectileSpawned;
    public event Action<string, Projectile>? OnProjectileDestroyed;

    public ProjectileSystem(ILogger<ProjectileSystem> logger, ICombatSystem combatSystem)
    {
        _logger = logger;
        _combatSystem = combatSystem;
    }

    /// <summary>
    /// Spawn a new projectile into the game world
    /// </summary>
    public Projectile SpawnProjectile(Projectile projectile, GameWorld world)
    {
        projectile.WorldId = world.WorldId;
        projectile.SpawnTime = DateTime.UtcNow;

        var projectiles = _worldProjectiles.GetOrAdd(world.WorldId, _ => new List<Projectile>());

        lock (projectiles)
        {
            projectiles.Add(projectile);
        }

        _logger.LogDebug("Projectile {Type} spawned by {Owner} at {Position}",
            projectile.ProjectileType, projectile.OwnerId, projectile.Position);

        OnProjectileSpawned?.Invoke(world.WorldId, projectile);

        return projectile;
    }

    /// <summary>
    /// Update all projectiles in a world (call each game tick)
    /// </summary>
    public void UpdateProjectiles(GameWorld world, float deltaTime)
    {
        if (!_worldProjectiles.TryGetValue(world.WorldId, out var projectiles))
            return;

        var toRemove = new List<Projectile>();

        // First, store current player positions for lag compensation
        StorePlayerPositions(world);

        lock (projectiles)
        {
            foreach (var projectile in projectiles)
            {
                if (projectile.IsDestroyed || projectile.IsExpired)
                {
                    toRemove.Add(projectile);
                    continue;
                }

                // Update projectile position
                projectile.UpdatePosition(deltaTime);

                // Check collisions with players
                var hitResults = CheckProjectileCollisions(projectile, world);
                foreach (var hitResult in hitResults)
                {
                    ProcessHit(projectile, hitResult, world);
                }

                // Check if projectile should be removed
                if (projectile.IsDestroyed || projectile.IsExpired)
                {
                    toRemove.Add(projectile);
                }
            }

            // Remove expired/destroyed projectiles
            foreach (var projectile in toRemove)
            {
                projectiles.Remove(projectile);
                OnProjectileDestroyed?.Invoke(world.WorldId, projectile);
            }
        }
    }

    /// <summary>
    /// Check if a projectile collides with any players
    /// Uses lag compensation to rewind player positions
    /// </summary>
    private List<ProjectileHitResult> CheckProjectileCollisions(Projectile projectile, GameWorld world)
    {
        var results = new List<ProjectileHitResult>();

        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive) continue;
            if (!projectile.CanHitTarget(player.PlayerId, player.TeamId)) continue;

            // Get player position (with lag compensation if needed)
            var targetPosition = GetLagCompensatedPosition(player, projectile.ClientTimestamp);

            // Check circle-circle collision
            var distance = Vector2.Distance(projectile.Position, targetPosition);
            var collisionDistance = projectile.Radius + 0.4f; // Player hitbox radius

            if (distance <= collisionDistance)
            {
                results.Add(new ProjectileHitResult
                {
                    Hit = true,
                    TargetId = player.PlayerId,
                    HitPosition = projectile.Position
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Process a projectile hit on a target
    /// </summary>
    private void ProcessHit(Projectile projectile, ProjectileHitResult hitResult, GameWorld world)
    {
        if (!world.Players.TryGetValue(hitResult.TargetId, out var target))
            return;

        // Calculate damage
        var damage = CalculateProjectileDamage(projectile, target);
        hitResult.DamageDealt = damage;

        // Apply damage
        ApplyProjectileDamage(target, damage, projectile);

        // Check for kill
        if (target.Health <= 0 && target.IsAlive)
        {
            hitResult.TargetKilled = true;
            target.IsAlive = false;
            target.Health = 0;
            target.DeathTime = DateTime.UtcNow;
            target.ForceNextUpdate();

            _logger.LogInformation("Player {Target} killed by projectile from {Owner}",
                target.PlayerName, projectile.OwnerId);
        }

        // Apply status effect
        if (!string.IsNullOrEmpty(projectile.StatusEffect) && projectile.StatusEffectDuration > 0)
        {
            var effect = new StatusEffect
            {
                EffectType = projectile.StatusEffect,
                Value = projectile.StatusEffectValue,
                ExpiresAt = DateTime.UtcNow.AddSeconds(projectile.StatusEffectDuration),
                SourcePlayerId = projectile.OwnerId
            };

            _combatSystem.ApplyStatusEffect(target, effect);
            hitResult.StatusEffectApplied = projectile.StatusEffect;
        }

        // Record hit on projectile (for pierce tracking)
        projectile.RecordHit(hitResult.TargetId);

        _logger.LogDebug("Projectile hit {Target} for {Damage} damage",
            target.PlayerName, damage);

        // Emit event
        OnProjectileHit?.Invoke(world.WorldId, hitResult, projectile);

        // Create combat event for network broadcast
        var combatEvent = new CombatEvent
        {
            EventType = "projectile_hit",
            SourceId = projectile.OwnerId,
            TargetId = hitResult.TargetId,
            Value = damage,
            Position = hitResult.HitPosition
        };
    }

    /// <summary>
    /// Calculate projectile damage with modifiers
    /// </summary>
    private int CalculateProjectileDamage(Projectile projectile, RealTimePlayer target)
    {
        var baseDamage = (float)projectile.BaseDamage;

        // Apply damage type modifiers
        switch (projectile.DamageType)
        {
            case "magical":
                // Check for magic resistance
                if (target.Stats.TryGetValue("magic_resist", out var magicResist))
                {
                    baseDamage *= (1f - magicResist * 0.01f);
                }
                break;

            case "physical":
                // Check for armor
                if (target.Stats.TryGetValue("armor", out var armor))
                {
                    baseDamage *= (1f - armor * 0.01f);
                }
                break;

            case "true":
                // True damage ignores all resistances
                break;
        }

        // Apply general damage reduction
        baseDamage *= (1f - target.DamageReduction);

        // Distance falloff (optional - damage decreases at max range)
        var rangeRatio = projectile.TraveledDistance / projectile.MaxRange;
        if (rangeRatio > 0.8f)
        {
            var falloffMultiplier = 1f - ((rangeRatio - 0.8f) * 0.5f);
            baseDamage *= falloffMultiplier;
        }

        return Math.Max(1, (int)baseDamage);
    }

    /// <summary>
    /// Apply damage from projectile (shield absorbs first)
    /// </summary>
    private void ApplyProjectileDamage(RealTimePlayer target, int damage, Projectile projectile)
    {
        var remainingDamage = damage;

        // Shield absorbs first
        if (target.Shield > 0)
        {
            var shieldDamage = Math.Min(target.Shield, remainingDamage);
            target.Shield -= shieldDamage;
            remainingDamage -= shieldDamage;
        }

        // Apply to health
        if (remainingDamage > 0)
        {
            target.Health = Math.Max(0, target.Health - remainingDamage);
        }

        target.LastDamageTime = DateTime.UtcNow;
    }

    #region Lag Compensation

    /// <summary>
    /// Store current player positions for lag compensation
    /// </summary>
    private void StorePlayerPositions(GameWorld world)
    {
        var now = DateTime.UtcNow;

        foreach (var player in world.Players.Values)
        {
            var history = _playerPositionHistory.GetOrAdd(
                player.PlayerId,
                _ => new Queue<PositionSnapshot>());

            lock (history)
            {
                history.Enqueue(new PositionSnapshot
                {
                    Position = player.Position,
                    Timestamp = now
                });

                // Keep history limited
                while (history.Count > MAX_POSITION_HISTORY)
                {
                    history.Dequeue();
                }
            }
        }
    }

    /// <summary>
    /// Get player position at a past time (for lag compensation)
    /// </summary>
    private Vector2 GetLagCompensatedPosition(RealTimePlayer player, float clientTimestamp)
    {
        // If no lag compensation requested, return current position
        if (clientTimestamp <= 0)
            return player.Position;

        if (!_playerPositionHistory.TryGetValue(player.PlayerId, out var history))
            return player.Position;

        // Convert client timestamp to DateTime
        var clientTime = DateTime.UnixEpoch.AddSeconds(clientTimestamp);
        var latency = (DateTime.UtcNow - clientTime).TotalMilliseconds;

        // Cap lag compensation to prevent abuse
        if (latency > LAG_COMPENSATION_MAX_MS || latency < 0)
            return player.Position;

        lock (history)
        {
            var snapshots = history.ToArray();
            if (snapshots.Length < 2)
                return player.Position;

            // Find two snapshots to interpolate between
            for (int i = snapshots.Length - 1; i > 0; i--)
            {
                if (snapshots[i].Timestamp >= clientTime && snapshots[i - 1].Timestamp <= clientTime)
                {
                    // Interpolate between these two snapshots
                    var t = (float)((clientTime - snapshots[i - 1].Timestamp).TotalMilliseconds /
                            (snapshots[i].Timestamp - snapshots[i - 1].Timestamp).TotalMilliseconds);

                    return Vector2.Lerp(snapshots[i - 1].Position, snapshots[i].Position, t);
                }
            }
        }

        return player.Position;
    }

    /// <summary>
    /// Cleanup player position history
    /// </summary>
    public void CleanupPlayerHistory(string playerId)
    {
        _playerPositionHistory.TryRemove(playerId, out _);
    }

    #endregion

    /// <summary>
    /// Get all active projectiles in a world
    /// </summary>
    public List<Projectile> GetWorldProjectiles(string worldId)
    {
        if (_worldProjectiles.TryGetValue(worldId, out var projectiles))
        {
            lock (projectiles)
            {
                return new List<Projectile>(projectiles);
            }
        }
        return new List<Projectile>();
    }

    /// <summary>
    /// Cleanup all projectiles for a world
    /// </summary>
    public void CleanupWorld(string worldId)
    {
        _worldProjectiles.TryRemove(worldId, out _);
        _logger.LogDebug("Cleaned up projectiles for world {WorldId}", worldId);
    }
}

/// <summary>
/// Position snapshot for lag compensation
/// </summary>
public class PositionSnapshot
{
    public Vector2 Position { get; set; }
    public DateTime Timestamp { get; set; }
}
