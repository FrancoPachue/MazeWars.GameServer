using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MazeWars.GameServer.Services.Combat;

public class CombatSystem : ICombatSystem
{
    private readonly ILogger<CombatSystem> _logger;
    private readonly GameServerSettings _settings;
    private readonly Random _random;

    public event Action<string, CombatEvent>? OnCombatEvent;
    public event Action<RealTimePlayer, RealTimePlayer?>? OnPlayerDeath;

    public CombatSystem(ILogger<CombatSystem> logger, IOptions<GameServerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _random = new Random();
    }

    public bool CanAttack(RealTimePlayer player)
    {
        if (!player.IsAlive || player.IsCasting) return false;
        return (DateTime.UtcNow - player.LastAttackTime).TotalMilliseconds >= _settings.GameBalance.AttackCooldownMs;
    }

    public bool CanUseAbility(RealTimePlayer player, string abilityType)
    {
        if (!player.IsAlive || player.IsCasting) return false;

        if (player.AbilityCooldowns.TryGetValue(abilityType, out var lastUsed))
        {
            var cooldownDuration = GetAbilityCooldown(abilityType);
            if (DateTime.UtcNow - lastUsed < cooldownDuration)
                return false;
        }

        var manaCost = GetAbilityManaCost(abilityType);
        return player.Mana >= manaCost;
    }

    public async Task<CombatResult> ProcessAttack(RealTimePlayer attacker, List<RealTimePlayer> potentialTargets, GameWorld world)
    {
        var result = new CombatResult { Success = false, Events = new List<CombatEvent>() };

        if (!CanAttack(attacker))
        {
            result.ErrorMessage = "Cannot attack at this time";
            return result;
        }

        var targets = FindTargetsInRange(attacker, potentialTargets);
        if (!targets.Any())
        {
            result.ErrorMessage = "No targets in range";
            return result;
        }

        foreach (var target in targets)
        {
            var damage = CalculateDamage(attacker, target);
            ApplyDamage(target, damage);

            var combatEvent = new CombatEvent
            {
                EventType = "damage",
                SourceId = attacker.PlayerId,
                TargetId = target.PlayerId,
                Value = damage,
                Position = target.Position
            };

            result.Events.Add(combatEvent);
            OnCombatEvent?.Invoke(world.WorldId, combatEvent);

            ApplyAttackEffects(attacker, target);
            target.LastDamageTime = DateTime.UtcNow;

            if (target.Health <= 0 && target.IsAlive)
            {
                ProcessPlayerDeath(target, attacker);
            }
        }

        attacker.LastAttackTime = DateTime.UtcNow;
        result.Success = true;
        result.TargetsHit = targets.Count;

        return result;
    }

    public async Task<AbilityResult> ProcessAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world)
    {
        var result = new AbilityResult { Success = false };

        if (!CanUseAbility(player, abilityType))
        {
            result.ErrorMessage = "Cannot use ability at this time";
            return result;
        }

        var manaCost = GetAbilityManaCost(abilityType);
        player.Mana -= manaCost;

        switch (player.PlayerClass.ToLower())
        {
            case "scout":
                result = await ProcessScoutAbility(player, abilityType, target, world);
                break;
            case "tank":
                result = await ProcessTankAbility(player, abilityType, target, world);
                break;
            case "support":
                result = await ProcessSupportAbility(player, abilityType, target, world);
                break;
            default:
                result.ErrorMessage = $"Unknown player class: {player.PlayerClass}";
                return result;
        }

        if (result.Success)
        {
            player.AbilityCooldowns[abilityType] = DateTime.UtcNow;
        }

        return result;
    }

    public void ApplyStatusEffect(RealTimePlayer player, StatusEffect effect)
    {
        // Remove existing effects of same type
        player.StatusEffects.RemoveAll(e => e.EffectType == effect.EffectType);
        player.StatusEffects.Add(effect);

        // Apply immediate effects
        switch (effect.EffectType.ToLower())
        {
            case "shield":
                player.Shield = Math.Min(player.MaxShield, player.Shield + effect.Value);
                break;
        }

        _logger.LogDebug("Applied {EffectType} to {PlayerName} for {Duration}s",
            effect.EffectType, player.PlayerName, (effect.ExpiresAt - DateTime.UtcNow).TotalSeconds);
    }

    public void UpdateStatusEffects(List<RealTimePlayer> players, float deltaTime)
    {
        foreach (var player in players.Where(p => p.IsAlive))
        {
            var effectsToRemove = new List<StatusEffect>();

            foreach (var effect in player.StatusEffects)
            {
                if (DateTime.UtcNow >= effect.ExpiresAt)
                {
                    RemoveStatusEffect(player, effect);
                    effectsToRemove.Add(effect);
                }
                else
                {
                    ProcessStatusEffect(player, effect, deltaTime);
                }
            }

            foreach (var effect in effectsToRemove)
            {
                player.StatusEffects.Remove(effect);
            }
        }
    }

    #region Private Methods

    private List<RealTimePlayer> FindTargetsInRange(RealTimePlayer attacker, List<RealTimePlayer> potentialTargets)
    {
        var targets = new List<RealTimePlayer>();
        var attackRange = _settings.GameBalance.AttackRange;

        foreach (var player in potentialTargets)
        {
            if (player.PlayerId == attacker.PlayerId || !player.IsAlive) continue;
            if (player.CurrentRoomId != attacker.CurrentRoomId) continue;

            var distance = GameMathUtils.Distance(player.Position, attacker.Position);
            if (distance <= attackRange)
            {
                var directionToTarget = (player.Position - attacker.Position).GetNormalized();
                var attackDirection = new Vector2(
                    (float)Math.Cos(attacker.Direction),
                    (float)Math.Sin(attacker.Direction)
                );

                var dotProduct = directionToTarget.X * attackDirection.X + directionToTarget.Y * attackDirection.Y;
                if (dotProduct > 0.5f) // ~60 degree cone
                {
                    targets.Add(player);
                }
            }
        }

        return targets;
    }

    private int CalculateDamage(RealTimePlayer attacker, RealTimePlayer target)
    {
        var baseDamage = attacker.PlayerClass switch
        {
            "tank" => 35,
            "scout" => 25,
            "support" => 20,
            _ => 25
        };

        // Add strength bonus
        if (attacker.Stats.TryGetValue("strength", out var strength))
        {
            baseDamage += strength * 2;
        }

        // Add weapon damage
        baseDamage += GetWeaponDamage(attacker);

        // Apply variance
        var variance = baseDamage * 0.2f;
        baseDamage += (int)(_random.NextDouble() * variance * 2 - variance);

        // Apply damage modifiers
        var finalDamage = (float)baseDamage;
        finalDamage *= (1f - target.DamageReduction);

        // Critical hit chance for scouts
        if (attacker.PlayerClass == "scout" && _random.NextDouble() < 0.15)
        {
            finalDamage *= 1.5f;
            // TODO: Emit critical hit event
        }

        return Math.Max(1, (int)finalDamage);
    }

    private void ApplyDamage(RealTimePlayer target, int damage)
    {
        var remainingDamage = damage;

        // Shield absorbs damage first
        if (target.Shield > 0)
        {
            var shieldDamage = Math.Min(target.Shield, remainingDamage);
            target.Shield -= shieldDamage;
            remainingDamage -= shieldDamage;
        }

        // Apply remaining damage to health
        if (remainingDamage > 0)
        {
            target.Health = Math.Max(0, target.Health - remainingDamage);
        }
    }

    private void ApplyAttackEffects(RealTimePlayer attacker, RealTimePlayer target)
    {
        switch (attacker.PlayerClass.ToLower())
        {
            case "tank":
                if (_random.NextDouble() < 0.3) // 30% chance to slow
                {
                    ApplyStatusEffect(target, new StatusEffect
                    {
                        EffectType = "slow",
                        Value = -50,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(3),
                        SourcePlayerId = attacker.PlayerId
                    });
                }
                break;

            case "scout":
                if (_random.NextDouble() < 0.2) // 20% chance to poison
                {
                    ApplyStatusEffect(target, new StatusEffect
                    {
                        EffectType = "poison",
                        Value = 5,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(5),
                        SourcePlayerId = attacker.PlayerId
                    });
                }
                break;
        }
    }

    private int GetWeaponDamage(RealTimePlayer player)
    {
        var weaponDamage = 0;

        foreach (var item in player.Inventory.Where(i => i.ItemType == "weapon"))
        {
            weaponDamage += item.Rarity * 5;

            if (item.Properties.TryGetValue("damage", out var damage))
            {
                weaponDamage += Convert.ToInt32(damage);
            }
        }

        return weaponDamage;
    }

    private void ProcessPlayerDeath(RealTimePlayer deadPlayer, RealTimePlayer? killer)
    {
        deadPlayer.IsAlive = false;
        deadPlayer.Health = 0;
        deadPlayer.DeathTime = DateTime.UtcNow;

        var killerName = killer?.PlayerName ?? "Environment";
        _logger.LogInformation("Player {DeadPlayer} was killed by {Killer}",
            deadPlayer.PlayerName, killerName);

        OnPlayerDeath?.Invoke(deadPlayer, killer);
    }

    private void ProcessStatusEffect(RealTimePlayer player, StatusEffect effect, float deltaTime)
    {
        switch (effect.EffectType.ToLower())
        {
            case "poison":
                var timeSinceStart = DateTime.UtcNow - (effect.ExpiresAt.AddSeconds(-5));
                if (timeSinceStart.TotalSeconds % 1 < deltaTime)
                {
                    player.Health = Math.Max(0, player.Health - effect.Value);
                    player.LastDamageTime = DateTime.UtcNow;

                    if (player.Health <= 0 && player.IsAlive)
                    {
                        ProcessPlayerDeath(player, null);
                    }
                }
                break;

            case "regen":
                var regenTime = DateTime.UtcNow - (effect.ExpiresAt.AddSeconds(-10));
                if (regenTime.TotalSeconds % 1 < deltaTime)
                {
                    player.Health = Math.Min(player.MaxHealth, player.Health + effect.Value);
                }
                break;

            case "slow":
                player.MovementSpeedModifier = 0.5f;
                break;

            case "speed":
                player.MovementSpeedModifier = 1.5f;
                break;
        }
    }

    private void RemoveStatusEffect(RealTimePlayer player, StatusEffect effect)
    {
        switch (effect.EffectType.ToLower())
        {
            case "slow":
            case "speed":
                player.MovementSpeedModifier = 1f;
                break;
        }
    }

    private async Task<AbilityResult> ProcessScoutAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world)
    {
        var result = new AbilityResult { Success = true };

        switch (abilityType.ToLower())
        {
            case "dash":
                var direction = (target - player.Position).GetNormalized();
                var newPosition = player.Position + direction * 10f;
                // Validate position with world bounds
                player.Position = newPosition;
                result.Message = "Dashed forward";
                break;

            case "stealth":
                ApplyStatusEffect(player, new StatusEffect
                {
                    EffectType = "stealth",
                    Value = 0,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(5),
                    SourcePlayerId = player.PlayerId
                });
                result.Message = "Entered stealth";
                break;

            default:
                result.Success = false;
                result.ErrorMessage = $"Unknown scout ability: {abilityType}";
                break;
        }

        return result;
    }

    private async Task<AbilityResult> ProcessTankAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world)
    {
        var result = new AbilityResult { Success = true };

        switch (abilityType.ToLower())
        {
            case "charge":
                var direction = (target - player.Position).GetNormalized();
                var newPosition = player.Position + direction * 8f;
                player.Position = newPosition;

                // Damage nearby enemies
                var nearbyEnemies = world.Players.Values
                    .Where(p => p.IsAlive && p.TeamId != player.TeamId)
                    .Where(p => GameMathUtils.Distance(p.Position, player.Position) <= 3f)
                    .ToList();

                foreach (var enemy in nearbyEnemies)
                {
                    ApplyDamage(enemy, 30);
                }

                result.Message = $"Charged and hit {nearbyEnemies.Count} enemies";
                break;

            case "shield":
                ApplyStatusEffect(player, new StatusEffect
                {
                    EffectType = "shield",
                    Value = 50,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(10),
                    SourcePlayerId = player.PlayerId
                });
                player.DamageReduction = 0.5f;
                result.Message = "Shield activated";
                break;

            default:
                result.Success = false;
                result.ErrorMessage = $"Unknown tank ability: {abilityType}";
                break;
        }

        return result;
    }

    private async Task<AbilityResult> ProcessSupportAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world)
    {
        var result = new AbilityResult { Success = true };

        switch (abilityType.ToLower())
        {
            case "heal":
                var nearbyTeammates = world.Players.Values
                    .Where(p => p.TeamId == player.TeamId && p.IsAlive)
                    .Where(p => GameMathUtils.Distance(p.Position, player.Position) <= 8f)
                    .ToList();

                foreach (var teammate in nearbyTeammates)
                {
                    var healAmount = 40;
                    teammate.Health = Math.Min(teammate.MaxHealth, teammate.Health + healAmount);
                }

                result.Message = $"Healed {nearbyTeammates.Count} teammates";
                break;

            case "buff":
                var buffTargets = world.Players.Values
                    .Where(p => p.TeamId == player.TeamId && p.IsAlive)
                    .Where(p => GameMathUtils.Distance(p.Position, player.Position) <= 6f)
                    .ToList();

                foreach (var buffTarget in buffTargets)
                {
                    ApplyStatusEffect(buffTarget, new StatusEffect
                    {
                        EffectType = "speed",
                        Value = 0,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(8),
                        SourcePlayerId = player.PlayerId
                    });
                }

                result.Message = $"Buffed {buffTargets.Count} teammates";
                break;

            default:
                result.Success = false;
                result.ErrorMessage = $"Unknown support ability: {abilityType}";
                break;
        }

        return result;
    }

    private TimeSpan GetAbilityCooldown(string abilityType)
    {
        return abilityType.ToLower() switch
        {
            "dash" => TimeSpan.FromSeconds(5),
            "stealth" => TimeSpan.FromSeconds(12),
            "charge" => TimeSpan.FromSeconds(8),
            "shield" => TimeSpan.FromSeconds(15),
            "heal" => TimeSpan.FromSeconds(6),
            "buff" => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(5)
        };
    }

    private int GetAbilityManaCost(string abilityType)
    {
        return abilityType.ToLower() switch
        {
            "dash" => 20,
            "stealth" => 30,
            "charge" => 25,
            "shield" => 40,
            "heal" => 35,
            "buff" => 30,
            _ => 20
        };
    }

    #endregion
}