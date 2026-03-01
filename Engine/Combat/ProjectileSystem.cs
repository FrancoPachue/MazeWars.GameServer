using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Engine.MobIASystem.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Utils;
using Microsoft.Extensions.Logging;

namespace MazeWars.GameServer.Services.Combat;

public class ProjectileSystem
{
    private readonly ILogger<ProjectileSystem> _logger;
    private readonly List<Projectile> _activeProjectiles = new();
    private readonly object _lock = new();

    public event Action<string, CombatEvent>? OnCombatEvent;
    public event Action<RealTimePlayer, RealTimePlayer?>? OnPlayerDeath;

    private const float PlayerHitRadius = 0.5f;
    private const float MobHitRadius = 0.6f;

    public ProjectileSystem(ILogger<ProjectileSystem> logger)
    {
        _logger = logger;
    }

    public Projectile SpawnProjectile(
        RealTimePlayer owner, string abilityId,
        Vector2 direction, float speed, float hitRadius,
        float maxRange, float damage, float areaRadius,
        GameWorld world)
    {
        var projectile = new Projectile
        {
            OwnerId = owner.PlayerId,
            OwnerTeamId = owner.TeamId,
            AbilityId = abilityId,
            Position = owner.Position,
            Direction = direction,
            Speed = speed,
            HitRadius = hitRadius,
            MaxRange = maxRange,
            Damage = damage,
            AreaRadius = areaRadius,
            RoomId = owner.CurrentRoomId,
            WorldId = world.WorldId
        };

        lock (_lock)
        {
            _activeProjectiles.Add(projectile);
        }

        // Emit spawn event so clients can render the projectile
        var angle = MathF.Atan2(direction.Y, direction.X);
        OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "projectile_spawn",
            SourceId = projectile.ProjectileId,
            TargetId = owner.PlayerId,
            Value = 0,
            Position = owner.Position,
            RoomId = owner.CurrentRoomId,
            Direction = angle,
            Speed = speed,
            AbilityId = abilityId
        });

        return projectile;
    }

    /// <summary>
    /// Spawn a projectile owned by a mob. Hits all players, ignores mobs.
    /// </summary>
    public Projectile SpawnMobProjectile(
        Mob owner, string abilityId,
        Vector2 direction, float speed, float hitRadius,
        float maxRange, float damage, float areaRadius,
        GameWorld world)
    {
        var projectile = new Projectile
        {
            OwnerId = owner.MobId,
            OwnerTeamId = "",
            AbilityId = abilityId,
            Position = owner.Position,
            Direction = direction,
            Speed = speed,
            HitRadius = hitRadius,
            MaxRange = maxRange,
            Damage = damage,
            AreaRadius = areaRadius,
            RoomId = owner.RoomId,
            WorldId = world.WorldId,
            IsMobProjectile = true
        };

        lock (_lock)
        {
            _activeProjectiles.Add(projectile);
        }

        var angle = MathF.Atan2(direction.Y, direction.X);
        OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "projectile_spawn",
            SourceId = projectile.ProjectileId,
            TargetId = (owner as EnhancedMob)?.TargetPlayerId ?? "",
            Value = 1, // 1 = mob projectile flag (0 = player projectile)
            Position = owner.Position,
            RoomId = owner.RoomId,
            Direction = angle,
            Speed = speed,
            AbilityId = abilityId
        });

        return projectile;
    }

    /// <summary>
    /// Spawn multiple projectiles with angular spread (e.g. bow_multishot).
    /// </summary>
    public void SpawnMultipleProjectiles(
        RealTimePlayer owner, string abilityId,
        Vector2 baseDirection, float spreadAngleRad, int count,
        float speed, float hitRadius, float maxRange,
        float damagePerProjectile, float areaRadius,
        GameWorld world)
    {
        var baseAngle = MathF.Atan2(baseDirection.Y, baseDirection.X);
        var step = count > 1 ? spreadAngleRad / (count - 1) : 0f;
        var startAngle = baseAngle - spreadAngleRad / 2f;

        for (int i = 0; i < count; i++)
        {
            var angle = startAngle + step * i;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            SpawnProjectile(owner, abilityId, dir, speed, hitRadius,
                maxRange, damagePerProjectile, areaRadius, world);
        }
    }

    public void UpdateProjectiles(GameWorld world, float deltaTime)
    {
        List<Projectile> toRemove;

        lock (_lock)
        {
            toRemove = new List<Projectile>();

            foreach (var proj in _activeProjectiles)
            {
                if (!proj.IsActive) continue;
                // Only process projectiles belonging to this world
                if (proj.WorldId != world.WorldId) continue;

                // Move
                var movement = proj.Direction * (proj.Speed * deltaTime);
                proj.Position = new Vector2(
                    proj.Position.X + movement.X,
                    proj.Position.Y + movement.Y
                );
                proj.DistanceTraveled += proj.Speed * deltaTime;

                // Check max range
                if (proj.DistanceTraveled >= proj.MaxRange)
                {
                    ExpireProjectile(proj, world);
                    toRemove.Add(proj);
                    continue;
                }

                // Collision detection vs players (both player and mob projectiles hit players)
                bool hit = false;
                foreach (var player in world.Players.Values)
                {
                    if (!proj.IsMobProjectile && player.PlayerId == proj.OwnerId) continue;
                    if (!player.IsAlive) continue;
                    if (player.CurrentRoomId != proj.RoomId) continue;
                    if (!proj.IsMobProjectile && player.TeamId == proj.OwnerTeamId && !string.IsNullOrEmpty(proj.OwnerTeamId)) continue;

                    var dist = GameMathUtils.Distance(proj.Position, player.Position);
                    if (dist <= proj.HitRadius + PlayerHitRadius)
                    {
                        HitPlayer(proj, player, world);
                        hit = true;
                        break;
                    }
                }

                if (hit)
                {
                    toRemove.Add(proj);
                    continue;
                }

                // Collision detection vs mobs (only player projectiles hit mobs)
                if (!proj.IsMobProjectile)
                {
                    foreach (var mob in world.Mobs.Values)
                    {
                        if (mob.Health <= 0) continue;
                        if (mob.RoomId != proj.RoomId) continue;

                        var dist = GameMathUtils.Distance(proj.Position, mob.Position);
                        if (dist <= proj.HitRadius + MobHitRadius)
                        {
                            HitMob(proj, mob, world);
                            hit = true;
                            break;
                        }
                    }

                    if (hit)
                        toRemove.Add(proj);
                }
            }

            foreach (var proj in toRemove)
                _activeProjectiles.Remove(proj);
        }
    }

    private void HitPlayer(Projectile proj, RealTimePlayer target, GameWorld world)
    {
        proj.IsActive = false;

        // Apply target's damage reduction
        var damage = Math.Max(1, (int)(proj.Damage * (1f - target.DamageReduction)));

        // Apply damage (shield absorbs first, same as CombatSystem.ApplyDamage)
        var actualDamage = damage;
        if (target.Shield > 0)
        {
            var absorbed = Math.Min(target.Shield, damage);
            target.Shield -= absorbed;
            damage -= absorbed;
        }
        target.Health = Math.Max(0, target.Health - damage);
        target.LastDamageTime = DateTime.UtcNow;

        var combatEvent = new CombatEvent
        {
            EventType = "projectile_hit",
            SourceId = proj.OwnerId,
            TargetId = target.PlayerId,
            Value = actualDamage,
            Position = target.Position,
            RoomId = proj.RoomId,
            AbilityId = proj.AbilityId
        };
        OnCombatEvent?.Invoke(world.WorldId, combatEvent);

        // AOE on impact (e.g. fireball)
        if (proj.AreaRadius > 0)
            ApplyAoeDamage(proj, world);

        // Check death
        if (target.Health <= 0 && target.IsAlive)
        {
            var owner = world.Players.Values.FirstOrDefault(p => p.PlayerId == proj.OwnerId);
            target.IsAlive = false;
            target.DeathTime = DateTime.UtcNow;
            OnPlayerDeath?.Invoke(target, owner);
        }
    }

    private void HitMob(Projectile proj, Mob mob, GameWorld world)
    {
        proj.IsActive = false;
        var damage = (int)proj.Damage;
        mob.Health = Math.Max(0, mob.Health - damage);
        mob.IsDirty = true;
        if (mob is EnhancedMob em1) em1.RequiresUpdate = true;

        var combatEvent = new CombatEvent
        {
            EventType = "projectile_hit",
            SourceId = proj.OwnerId,
            TargetId = mob.MobId,
            Value = damage,
            Position = mob.Position,
            RoomId = proj.RoomId,
            AbilityId = proj.AbilityId
        };
        OnCombatEvent?.Invoke(world.WorldId, combatEvent);

        // AOE on impact
        if (proj.AreaRadius > 0)
            ApplyAoeDamage(proj, world);
    }

    private void ApplyAoeDamage(Projectile proj, GameWorld world)
    {
        var aoeDamage = (int)(proj.Damage * 0.6f); // AOE does 60% of direct damage

        // Hit nearby players
        foreach (var player in world.Players.Values)
        {
            if (!proj.IsMobProjectile && player.PlayerId == proj.OwnerId) continue;
            if (!player.IsAlive) continue;
            if (player.CurrentRoomId != proj.RoomId) continue;
            if (!proj.IsMobProjectile && player.TeamId == proj.OwnerTeamId && !string.IsNullOrEmpty(proj.OwnerTeamId)) continue;

            var dist = GameMathUtils.Distance(proj.Position, player.Position);
            if (dist > proj.AreaRadius) continue;

            // Don't double-hit the direct target (already took full damage)
            if (dist <= proj.HitRadius + PlayerHitRadius) continue;

            // Apply target's damage reduction to AOE
            var playerAoeDmg = Math.Max(1, (int)(aoeDamage * (1f - player.DamageReduction)));

            if (player.Shield > 0)
            {
                var absorbed = Math.Min(player.Shield, playerAoeDmg);
                player.Shield -= absorbed;
                var remainingDmg = playerAoeDmg - absorbed;
                player.Health = Math.Max(0, player.Health - remainingDmg);
            }
            else
            {
                player.Health = Math.Max(0, player.Health - playerAoeDmg);
            }
            player.LastDamageTime = DateTime.UtcNow;

            OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
            {
                EventType = "ability_damage",
                SourceId = proj.OwnerId,
                TargetId = player.PlayerId,
                Value = playerAoeDmg,
                Position = player.Position,
                RoomId = proj.RoomId,
                AbilityId = proj.AbilityId
            });

            if (player.Health <= 0 && player.IsAlive)
            {
                var owner = world.Players.Values.FirstOrDefault(p => p.PlayerId == proj.OwnerId);
                player.IsAlive = false;
                player.DeathTime = DateTime.UtcNow;
                OnPlayerDeath?.Invoke(player, owner);
            }
        }

        // Hit nearby mobs (only player projectiles damage mobs)
        if (!proj.IsMobProjectile)
        {
            foreach (var mob in world.Mobs.Values)
            {
                if (mob.Health <= 0) continue;
                if (mob.RoomId != proj.RoomId) continue;

                var dist = GameMathUtils.Distance(proj.Position, mob.Position);
                if (dist > proj.AreaRadius) continue;
                if (dist <= proj.HitRadius + MobHitRadius) continue; // already hit directly

                mob.Health = Math.Max(0, mob.Health - aoeDamage);
                mob.IsDirty = true;
                if (mob is EnhancedMob em2) em2.RequiresUpdate = true;

                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "ability_damage",
                    SourceId = proj.OwnerId,
                    TargetId = mob.MobId,
                    Value = aoeDamage,
                    Position = mob.Position,
                    RoomId = proj.RoomId,
                    AbilityId = proj.AbilityId
                });
            }
        }
    }

    private void ExpireProjectile(Projectile proj, GameWorld world)
    {
        proj.IsActive = false;

        OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "projectile_expire",
            SourceId = proj.ProjectileId,
            Value = 0,
            Position = proj.Position,
            RoomId = proj.RoomId,
            AbilityId = proj.AbilityId
        });
    }

    public void ClearWorldProjectiles(string worldId)
    {
        lock (_lock)
        {
            _activeProjectiles.RemoveAll(p => p.WorldId == worldId);
        }
    }
}
