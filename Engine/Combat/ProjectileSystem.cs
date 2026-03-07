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
        GameWorld world,
        string? appliesEffect = null, int effectValue = 0, int effectDurationMs = 0)
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
            WorldId = world.WorldId,
            OwnerCritChance = owner.CritChance,
            AppliesEffect = appliesEffect,
            EffectValue = effectValue,
            EffectDurationMs = effectDurationMs,
            EffectSourcePlayerId = owner.PlayerId
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
        GameWorld world,
        string? appliesEffect = null, int effectValue = 0, int effectDurationMs = 0)
    {
        var baseAngle = MathF.Atan2(baseDirection.Y, baseDirection.X);
        var step = count > 1 ? spreadAngleRad / (count - 1) : 0f;
        var startAngle = baseAngle - spreadAngleRad / 2f;

        for (int i = 0; i < count; i++)
        {
            var angle = startAngle + step * i;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            SpawnProjectile(owner, abilityId, dir, speed, hitRadius,
                maxRange, damagePerProjectile, areaRadius, world,
                appliesEffect, effectValue, effectDurationMs);
        }
    }

    // Deferred hit info collected inside lock, processed outside
    private enum HitKind { Player, Mob, Expire }
    private readonly record struct DeferredHit(Projectile Projectile, HitKind Kind, RealTimePlayer? Player, Mob? Mob);

    public void UpdateProjectiles(GameWorld world, float deltaTime)
    {
        List<DeferredHit> deferredHits;

        lock (_lock)
        {
            var toRemove = new List<Projectile>();
            deferredHits = new List<DeferredHit>();

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
                    proj.IsActive = false;
                    deferredHits.Add(new DeferredHit(proj, HitKind.Expire, null, null));
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
                        proj.IsActive = false;
                        deferredHits.Add(new DeferredHit(proj, HitKind.Player, player, null));
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
                            proj.IsActive = false;
                            deferredHits.Add(new DeferredHit(proj, HitKind.Mob, null, mob));
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

        // Process hits outside the lock to avoid firing events while holding _lock
        foreach (var hit in deferredHits)
        {
            switch (hit.Kind)
            {
                case HitKind.Player:
                    HitPlayer(hit.Projectile, hit.Player!, world);
                    break;
                case HitKind.Mob:
                    HitMob(hit.Projectile, hit.Mob!, world);
                    break;
                case HitKind.Expire:
                    ExpireProjectile(hit.Projectile, world);
                    break;
            }
        }
    }

    private void HitPlayer(Projectile proj, RealTimePlayer target, GameWorld world)
    {
        proj.IsActive = false;

        // Roll crit on hit (not at spawn, so each target gets independent roll)
        bool isCrit = !proj.IsMobProjectile && Random.Shared.NextSingle() < proj.OwnerCritChance;
        var baseDamage = proj.Damage;

        // Apply damage variance ±10%
        baseDamage *= 1f + (Random.Shared.NextSingle() * 0.2f - 0.1f);

        if (isCrit) baseDamage *= 1.5f;

        // Apply target's damage reduction (capped at 70% for PvP)
        var damage = Math.Max(1, (int)(baseDamage * (1f - Math.Clamp(target.DamageReduction, 0f, 0.70f))));

        // Apply damage (shield absorbs first, reset DR when shield breaks)
        var actualDamage = damage;
        if (target.Shield > 0)
        {
            var absorbed = Math.Min(target.Shield, damage);
            target.Shield -= absorbed;
            damage -= absorbed;

            if (target.Shield <= 0)
            {
                lock (target.StatusEffectsLock)
                {
                    target.StatusEffects.RemoveAll(e => e.EffectType == "shield");
                }
                target.DamageReduction = target.EquipmentDamageReduction;
            }
        }
        target.Health = Math.Max(0, target.Health - damage);
        target.LastDamageTime = DateTime.UtcNow;

        var combatEvent = new CombatEvent
        {
            EventType = isCrit ? "projectile_hit_crit" : "projectile_hit",
            SourceId = proj.OwnerId,
            TargetId = target.PlayerId,
            Value = actualDamage,
            Position = target.Position,
            RoomId = proj.RoomId,
            AbilityId = proj.AbilityId
        };
        OnCombatEvent?.Invoke(world.WorldId, combatEvent);

        // Apply status effect on hit (e.g., slow from frostbolt, poison from projectile abilities)
        if (!string.IsNullOrEmpty(proj.AppliesEffect) && proj.EffectDurationMs > 0)
        {
            var effect = new StatusEffect
            {
                EffectType = proj.AppliesEffect,
                Value = proj.EffectValue,
                ExpiresAt = DateTime.UtcNow.AddMilliseconds(proj.EffectDurationMs),
                SourcePlayerId = proj.EffectSourcePlayerId
            };
            ApplyStatusEffect(target, effect);
        }

        // AOE on impact (e.g. fireball) — exclude direct target from AOE
        if (proj.AreaRadius > 0)
            ApplyAoeDamage(proj, world, excludePlayerId: target.PlayerId);

        // Check death
        if (target.Health <= 0 && target.TryKill())
        {
            var owner = world.Players.Values.FirstOrDefault(p => p.PlayerId == proj.OwnerId);
            target.Health = 0;
            target.DeathTime = DateTime.UtcNow;
            target.ForceNextUpdate();

            // Drop soul if carrying one (matches CombatSystem.ProcessPlayerDeath)
            if (target.CarryingSoulOfPlayerId != null)
            {
                _logger.LogInformation("Player {PlayerName} died (projectile) while carrying soul of {SoulId}",
                    target.PlayerName, target.CarryingSoulOfPlayerId);
                target.CarryingSoulOfPlayerId = null;
            }

            OnPlayerDeath?.Invoke(target, owner);
        }
    }

    private void HitMob(Projectile proj, Mob mob, GameWorld world)
    {
        proj.IsActive = false;
        var damage = ApplyMobDefenses((int)proj.Damage, mob);
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

        // AOE on impact — exclude direct target mob from AOE
        if (proj.AreaRadius > 0)
            ApplyAoeDamage(proj, world, excludeMobId: mob.MobId);
    }

    private void ApplyAoeDamage(Projectile proj, GameWorld world, string? excludePlayerId = null, string? excludeMobId = null)
    {
        var aoeDamage = (int)(proj.Damage * 0.6f); // AOE does 60% of direct damage

        // Hit nearby players
        foreach (var player in world.Players.Values)
        {
            if (player.PlayerId == excludePlayerId) continue;
            if (!proj.IsMobProjectile && player.PlayerId == proj.OwnerId) continue;
            if (!player.IsAlive) continue;
            if (player.CurrentRoomId != proj.RoomId) continue;
            if (!proj.IsMobProjectile && player.TeamId == proj.OwnerTeamId && !string.IsNullOrEmpty(proj.OwnerTeamId)) continue;

            var dist = GameMathUtils.Distance(proj.Position, player.Position);
            if (dist > proj.AreaRadius) continue;

            // Apply target's damage reduction to AOE (capped at 70%)
            var playerAoeDmg = Math.Max(1, (int)(aoeDamage * (1f - Math.Clamp(player.DamageReduction, 0f, 0.70f))));

            if (player.Shield > 0)
            {
                var absorbed = Math.Min(player.Shield, playerAoeDmg);
                player.Shield -= absorbed;
                var remainingDmg = playerAoeDmg - absorbed;
                player.Health = Math.Max(0, player.Health - remainingDmg);

                if (player.Shield <= 0)
                {
                    lock (player.StatusEffectsLock)
                    {
                        player.StatusEffects.RemoveAll(e => e.EffectType == "shield");
                    }
                    player.DamageReduction = player.EquipmentDamageReduction;
                }
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

            // Apply status effect to AOE targets (e.g., frozen_orb slow)
            if (!string.IsNullOrEmpty(proj.AppliesEffect) && proj.EffectDurationMs > 0)
            {
                ApplyStatusEffect(player, new StatusEffect
                {
                    EffectType = proj.AppliesEffect,
                    Value = proj.EffectValue,
                    ExpiresAt = DateTime.UtcNow.AddMilliseconds(proj.EffectDurationMs),
                    SourcePlayerId = proj.EffectSourcePlayerId
                });
            }

            if (player.Health <= 0 && player.TryKill())
            {
                var owner = world.Players.Values.FirstOrDefault(p => p.PlayerId == proj.OwnerId);
                player.Health = 0;
                player.DeathTime = DateTime.UtcNow;
                player.ForceNextUpdate();

                if (player.CarryingSoulOfPlayerId != null)
                {
                    _logger.LogInformation("Player {PlayerName} died (AOE projectile) while carrying soul of {SoulId}",
                        player.PlayerName, player.CarryingSoulOfPlayerId);
                    player.CarryingSoulOfPlayerId = null;
                }

                OnPlayerDeath?.Invoke(player, owner);
            }
        }

        // Hit nearby mobs (only player projectiles damage mobs)
        if (!proj.IsMobProjectile)
        {
            foreach (var mob in world.Mobs.Values)
            {
                if (mob.MobId == excludeMobId) continue;
                if (mob.Health <= 0) continue;
                if (mob.RoomId != proj.RoomId) continue;

                var dist = GameMathUtils.Distance(proj.Position, mob.Position);
                if (dist > proj.AreaRadius) continue;

                var mobAoeDmg = ApplyMobDefenses(aoeDamage, mob);
                mob.Health = Math.Max(0, mob.Health - mobAoeDmg);
                mob.IsDirty = true;
                if (mob is EnhancedMob em2) em2.RequiresUpdate = true;

                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "ability_damage",
                    SourceId = proj.OwnerId,
                    TargetId = mob.MobId,
                    Value = mobAoeDmg,
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

    /// <summary>
    /// Applies mob armor/magic resistance to reduce incoming damage.
    /// Uses the higher of armor and magic resistance for simplicity.
    /// </summary>
    private static int ApplyMobDefenses(int damage, Mob mob)
    {
        if (mob is EnhancedMob em)
        {
            var armor = em.EnhancedStats.Armor;
            var mr = em.EnhancedStats.MagicResistance;
            var effectiveDefense = Math.Max(armor, mr);
            if (effectiveDefense > 0)
            {
                var dr = GameMathUtils.CalculateDamageReduction(effectiveDefense);
                damage = (int)(damage * (1f - dr));
            }
        }
        return Math.Max(1, damage);
    }

    private static readonly HashSet<string> StackableEffects = new(StringComparer.OrdinalIgnoreCase)
        { "bleed", "poison" };
    private static readonly HashSet<string> CcEffects = new(StringComparer.OrdinalIgnoreCase)
        { "stun", "slow" };
    private const int MaxEffectStacks = 5;
    private const float CcDiminishWindowSeconds = 10f;

    private void ApplyStatusEffect(RealTimePlayer player, StatusEffect effect)
    {
        // Diminishing returns for CC effects (stun, slow) in PvP
        if (CcEffects.Contains(effect.EffectType))
        {
            var now = DateTime.UtcNow;
            var timeSinceLastCc = (now - player.LastCcTime).TotalSeconds;
            if (timeSinceLastCc > CcDiminishWindowSeconds)
                player.RecentCcCount = 0;

            player.RecentCcCount++;
            player.LastCcTime = now;

            var durationMultiplier = player.RecentCcCount switch
            {
                1 => 1.0f,
                2 => 0.75f,
                3 => 0.50f,
                _ => 0f
            };

            if (durationMultiplier <= 0f) return; // Immune

            if (durationMultiplier < 1f)
            {
                var originalDuration = effect.ExpiresAt - now;
                effect.ExpiresAt = now + originalDuration * durationMultiplier;
            }
        }

        lock (player.StatusEffectsLock)
        {
            if (StackableEffects.Contains(effect.EffectType))
            {
                var existing = player.StatusEffects.Where(e => e.EffectType == effect.EffectType).ToList();
                if (existing.Count >= MaxEffectStacks)
                {
                    var oldest = existing.OrderBy(e => e.AppliedAt).First();
                    player.StatusEffects.Remove(oldest);
                }
            }
            else
            {
                player.StatusEffects.RemoveAll(e => e.EffectType == effect.EffectType);
            }
            player.StatusEffects.Add(effect);
        }
    }

    public void ClearWorldProjectiles(string worldId)
    {
        lock (_lock)
        {
            _activeProjectiles.RemoveAll(p => p.WorldId == worldId);
        }
    }
}
