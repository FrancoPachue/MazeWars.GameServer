using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Engine.Equipment.Data;
using MazeWars.GameServer.Engine.Equipment.Interface;
using MazeWars.GameServer.Engine.Equipment.Models;
using MazeWars.GameServer.Engine.MobIASystem.Models;
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
    private readonly IEquipmentSystem _equipmentSystem;
    private readonly Random _random;

    public ProjectileSystem ProjectileSystem { get; }

    public event Action<string, CombatEvent>? OnCombatEvent;
    public event Action<RealTimePlayer, RealTimePlayer?>? OnPlayerDeath;
    public Func<string, float>? RttLookup { get; set; }

    public CombatSystem(ILogger<CombatSystem> logger, IOptions<GameServerSettings> settings,
        IEquipmentSystem equipmentSystem, ProjectileSystem projectileSystem)
    {
        _logger = logger;
        _settings = settings.Value;
        _equipmentSystem = equipmentSystem;
        _random = new Random();
        ProjectileSystem = projectileSystem;
    }

    public bool CanAttack(RealTimePlayer player)
    {
        if (!player.IsAlive || player.IsCasting) return false;

        // Resolve per-weapon attack speed
        var attackCooldownMs = _settings.GameBalance.AttackCooldownMs;
        var basicAbility = _equipmentSystem.GetAvailableAbilities(player)
            .FirstOrDefault(a => a.SlotIndex == 0 && a.AttackSpeedMs > 0);
        if (basicAbility != null)
            attackCooldownMs = basicAbility.AttackSpeedMs;

        // Apply class modifier
        var globalMod = ClassModifierRegistry.GetGlobal(player.PlayerClass);
        if (globalMod != null)
            attackCooldownMs = (int)(attackCooldownMs * globalMod.AttackSpeedMultiplier);

        // Apply equipment attack speed bonus
        attackCooldownMs = (int)(attackCooldownMs * (1f - player.AttackSpeedBonus));

        return (DateTime.UtcNow - player.LastAttackTime).TotalMilliseconds >= attackCooldownMs;
    }

    /// <summary>Get the effective attack cooldown for a player (for sending to client).</summary>
    public int GetEffectiveAttackSpeedMs(RealTimePlayer player)
    {
        var attackCooldownMs = _settings.GameBalance.AttackCooldownMs;
        var basicAbility = _equipmentSystem.GetAvailableAbilities(player)
            .FirstOrDefault(a => a.SlotIndex == 0 && a.AttackSpeedMs > 0);
        if (basicAbility != null)
            attackCooldownMs = basicAbility.AttackSpeedMs;

        var globalMod = ClassModifierRegistry.GetGlobal(player.PlayerClass);
        if (globalMod != null)
            attackCooldownMs = (int)(attackCooldownMs * globalMod.AttackSpeedMultiplier);

        // Apply equipment attack speed bonus
        attackCooldownMs = (int)(attackCooldownMs * (1f - player.AttackSpeedBonus));

        return attackCooldownMs;
    }

    /// <summary>Get the effective auto-attack range for a player (slot-0 ability range, or global fallback).</summary>
    public float GetEffectiveAttackRange(RealTimePlayer player)
    {
        var basicAbility = _equipmentSystem.GetAvailableAbilities(player)
            .FirstOrDefault(a => a.SlotIndex == 0);
        return basicAbility?.Range ?? _settings.GameBalance.AttackRange;
    }

    public bool CanUseAbility(RealTimePlayer player, string abilityType)
    {
        if (!player.IsAlive || player.IsCasting) return false;

        // Resolve ability through equipment system
        var resolved = _equipmentSystem.ResolveAbilityWithModifiers(player, abilityType);
        if (resolved == null) return false;

        var (ability, modifier) = resolved.Value;

        // Check cooldown (with CDR from equipment)
        if (player.AbilityCooldowns.TryGetValue(abilityType, out var lastUsed))
        {
            var cooldownMs = (int)(ability.CooldownMs * (modifier?.CooldownMultiplier ?? 1f) * (1f - player.CooldownReduction));
            if (DateTime.UtcNow - lastUsed < TimeSpan.FromMilliseconds(cooldownMs))
                return false;
        }

        // Check mana
        var manaCost = (int)(ability.ManaCost * (modifier?.ManaCostMultiplier ?? 1f));
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

        // Resolve weapon's basic ability (slot 0) for range and projectile info
        var basicAbility = _equipmentSystem.GetAvailableAbilities(attacker)
            .FirstOrDefault(a => a.SlotIndex == 0);
        var attackRange = basicAbility?.Range ?? _settings.GameBalance.AttackRange;

        // Ranged weapon with projectile: fire projectile in aim direction
        // The projectile handles both player and mob collision automatically
        if (basicAbility != null && basicAbility.ProjectileSpeed > 0)
        {
            var direction = new Vector2(
                (float)Math.Cos(attacker.Direction),
                (float)Math.Sin(attacker.Direction)
            );
            var (damage, _) = CalculateBasicAttackDamageVsMob(attacker); // pre-calculated, crit handled on hit

            ProjectileSystem.SpawnProjectile(
                attacker, basicAbility.AbilityId,
                direction, basicAbility.ProjectileSpeed, basicAbility.ProjectileRadius,
                attackRange, damage, 0, world);

            attacker.LastAttackTime = DateTime.UtcNow;
            result.Success = true;
            result.IsRangedProjectile = true;
            return result;
        }

        // Melee weapon: instant cone hit
        var targets = FindTargetsInRange(attacker, potentialTargets, attackRange);
        if (!targets.Any())
        {
            result.ErrorMessage = "No targets in range";
            return result;
        }

        foreach (var target in targets)
        {
            var (damage, isCrit) = CalculateBasicAttackDamage(attacker, target);
            ApplyDamage(target, damage);

            var combatEvent = new CombatEvent
            {
                EventType = isCrit ? "damage_crit" : "damage",
                SourceId = attacker.PlayerId,
                TargetId = target.PlayerId,
                Value = damage,
                Position = target.Position,
                RoomId = attacker.CurrentRoomId
            };

            result.Events.Add(combatEvent);
            OnCombatEvent?.Invoke(world.WorldId, combatEvent);

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

    /// <summary>
    /// Process basic attack against mobs. Does NOT check CanAttack or set LastAttackTime
    /// (caller ProcessAttack already did that for the same attack frame).
    /// </summary>
    public CombatResult ProcessAttackAgainstMobs(RealTimePlayer attacker, List<Mob> potentialMobs, GameWorld world)
    {
        var result = new CombatResult { Success = false, Events = new List<CombatEvent>() };

        // Use weapon's range for melee mob targeting
        var basicAbility = _equipmentSystem.GetAvailableAbilities(attacker)
            .FirstOrDefault(a => a.SlotIndex == 0);
        var attackRange = basicAbility?.Range ?? _settings.GameBalance.AttackRange;

        var targets = FindMobTargetsInRange(attacker, potentialMobs, attackRange);
        if (!targets.Any())
            return result;

        foreach (var mob in targets)
        {
            var (damage, isCrit) = CalculateBasicAttackDamageVsMob(attacker);
            mob.Health = Math.Max(0, mob.Health - damage);
            mob.IsDirty = true;
            if (mob is EnhancedMob em1) em1.RequiresUpdate = true;

            var combatEvent = new CombatEvent
            {
                EventType = isCrit ? "damage_crit" : "damage",
                SourceId = attacker.PlayerId,
                TargetId = mob.MobId,
                Value = damage,
                Position = mob.Position,
                RoomId = attacker.CurrentRoomId
            };

            result.Events.Add(combatEvent);
            OnCombatEvent?.Invoke(world.WorldId, combatEvent);
        }

        result.Success = true;
        result.TargetsHit = targets.Count;
        return result;
    }

    public async Task<AbilityResult> ProcessAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world)
    {
        // Universal mechanic: revive teammate (not equipment-based)
        if (abilityType == "revive")
            return ProcessRevive(player, target, world);

        var result = new AbilityResult { Success = false };

        // Resolve ability + class modifiers via equipment system
        var resolved = _equipmentSystem.ResolveAbilityWithModifiers(player, abilityType);
        if (resolved == null)
        {
            result.ErrorMessage = "Ability not available";
            return result;
        }

        var (ability, modifier) = resolved.Value;

        // Calculate final parameters
        var finalManaCost = (int)(ability.ManaCost * (modifier?.ManaCostMultiplier ?? 1f));
        var finalCooldownMs = (int)(ability.CooldownMs * (modifier?.CooldownMultiplier ?? 1f) * (1f - player.CooldownReduction));
        var finalRange = ability.Range * (modifier?.RangeMultiplier ?? 1f);
        var finalDamage = ability.BaseDamage * (modifier?.DamageMultiplier ?? 1f);
        var finalHealing = ability.BaseHealing * (modifier?.HealingMultiplier ?? 1f);
        var finalDuration = (int)(ability.DurationMs * (modifier?.DurationMultiplier ?? 1f));
        var finalCastTime = (int)(ability.CastTimeMs * (modifier?.CastTimeMultiplier ?? 1f));

        // Validate cooldown
        if (player.AbilityCooldowns.TryGetValue(abilityType, out var lastUsed))
        {
            if (DateTime.UtcNow - lastUsed < TimeSpan.FromMilliseconds(finalCooldownMs))
            {
                result.ErrorMessage = "Ability on cooldown";
                return result;
            }
        }

        // Validate mana
        if (player.Mana < finalManaCost)
        {
            result.ErrorMessage = "Not enough mana";
            return result;
        }

        // Consume mana
        player.Mana -= finalManaCost;

        // Handle cast time — stop movement during cast
        if (finalCastTime > 0)
        {
            player.IsCasting = true;
            player.CastingUntil = DateTime.UtcNow.AddMilliseconds(finalCastTime);
        }

        // Stop movement when casting any ability
        player.Velocity = Vector2.Zero;
        player.IsMoving = false;
        player.MoveTarget = null;
        player.IsPathing = false;
        player.IsSprinting = false;

        // Execute by AbilityType
        result = ability.Type switch
        {
            AbilityType.MeleeDamage => ExecuteMeleeDamage(player, finalDamage, finalRange, target, world),
            AbilityType.ProjectileDamage => ExecuteProjectileDamage(player, ability, finalDamage, finalRange, target, world),
            AbilityType.AreaDamage => ExecuteAreaDamage(player, finalDamage, finalRange, ability.AreaRadius, target, world, ability.AbilityId),
            AbilityType.Dash => ExecuteDash(player, ability.DashDistance, ability.DashSpeed, target),
            AbilityType.Shield => ExecuteShield(player, (int)finalHealing, finalDuration),
            AbilityType.Heal => ExecuteHeal(player, finalHealing, finalRange, target, world, ability.AbilityId),
            AbilityType.Buff => ExecuteBuff(player, ability, modifier, finalDuration, world),
            AbilityType.StatusApply => ExecuteStatusApply(player, ability, finalRange, target, world),
            AbilityType.Stealth => ExecuteStealth(player, finalDuration),
            AbilityType.Taunt => ExecuteTaunt(player, finalRange, world),
            _ => new AbilityResult { Success = false, ErrorMessage = "Unknown ability type" }
        };

        // Emit VFX combat events for self-cast abilities that don't emit their own
        if (result.Success)
        {
            CombatEvent? vfxEvent = ability.Type switch
            {
                AbilityType.Stealth => new CombatEvent
                {
                    EventType = "stealth_applied", SourceId = player.PlayerId,
                    Position = player.Position, RoomId = player.CurrentRoomId,
                    AbilityId = ability.AbilityId
                },
                AbilityType.Dash => new CombatEvent
                {
                    EventType = ability.DashSpeed >= 50 ? "blink" : "dash",
                    SourceId = player.PlayerId, Position = player.Position,
                    RoomId = player.CurrentRoomId, AbilityId = ability.AbilityId,
                    Direction = player.Direction, Speed = ability.DashSpeed
                },
                AbilityType.Shield => new CombatEvent
                {
                    EventType = "shield_applied", SourceId = player.PlayerId,
                    Value = (int)finalHealing, Position = player.Position,
                    RoomId = player.CurrentRoomId, AbilityId = ability.AbilityId
                },
                AbilityType.Buff => new CombatEvent
                {
                    EventType = "buff_applied", SourceId = player.PlayerId,
                    Value = ability.EffectValue, Position = player.Position,
                    RoomId = player.CurrentRoomId, AbilityId = ability.AbilityId
                },
                _ => null
            };

            if (vfxEvent != null)
                OnCombatEvent?.Invoke(world.WorldId, vfxEvent);
        }

        // Apply class extra effect on success
        if (result.Success && modifier != null && !string.IsNullOrEmpty(modifier.ExtraEffect))
        {
            if (_random.NextSingle() <= modifier.ExtraEffectChance)
            {
                var damageDealt = result.Events.Where(e => e.Value > 0).Sum(e => e.Value);
                ApplyClassExtraEffect(player, modifier, target, world, damageDealt);
            }
        }

        // Set cooldown or refund mana
        if (result.Success)
        {
            player.AbilityCooldowns[abilityType] = DateTime.UtcNow;
        }
        else
        {
            player.Mana = Math.Min(player.MaxMana, player.Mana + finalManaCost);
            if (finalCastTime > 0)
            {
                player.IsCasting = false;
            }
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

    public void UpdateStatusEffects(IEnumerable<RealTimePlayer> players, float deltaTime)
    {
        foreach (var player in players)
        {
            if (!player.IsAlive) continue;

            // Check cast completion
            if (player.IsCasting && DateTime.UtcNow >= player.CastingUntil)
            {
                player.IsCasting = false;
            }

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

    #region Revival

    private const float ReviveRange = 2.5f;
    private const int ReviveManaCost = 40;
    private const int ReviveCooldownMs = 30000;
    private const float ReviveHealthPercent = 0.3f;

    private AbilityResult ProcessRevive(RealTimePlayer player, Vector2 target, GameWorld world)
    {
        // Only support class can directly revive — other classes must use Revival Altars
        if (player.PlayerClass != "support")
            return new AbilityResult { Success = false, ErrorMessage = "Only support class can directly revive. Use a Revival Altar instead." };

        if (!player.IsAlive)
            return new AbilityResult { Success = false, ErrorMessage = "Cannot revive while dead" };

        if (player.IsCasting || player.ChannelingAbility != null)
            return new AbilityResult { Success = false, ErrorMessage = "Already casting" };

        // Check cooldown
        if (player.AbilityCooldowns.TryGetValue("revive", out var lastRevive))
        {
            if (DateTime.UtcNow - lastRevive < TimeSpan.FromMilliseconds(ReviveCooldownMs))
                return new AbilityResult { Success = false, ErrorMessage = "Revive on cooldown" };
        }

        if (player.Mana < ReviveManaCost)
            return new AbilityResult { Success = false, ErrorMessage = "Not enough mana" };

        // Find dead teammate near target position
        var deadAlly = world.Players.Values
            .Where(p => !p.IsAlive && p.TeamId == player.TeamId && p.PlayerId != player.PlayerId)
            .Where(p => GameMathUtils.Distance(p.Position, player.Position) <= ReviveRange)
            .OrderBy(p => GameMathUtils.Distance(p.Position, target))
            .FirstOrDefault();

        if (deadAlly == null)
            return new AbilityResult { Success = false, ErrorMessage = "No dead teammate nearby" };

        // Consume mana
        player.Mana -= ReviveManaCost;

        // Start channeling instead of instant revive
        var channelDuration = _settings?.GameBalance?.RevivalChannelSeconds ?? 3.0f;
        player.ChannelingAbility = "revive";
        player.ChannelingTargetId = deadAlly.PlayerId;
        player.ChannelingStartTime = DateTime.UtcNow;
        player.ChannelingDuration = channelDuration;
        player.IsMoving = false;
        player.MoveTarget = null;

        // Set cooldown
        player.AbilityCooldowns["revive"] = DateTime.UtcNow;

        _logger.LogInformation("Player {Reviver} started revive channel on {Target} ({Duration}s)",
            player.PlayerName, deadAlly.PlayerName, channelDuration);

        var channelingEvent = new CombatEvent
        {
            EventType = "channeling_started",
            SourceId = player.PlayerId,
            TargetId = deadAlly.PlayerId,
            Value = (int)(channelDuration * 10),
            AbilityId = "Reviving",
            Position = player.Position,
            RoomId = player.CurrentRoomId
        };
        OnCombatEvent?.Invoke(world.WorldId, channelingEvent);

        return new AbilityResult
        {
            Success = true,
            Message = $"Channeling revive on {deadAlly.PlayerName}...",
            Events = new List<CombatEvent> { channelingEvent }
        };
    }

    public void UpdateChanneling(GameWorld world, float deltaTime)
    {
        foreach (var player in world.Players.Values)
        {
            if (player.ChannelingAbility == null) continue;
            if (!player.IsAlive)
            {
                CancelChanneling(player, world, "died");
                continue;
            }

            var elapsed = (DateTime.UtcNow - player.ChannelingStartTime).TotalSeconds;

            // Cancel if player moved
            if (player.IsMoving || player.MoveTarget != null)
            {
                CancelChanneling(player, world, "moved");
                continue;
            }

            // Cancel if player took damage during channel
            if (player.LastDamageTime > player.ChannelingStartTime)
            {
                CancelChanneling(player, world, "damage");
                continue;
            }

            // Check if target is still valid (for revive: still dead and in range)
            if (player.ChannelingAbility == "revive" && player.ChannelingTargetId != null)
            {
                if (!world.Players.TryGetValue(player.ChannelingTargetId, out var target) ||
                    target.IsAlive ||
                    GameMathUtils.Distance(player.Position, target.Position) > ReviveRange + 1.0f)
                {
                    CancelChanneling(player, world, "invalid_target");
                    continue;
                }
            }

            // Check if door channeling is still valid (open or close)
            if ((player.ChannelingAbility == "door_open" || player.ChannelingAbility == "door_close")
                && player.ChannelingTargetId != null)
            {
                if (!world.Doors.TryGetValue(player.ChannelingTargetId, out var door) || door.IsLocked)
                {
                    CancelChanneling(player, world, "invalid_target");
                    continue;
                }

                // Validate state hasn't changed during channel
                if (player.ChannelingAbility == "door_open" && door.IsOpen)
                {
                    CancelChanneling(player, world, "already_open");
                    continue;
                }
                if (player.ChannelingAbility == "door_close" && !door.IsOpen)
                {
                    CancelChanneling(player, world, "already_closed");
                    continue;
                }
            }

            // Check if altar revive is still valid
            if (player.ChannelingAbility == "altar_revive" && player.ChannelingTargetId != null)
            {
                // Player must still be carrying a soul
                if (player.CarryingSoulOfPlayerId == null)
                {
                    CancelChanneling(player, world, "no_soul");
                    continue;
                }

                // Altar must still exist and be active
                if (!world.RevivalAltars.TryGetValue(player.ChannelingTargetId, out var altar) ||
                    !altar.IsActive)
                {
                    CancelChanneling(player, world, "invalid_target");
                    continue;
                }

                // Player must still be within 3.0 of altar (slightly more lenient during channel)
                if (GameMathUtils.Distance(player.Position, altar.Position) > 3.0f)
                {
                    CancelChanneling(player, world, "out_of_range");
                    continue;
                }
            }

            if (elapsed >= player.ChannelingDuration)
            {
                // Channel complete
                CompleteChanneling(player, world);
            }
            else
            {
                // Emit progress event
                var progress = (float)(elapsed / player.ChannelingDuration);
                var remaining = (int)(player.ChannelingDuration - elapsed);
                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "channeling_progress",
                    SourceId = player.PlayerId,
                    TargetId = player.ChannelingTargetId ?? "",
                    Value = (int)(progress * 100),
                    Speed = remaining,
                    Position = player.Position,
                    RoomId = player.CurrentRoomId
                });
            }
        }
    }

    private void CompleteChanneling(RealTimePlayer player, GameWorld world)
    {
        if (player.ChannelingAbility == "revive" && player.ChannelingTargetId != null)
        {
            if (world.Players.TryGetValue(player.ChannelingTargetId, out var deadAlly) && !deadAlly.IsAlive)
            {
                deadAlly.IsAlive = true;
                deadAlly.Health = (int)(deadAlly.MaxHealth * ReviveHealthPercent);
                deadAlly.Shield = 0;
                deadAlly.StatusEffects.Clear();
                deadAlly.MovementSpeedModifier = 1f + deadAlly.EquipmentSpeedBonus;
                deadAlly.DamageReduction = deadAlly.EquipmentDamageReduction;
                deadAlly.ForceNextUpdate();

                _logger.LogInformation("Player {Reviver} revived {Revived} ({Health}/{MaxHealth} HP)",
                    player.PlayerName, deadAlly.PlayerName, deadAlly.Health, deadAlly.MaxHealth);

                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "revive",
                    SourceId = player.PlayerId,
                    TargetId = deadAlly.PlayerId,
                    Value = deadAlly.Health,
                    Position = deadAlly.Position,
                    RoomId = player.CurrentRoomId
                });
            }
        }
        else if (player.ChannelingAbility == "altar_revive" && player.CarryingSoulOfPlayerId != null)
        {
            // Find the dead player by CarryingSoulOfPlayerId
            if (world.Players.TryGetValue(player.CarryingSoulOfPlayerId, out var soulAlly) && !soulAlly.IsAlive)
            {
                // Find the altar to get its position for revive location
                var altarPosition = player.Position; // fallback
                if (player.ChannelingTargetId != null &&
                    world.RevivalAltars.TryGetValue(player.ChannelingTargetId, out var altar))
                {
                    altarPosition = altar.Position;
                }

                // Revive at altar position with 30% HP
                soulAlly.IsAlive = true;
                soulAlly.Health = (int)(soulAlly.MaxHealth * 0.3f);
                soulAlly.Shield = 0;
                soulAlly.Position = altarPosition;
                soulAlly.CurrentRoomId = player.CurrentRoomId;
                soulAlly.StatusEffects.Clear();
                soulAlly.MovementSpeedModifier = 1f + soulAlly.EquipmentSpeedBonus;
                soulAlly.DamageReduction = soulAlly.EquipmentDamageReduction;
                soulAlly.ForceNextUpdate();

                // Clear the soul from carrier and restore speed
                var carriedSoulId = player.CarryingSoulOfPlayerId;
                player.CarryingSoulOfPlayerId = null;
                player.MovementSpeedModifier /= 0.8f;

                _logger.LogInformation("Player {Reviver} altar-revived {Revived} at {AltarId} ({Health}/{MaxHealth} HP)",
                    player.PlayerName, soulAlly.PlayerName, player.ChannelingTargetId, soulAlly.Health, soulAlly.MaxHealth);

                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "altar_revive_complete",
                    SourceId = player.PlayerId,
                    TargetId = soulAlly.PlayerId,
                    Value = soulAlly.Health,
                    Position = altarPosition,
                    RoomId = player.CurrentRoomId
                });

                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "revive",
                    SourceId = player.PlayerId,
                    TargetId = soulAlly.PlayerId,
                    Value = soulAlly.Health,
                    Position = altarPosition,
                    RoomId = player.CurrentRoomId
                });
            }
        }
        else if (player.ChannelingAbility == "door_open" && player.ChannelingTargetId != null)
        {
            if (world.Doors.TryGetValue(player.ChannelingTargetId, out var door) && !door.IsOpen && !door.IsLocked)
            {
                door.IsOpen = true;
                door.OpenedAt = DateTime.UtcNow;
                door.ChannelingPlayerId = null;

                var doorPosition = CalculateDoorCenter(world, door);

                _logger.LogInformation("Player {PlayerName} opened door {DoorId}",
                    player.PlayerName, door.DoorId);

                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "door_opened",
                    SourceId = player.PlayerId,
                    TargetId = door.DoorId,
                    Value = 0,
                    Position = doorPosition,
                    RoomId = player.CurrentRoomId
                });
            }
        }
        else if (player.ChannelingAbility == "door_close" && player.ChannelingTargetId != null)
        {
            if (world.Doors.TryGetValue(player.ChannelingTargetId, out var door) && door.IsOpen)
            {
                door.IsOpen = false;
                door.ChannelingPlayerId = null;

                var doorPosition = CalculateDoorCenter(world, door);

                _logger.LogInformation("Player {PlayerName} closed door {DoorId}",
                    player.PlayerName, door.DoorId);

                OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
                {
                    EventType = "door_closed",
                    SourceId = player.PlayerId,
                    TargetId = door.DoorId,
                    Value = 0,
                    Position = doorPosition,
                    RoomId = player.CurrentRoomId
                });
            }
        }

        OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "channeling_complete",
            SourceId = player.PlayerId,
            TargetId = player.ChannelingTargetId ?? "",
            Position = player.Position,
            RoomId = player.CurrentRoomId
        });

        player.ChannelingAbility = null;
        player.ChannelingTargetId = null;
    }

    private void CancelChanneling(RealTimePlayer player, GameWorld world, string reason)
    {
        _logger.LogDebug("Channeling cancelled for {PlayerName}: {Reason}",
            player.PlayerName, reason);

        // Clear door channeling state if this was a door channel
        if ((player.ChannelingAbility == "door_open" || player.ChannelingAbility == "door_close")
            && player.ChannelingTargetId != null)
        {
            if (world.Doors.TryGetValue(player.ChannelingTargetId, out var door))
            {
                door.ChannelingPlayerId = null;
            }
        }

        // NOTE: If altar_revive is cancelled, the player keeps the soul.
        // They can try again at another altar or the same one.

        OnCombatEvent?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "channeling_cancelled",
            SourceId = player.PlayerId,
            TargetId = player.ChannelingTargetId ?? "",
            Position = player.Position,
            RoomId = player.CurrentRoomId
        });

        player.ChannelingAbility = null;
        player.ChannelingTargetId = null;
    }

    /// <summary>
    /// Calculate door position at the CENTER of the corridor (midpoint between both room walls).
    /// </summary>
    private static Vector2 CalculateDoorCenter(GameWorld world, RoomDoor door)
    {
        if (!world.Rooms.TryGetValue(door.RoomIdA, out var roomA) ||
            !world.Rooms.TryGetValue(door.RoomIdB, out var roomB))
            return Vector2.Zero;

        var dx = roomB.Position.X - roomA.Position.X;
        var dy = roomB.Position.Y - roomA.Position.Y;

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            var leftRoom = dx > 0 ? roomA : roomB;
            var rightRoom = dx > 0 ? roomB : roomA;
            var leftWall = leftRoom.Position.X + leftRoom.Size.X / 2f;
            var rightWall = rightRoom.Position.X - rightRoom.Size.X / 2f;
            return new Vector2(
                (leftWall + rightWall) / 2f,
                (leftRoom.Position.Y + rightRoom.Position.Y) / 2f);
        }
        else
        {
            var topRoom = dy > 0 ? roomA : roomB;
            var bottomRoom = dy > 0 ? roomB : roomA;
            var topWall = topRoom.Position.Y + topRoom.Size.Y / 2f;
            var bottomWall = bottomRoom.Position.Y - bottomRoom.Size.Y / 2f;
            return new Vector2(
                (topRoom.Position.X + bottomRoom.Position.X) / 2f,
                (topWall + bottomWall) / 2f);
        }
    }

    #endregion

    #region Ability Execution Methods

    private AbilityResult ExecuteMeleeDamage(RealTimePlayer player, float baseDamage, float range, Vector2 target, GameWorld world)
    {
        var result = new AbilityResult { Success = false, Events = new List<CombatEvent>() };
        var totalHits = 0;

        // Hit players
        var targets = FindTargetsInRange(player, world.Players.Values, range);
        foreach (var t in targets)
        {
            var (damage, isCrit) = CalculateAbilityDamage(player, baseDamage, t);
            ApplyDamage(t, damage);
            t.LastDamageTime = DateTime.UtcNow;

            result.Events.Add(new CombatEvent
            {
                EventType = isCrit ? "ability_damage_crit" : "ability_damage",
                SourceId = player.PlayerId,
                TargetId = t.PlayerId,
                Value = damage,
                Position = t.Position,
                RoomId = player.CurrentRoomId
            });

            OnCombatEvent?.Invoke(world.WorldId, result.Events.Last());

            if (t.Health <= 0 && t.IsAlive)
                ProcessPlayerDeath(t, player);
            totalHits++;
        }

        // Hit mobs
        var mobTargets = FindMobTargetsInRange(player, world.Mobs.Values, range);
        foreach (var mob in mobTargets)
        {
            var (damage, isCrit) = CalculateAbilityDamageVsMob(player, baseDamage);
            mob.Health = Math.Max(0, mob.Health - damage);
            mob.IsDirty = true;
            if (mob is EnhancedMob em2) em2.RequiresUpdate = true;

            var combatEvent = new CombatEvent
            {
                EventType = isCrit ? "ability_damage_crit" : "ability_damage",
                SourceId = player.PlayerId,
                TargetId = mob.MobId,
                Value = damage,
                Position = mob.Position,
                RoomId = player.CurrentRoomId
            };

            result.Events.Add(combatEvent);
            OnCombatEvent?.Invoke(world.WorldId, combatEvent);
            totalHits++;
        }

        if (totalHits == 0)
            return new AbilityResult { Success = false, ErrorMessage = "No targets in range" };

        result.Success = true;
        result.Message = $"Hit {totalHits} target(s)";
        return result;
    }

    private AbilityResult ExecuteProjectileDamage(RealTimePlayer player, AbilityDefinition ability, float baseDamage, float range, Vector2 target, GameWorld world)
    {
        var direction = (target - player.Position).GetNormalized();

        // Real projectile: spawn a traveling projectile instead of instant hit
        if (ability.ProjectileSpeed > 0)
        {
            if (ability.AbilityId == "bow_multishot")
            {
                // 3 arrows with ±15° spread
                ProjectileSystem.SpawnMultipleProjectiles(
                    player, ability.AbilityId,
                    direction, spreadAngleRad: 0.52f, count: 3,
                    ability.ProjectileSpeed, ability.ProjectileRadius,
                    range, baseDamage, ability.AreaRadius, world);
            }
            else
            {
                ProjectileSystem.SpawnProjectile(
                    player, ability.AbilityId,
                    direction, ability.ProjectileSpeed, ability.ProjectileRadius,
                    range, baseDamage, ability.AreaRadius, world);
            }

            return new AbilityResult { Success = true, Message = "Projectile fired" };
        }

        // Legacy instant hit (fallback for abilities with ProjectileSpeed = 0)

        // Check players
        var closestPlayer = world.Players.Values
            .Where(p => p.PlayerId != player.PlayerId && p.IsAlive && p.CurrentRoomId == player.CurrentRoomId)
            .Where(p =>
            {
                var dist = GameMathUtils.Distance(p.Position, player.Position);
                if (dist > range) return false;
                var toEnemy = (p.Position - player.Position).GetNormalized();
                var dot = direction.X * toEnemy.X + direction.Y * toEnemy.Y;
                return dot > 0.7f;
            })
            .OrderBy(p => GameMathUtils.Distance(p.Position, player.Position))
            .FirstOrDefault();

        // Check mobs
        var closestMob = world.Mobs.Values
            .Where(m => m.Health > 0 && m.RoomId == player.CurrentRoomId)
            .Where(m =>
            {
                var dist = GameMathUtils.Distance(m.Position, player.Position);
                if (dist > range) return false;
                var toMob = (m.Position - player.Position).GetNormalized();
                var dot = direction.X * toMob.X + direction.Y * toMob.Y;
                return dot > 0.7f;
            })
            .OrderBy(m => GameMathUtils.Distance(m.Position, player.Position))
            .FirstOrDefault();

        // Pick whichever is closest
        var playerDist = closestPlayer != null ? GameMathUtils.Distance(closestPlayer.Position, player.Position) : float.MaxValue;
        var mobDist = closestMob != null ? GameMathUtils.Distance(closestMob.Position, player.Position) : float.MaxValue;

        if (closestPlayer == null && closestMob == null)
            return new AbilityResult { Success = true, Message = "Projectile missed" };

        var result = new AbilityResult { Success = true, Events = new List<CombatEvent>() };

        if (playerDist <= mobDist && closestPlayer != null)
        {
            // Hit player
            var (damage, isCrit) = CalculateAbilityDamage(player, baseDamage, closestPlayer);
            ApplyDamage(closestPlayer, damage);
            closestPlayer.LastDamageTime = DateTime.UtcNow;

            result.Events.Add(new CombatEvent
            {
                EventType = isCrit ? "ability_damage_crit" : "ability_damage",
                SourceId = player.PlayerId,
                TargetId = closestPlayer.PlayerId,
                Value = damage,
                Position = closestPlayer.Position,
                RoomId = player.CurrentRoomId
            });

            OnCombatEvent?.Invoke(world.WorldId, result.Events.Last());

            if (closestPlayer.Health <= 0 && closestPlayer.IsAlive)
                ProcessPlayerDeath(closestPlayer, player);

            result.Message = $"Hit {closestPlayer.PlayerName}";
        }
        else if (closestMob != null)
        {
            // Hit mob
            var (damage, isCrit) = CalculateAbilityDamageVsMob(player, baseDamage);
            closestMob.Health = Math.Max(0, closestMob.Health - damage);
            closestMob.IsDirty = true;

            var combatEvent = new CombatEvent
            {
                EventType = isCrit ? "ability_damage_crit" : "ability_damage",
                SourceId = player.PlayerId,
                TargetId = closestMob.MobId,
                Value = damage,
                Position = closestMob.Position,
                RoomId = player.CurrentRoomId
            };

            result.Events.Add(combatEvent);
            OnCombatEvent?.Invoke(world.WorldId, combatEvent);

            result.Message = $"Hit mob {closestMob.MobId}";
        }

        return result;
    }

    private AbilityResult ExecuteAreaDamage(RealTimePlayer player, float baseDamage, float range, float radius, Vector2 target, GameWorld world, string abilityId = "")
    {
        // AOE centered on target position (or player if range = 0)
        var center = range > 0 ? target : player.Position;

        // Clamp target to max range
        if (range > 0)
        {
            var dist = GameMathUtils.Distance(center, player.Position);
            if (dist > range)
            {
                var dir = (center - player.Position).GetNormalized();
                center = player.Position + dir * range;
            }
        }

        var result = new AbilityResult { Success = true, Events = new List<CombatEvent>() };

        // Emit aoe_impact event for client VFX (once per ability, not per target)
        var aoeImpactEvent = new CombatEvent
        {
            EventType = "aoe_impact",
            SourceId = player.PlayerId,
            Value = (int)(radius * 100),
            Position = center,
            RoomId = player.CurrentRoomId,
            AbilityId = abilityId
        };
        result.Events.Add(aoeImpactEvent);
        OnCombatEvent?.Invoke(world.WorldId, aoeImpactEvent);

        var totalHits = 0;

        foreach (var enemy in world.Players.Values)
        {
            if (enemy.PlayerId == player.PlayerId || !enemy.IsAlive || enemy.CurrentRoomId != player.CurrentRoomId)
                continue;
            if (GameMathUtils.Distance(enemy.Position, center) > radius)
                continue;

            var (damage, isCrit) = CalculateAbilityDamage(player, baseDamage, enemy);
            ApplyDamage(enemy, damage);
            enemy.LastDamageTime = DateTime.UtcNow;

            result.Events.Add(new CombatEvent
            {
                EventType = isCrit ? "ability_damage_crit" : "ability_damage",
                SourceId = player.PlayerId,
                TargetId = enemy.PlayerId,
                Value = damage,
                Position = enemy.Position,
                RoomId = player.CurrentRoomId
            });

            OnCombatEvent?.Invoke(world.WorldId, result.Events.Last());

            if (enemy.Health <= 0 && enemy.IsAlive)
                ProcessPlayerDeath(enemy, player);
            totalHits++;
        }

        // Also hit mobs in AOE
        foreach (var mob in world.Mobs.Values)
        {
            if (mob.Health <= 0 || mob.RoomId != player.CurrentRoomId) continue;
            if (GameMathUtils.Distance(mob.Position, center) > radius) continue;
            var (damage, isCrit) = CalculateAbilityDamageVsMob(player, baseDamage);
            mob.Health = Math.Max(0, mob.Health - damage);
            mob.IsDirty = true;
            if (mob is EnhancedMob em3) em3.RequiresUpdate = true;

            var combatEvent = new CombatEvent
            {
                EventType = isCrit ? "ability_damage_crit" : "ability_damage",
                SourceId = player.PlayerId,
                TargetId = mob.MobId,
                Value = damage,
                Position = mob.Position,
                RoomId = player.CurrentRoomId
            };

            result.Events.Add(combatEvent);
            OnCombatEvent?.Invoke(world.WorldId, combatEvent);
            totalHits++;
        }

        result.Message = $"AOE hit {totalHits} target(s)";
        return result;
    }

    private AbilityResult ExecuteDash(RealTimePlayer player, float distance, float speed, Vector2 target)
    {
        var direction = (target - player.Position).GetNormalized();
        if (direction.Magnitude < 0.01f)
            direction = new Vector2((float)Math.Cos(player.Direction), (float)Math.Sin(player.Direction));

        var newPosition = player.Position + direction * distance;

        // Clamp to world bounds
        newPosition = new Vector2(
            Math.Clamp(newPosition.X, 0, 240),
            Math.Clamp(newPosition.Y, 0, 240)
        );

        player.Position = newPosition;
        player.MoveTarget = null;
        player.IsPathing = false;
        player.LastDashTime = DateTime.UtcNow;
        player.ForceNextUpdate();

        return new AbilityResult { Success = true, Message = "Dashed" };
    }

    private AbilityResult ExecuteShield(RealTimePlayer player, int shieldAmount, int durationMs)
    {
        player.MaxShield = Math.Max(player.MaxShield, shieldAmount);
        ApplyStatusEffect(player, new StatusEffect
        {
            EffectType = "shield",
            Value = shieldAmount,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(durationMs),
            SourcePlayerId = player.PlayerId
        });
        player.DamageReduction = player.EquipmentDamageReduction + 0.2f;

        return new AbilityResult { Success = true, Message = "Shield activated" };
    }

    private AbilityResult ExecuteHeal(RealTimePlayer player, float healAmount, float range, Vector2 target, GameWorld world, string abilityId = "")
    {
        var result = new AbilityResult { Success = true, Events = new List<CombatEvent>() };

        // Apply healing bonuses from equipment + levels
        var totalHealBonus = player.BonusHealingPercent + player.LevelBonusHealingPercent;
        var finalHealAmount = healAmount * (1f + totalHealBonus);

        if (range <= 0)
        {
            // Self/AOE heal — emit aoe_impact for visual effect
            float aoeRadius = 8f;
            var aoeImpactEvent = new CombatEvent
            {
                EventType = "aoe_impact",
                SourceId = player.PlayerId,
                Value = (int)(aoeRadius * 100),
                Position = player.Position,
                RoomId = player.CurrentRoomId,
                AbilityId = abilityId
            };
            result.Events.Add(aoeImpactEvent);
            OnCombatEvent?.Invoke(world.WorldId, aoeImpactEvent);

            int healCount = 0;
            foreach (var ally in world.Players.Values)
            {
                if (ally.TeamId != player.TeamId || !ally.IsAlive) continue;
                if (GameMathUtils.Distance(ally.Position, player.Position) > aoeRadius) continue;

                var heal = (int)finalHealAmount;
                ally.Health = Math.Min(ally.MaxHealth, ally.Health + heal);

                result.Events.Add(new CombatEvent
                {
                    EventType = "heal",
                    SourceId = player.PlayerId,
                    TargetId = ally.PlayerId,
                    Value = heal,
                    Position = ally.Position,
                    RoomId = player.CurrentRoomId
                });
                healCount++;
            }

            result.Message = $"Healed {healCount} ally(s)";
        }
        else
        {
            // Targeted heal - find closest ally near target position
            var ally = world.Players.Values
                .Where(p => p.TeamId == player.TeamId && p.IsAlive)
                .Where(p => GameMathUtils.Distance(p.Position, target) <= 2f)
                .OrderBy(p => GameMathUtils.Distance(p.Position, target))
                .FirstOrDefault() ?? player; // Self-heal fallback

            var dist = GameMathUtils.Distance(player.Position, ally.Position);
            if (dist > range)
                return new AbilityResult { Success = false, ErrorMessage = "Target out of range" };

            var heal = (int)finalHealAmount;
            ally.Health = Math.Min(ally.MaxHealth, ally.Health + heal);

            result.Events.Add(new CombatEvent
            {
                EventType = "heal",
                SourceId = player.PlayerId,
                TargetId = ally.PlayerId,
                Value = heal,
                Position = ally.Position,
                RoomId = player.CurrentRoomId
            });

            result.Message = $"Healed {ally.PlayerName}";
        }

        return result;
    }

    private AbilityResult ExecuteBuff(RealTimePlayer player, AbilityDefinition ability, ClassAbilityModifier? modifier, int durationMs, GameWorld world)
    {
        var effect = new StatusEffect
        {
            EffectType = ability.AppliesEffect,
            Value = ability.EffectValue,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(durationMs),
            SourcePlayerId = player.PlayerId
        };

        ApplyStatusEffect(player, effect);

        return new AbilityResult { Success = true, Message = $"Buff {ability.AppliesEffect} applied" };
    }

    private AbilityResult ExecuteStatusApply(RealTimePlayer player, AbilityDefinition ability, float range, Vector2 target, GameWorld world)
    {
        // Purge: remove debuffs from self
        if (ability.AppliesEffect == "purge")
        {
            var debuffs = new[] { "poison", "slow", "bleed", "weaken", "stun" };
            player.StatusEffects.RemoveAll(e => debuffs.Contains(e.EffectType.ToLower()));
            player.MovementSpeedModifier = 1f + player.EquipmentSpeedBonus;
            return new AbilityResult { Success = true, Message = "Debuffs purged" };
        }

        // Apply status to enemies in range
        if (range > 0)
        {
            int effectCount = 0;
            foreach (var enemy in world.Players.Values)
            {
                if (enemy.PlayerId == player.PlayerId || !enemy.IsAlive || enemy.CurrentRoomId != player.CurrentRoomId)
                    continue;
                if (GameMathUtils.Distance(enemy.Position, player.Position) > range)
                    continue;

                ApplyStatusEffect(enemy, new StatusEffect
                {
                    EffectType = ability.AppliesEffect,
                    Value = ability.EffectValue,
                    ExpiresAt = DateTime.UtcNow.AddMilliseconds(ability.EffectDurationMs),
                    SourcePlayerId = player.PlayerId
                });
                effectCount++;
            }

            return new AbilityResult { Success = true, Message = $"Applied {ability.AppliesEffect} to {effectCount} target(s)" };
        }

        return new AbilityResult { Success = true, Message = "Status applied" };
    }

    private AbilityResult ExecuteStealth(RealTimePlayer player, int durationMs)
    {
        ApplyStatusEffect(player, new StatusEffect
        {
            EffectType = "stealth",
            Value = 0,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(durationMs),
            SourcePlayerId = player.PlayerId
        });

        return new AbilityResult { Success = true, Message = "Entered stealth" };
    }

    private AbilityResult ExecuteTaunt(RealTimePlayer player, float range, GameWorld world)
    {
        return new AbilityResult { Success = true, Message = "Taunt activated" };
    }

    #endregion

    #region Private Methods

    private List<RealTimePlayer> FindTargetsInRange(RealTimePlayer attacker, IEnumerable<RealTimePlayer> potentialTargets, float attackRange)
    {
        var targets = new List<RealTimePlayer>();

        // Lag compensation
        var attackerRtt = RttLookup?.Invoke(attacker.PlayerId) ?? 0f;
        var compensationMs = Math.Min(attackerRtt, _settings.NetworkSettings.MaxLagCompensationMs);
        var currentTime = (float)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        var evaluationTime = currentTime - (compensationMs / 2000f);

        foreach (var player in potentialTargets)
        {
            if (player.PlayerId == attacker.PlayerId || !player.IsAlive) continue;
            if (player.CurrentRoomId != attacker.CurrentRoomId) continue;

            var targetPos = compensationMs > 10
                ? player.GetPositionAtTime(evaluationTime)
                : player.Position;

            var distance = GameMathUtils.Distance(targetPos, attacker.Position);
            if (distance <= attackRange)
            {
                var directionToTarget = (targetPos - attacker.Position).GetNormalized();
                var attackDirection = new Vector2(
                    (float)Math.Cos(attacker.Direction),
                    (float)Math.Sin(attacker.Direction)
                );

                var dotProduct = directionToTarget.X * attackDirection.X + directionToTarget.Y * attackDirection.Y;
                if (dotProduct > 0.5f)
                {
                    targets.Add(player);
                }
            }
        }

        return targets;
    }

    private List<Mob> FindMobTargetsInRange(RealTimePlayer attacker, IEnumerable<Mob> potentialMobs, float attackRange)
    {
        var targets = new List<Mob>();

        foreach (var mob in potentialMobs)
        {
            if (mob.Health <= 0) continue;
            if (mob.RoomId != attacker.CurrentRoomId) continue;

            var distance = GameMathUtils.Distance(mob.Position, attacker.Position);
            if (distance <= attackRange)
            {
                targets.Add(mob);
            }
        }

        return targets;
    }

    private (int damage, bool isCrit) CalculateDamage(RealTimePlayer attacker, float baseDamage, RealTimePlayer? target = null)
    {
        var totalBonusDmg = attacker.BonusDamagePercent + attacker.LevelBonusDamagePercent;
        var damage = baseDamage * (1f + totalBonusDmg);

        // Variance ±10%
        damage *= 1f + (_random.NextSingle() * 0.2f - 0.1f);

        // Weaken debuff
        var weaken = attacker.StatusEffects.FirstOrDefault(e => e.EffectType == "weaken");
        if (weaken != null)
            damage *= 1f - (weaken.Value * 0.01f);

        // Damage boost buff
        var dmgBoost = attacker.StatusEffects.FirstOrDefault(e => e.EffectType == "damage_boost");
        if (dmgBoost != null)
            damage *= 1f + (dmgBoost.Value * 0.01f);

        // Crit
        bool isCrit = _random.NextSingle() < attacker.CritChance;
        if (isCrit) damage *= 1.5f;

        // Target DR
        if (target != null)
            damage *= 1f - target.DamageReduction;

        return (Math.Max(1, (int)damage), isCrit);
    }

    private (int damage, bool isCrit) CalculateBasicAttackDamageVsMob(RealTimePlayer attacker)
    {
        var abilities = _equipmentSystem.GetAvailableAbilities(attacker);
        var basicAbility = abilities.FirstOrDefault(a => a.SlotIndex == 0 &&
            (a.Type == AbilityType.MeleeDamage || a.Type == AbilityType.ProjectileDamage));

        var baseDamage = basicAbility?.BaseDamage ?? 15f;
        return CalculateDamage(attacker, baseDamage);
    }

    private (int damage, bool isCrit) CalculateAbilityDamageVsMob(RealTimePlayer attacker, float abilityBaseDamage)
    {
        return CalculateDamage(attacker, abilityBaseDamage);
    }

    private (int damage, bool isCrit) CalculateBasicAttackDamage(RealTimePlayer attacker, RealTimePlayer target)
    {
        var abilities = _equipmentSystem.GetAvailableAbilities(attacker);
        var basicAbility = abilities.FirstOrDefault(a => a.SlotIndex == 0 &&
            (a.Type == AbilityType.MeleeDamage || a.Type == AbilityType.ProjectileDamage));

        var baseDamage = basicAbility?.BaseDamage ?? 15f;
        return CalculateDamage(attacker, baseDamage, target);
    }

    private (int damage, bool isCrit) CalculateAbilityDamage(RealTimePlayer attacker, float abilityBaseDamage, RealTimePlayer target)
    {
        return CalculateDamage(attacker, abilityBaseDamage, target);
    }

    private void ApplyDamage(RealTimePlayer target, int damage)
    {
        var remainingDamage = damage;

        if (target.Shield > 0)
        {
            var shieldDamage = Math.Min(target.Shield, remainingDamage);
            target.Shield -= shieldDamage;
            remainingDamage -= shieldDamage;
        }

        if (remainingDamage > 0)
        {
            target.Health = Math.Max(0, target.Health - remainingDamage);
        }
    }

    public void ProcessPlayerDeath(RealTimePlayer deadPlayer, RealTimePlayer? killer)
    {
        // Guard against double death processing (idempotent)
        if (!deadPlayer.IsAlive) return;

        deadPlayer.IsAlive = false;
        deadPlayer.Health = 0;
        deadPlayer.DeathTime = DateTime.UtcNow;
        deadPlayer.ForceNextUpdate();

        // If the dead player was carrying a soul, drop it (clear carrier state and restore speed)
        if (deadPlayer.CarryingSoulOfPlayerId != null)
        {
            _logger.LogInformation("Player {PlayerName} died while carrying soul of {SoulId}, soul dropped",
                deadPlayer.PlayerName, deadPlayer.CarryingSoulOfPlayerId);
            deadPlayer.CarryingSoulOfPlayerId = null;
            // Speed modifier will be reset when they are revived, no need to restore here
        }

        var killerName = killer?.PlayerName ?? "Environment";
        _logger.LogInformation("Player {DeadPlayer} was killed by {Killer}",
            deadPlayer.PlayerName, killerName);

        OnPlayerDeath?.Invoke(deadPlayer, killer);
    }

    private void ApplyClassExtraEffect(RealTimePlayer source, ClassAbilityModifier modifier, Vector2 target, GameWorld world, int damageDealt = 0)
    {
        switch (modifier.ExtraEffect.ToLower())
        {
            case "bleed":
            case "poison":
                var dotTarget = FindNearestEnemy(source, target, 3f, world);
                if (dotTarget != null)
                {
                    ApplyStatusEffect(dotTarget, new StatusEffect
                    {
                        EffectType = modifier.ExtraEffect,
                        Value = modifier.ExtraEffectValue,
                        ExpiresAt = DateTime.UtcNow.AddMilliseconds(modifier.ExtraEffectDurationMs),
                        SourcePlayerId = source.PlayerId
                    });
                }
                break;

            case "stun":
                var stunTarget = FindNearestEnemy(source, target, 3f, world);
                if (stunTarget != null)
                {
                    ApplyStatusEffect(stunTarget, new StatusEffect
                    {
                        EffectType = "stun",
                        Value = 0,
                        ExpiresAt = DateTime.UtcNow.AddMilliseconds(modifier.ExtraEffectDurationMs),
                        SourcePlayerId = source.PlayerId
                    });
                    stunTarget.IsMoving = false;
                    stunTarget.MoveTarget = null;
                    stunTarget.IsPathing = false;
                }
                break;

            case "slow":
                var slowTarget = FindNearestEnemy(source, target, 3f, world);
                if (slowTarget != null)
                {
                    ApplyStatusEffect(slowTarget, new StatusEffect
                    {
                        EffectType = "slow",
                        Value = -modifier.ExtraEffectValue,
                        ExpiresAt = DateTime.UtcNow.AddMilliseconds(modifier.ExtraEffectDurationMs),
                        SourcePlayerId = source.PlayerId
                    });
                }
                break;

            case "knockback":
                var kbTarget = FindNearestEnemy(source, target, 3f, world);
                if (kbTarget != null)
                {
                    var dir = (kbTarget.Position - source.Position).GetNormalized();
                    kbTarget.Position = kbTarget.Position + dir * modifier.ExtraEffectValue;
                    kbTarget.MoveTarget = null;
                    kbTarget.IsPathing = false;
                    kbTarget.ForceNextUpdate();
                }
                break;

            case "life_steal":
                // ExtraEffectValue = percentage of damage dealt to heal (e.g. 10 = 10%)
                var lsHeal = Math.Max(1, (int)(modifier.ExtraEffectValue * 0.01f * damageDealt));
                source.Health = Math.Min(source.MaxHealth, source.Health + lsHeal);
                break;

            case "team_heal":
                foreach (var ally in world.Players.Values)
                {
                    if (ally.PlayerId == source.PlayerId || ally.TeamId != source.TeamId || !ally.IsAlive) continue;
                    if (GameMathUtils.Distance(ally.Position, source.Position) > 6f) continue;
                    var thHeal = Math.Max(1, (int)(modifier.ExtraEffectValue * 0.01f * damageDealt));
                    ally.Health = Math.Min(ally.MaxHealth, ally.Health + thHeal);
                }
                break;

            case "speed_boost_allies":
                foreach (var ally in world.Players.Values)
                {
                    if (ally.TeamId != source.TeamId || !ally.IsAlive) continue;
                    if (GameMathUtils.Distance(ally.Position, source.Position) > 6f) continue;
                    ApplyStatusEffect(ally, new StatusEffect
                    {
                        EffectType = "speed",
                        Value = modifier.ExtraEffectValue,
                        ExpiresAt = DateTime.UtcNow.AddMilliseconds(modifier.ExtraEffectDurationMs),
                        SourcePlayerId = source.PlayerId
                    });
                }
                break;

            case "weaken":
                var weakenTarget = FindNearestEnemy(source, target, 6f, world);
                if (weakenTarget != null)
                {
                    ApplyStatusEffect(weakenTarget, new StatusEffect
                    {
                        EffectType = "weaken",
                        Value = modifier.ExtraEffectValue,
                        ExpiresAt = DateTime.UtcNow.AddMilliseconds(modifier.ExtraEffectDurationMs),
                        SourcePlayerId = source.PlayerId
                    });
                }
                break;

            case "regen":
                ApplyStatusEffect(source, new StatusEffect
                {
                    EffectType = "regen",
                    Value = modifier.ExtraEffectValue,
                    ExpiresAt = DateTime.UtcNow.AddMilliseconds(modifier.ExtraEffectDurationMs),
                    SourcePlayerId = source.PlayerId
                });
                break;
        }
    }

    private RealTimePlayer? FindNearestEnemy(RealTimePlayer source, Vector2 position, float maxRange, GameWorld world)
    {
        return world.Players.Values
            .Where(p => p.PlayerId != source.PlayerId && p.IsAlive && p.CurrentRoomId == source.CurrentRoomId)
            .Where(p => p.TeamId != source.TeamId)
            .Where(p => GameMathUtils.Distance(p.Position, position) <= maxRange)
            .OrderBy(p => GameMathUtils.Distance(p.Position, position))
            .FirstOrDefault();
    }

    private void ProcessStatusEffect(RealTimePlayer player, StatusEffect effect, float deltaTime)
    {
        switch (effect.EffectType.ToLower())
        {
            case "poison":
            case "bleed":
                if ((DateTime.UtcNow - effect.LastTickTime).TotalSeconds >= 1.0)
                {
                    effect.LastTickTime = DateTime.UtcNow;
                    player.Health = Math.Max(0, player.Health - effect.Value);
                    player.LastDamageTime = DateTime.UtcNow;

                    if (player.Health <= 0 && player.IsAlive)
                    {
                        ProcessPlayerDeath(player, null);
                    }
                }
                break;

            case "regen":
                if ((DateTime.UtcNow - effect.LastTickTime).TotalSeconds >= 1.0)
                {
                    effect.LastTickTime = DateTime.UtcNow;
                    player.Health = Math.Min(player.MaxHealth, player.Health + effect.Value);
                }
                break;

            case "slow":
                player.MovementSpeedModifier = Math.Max(0.3f, 1f + player.EquipmentSpeedBonus + (effect.Value * 0.01f));
                break;

            case "speed":
            case "speed_boost":
                player.MovementSpeedModifier = 1f + player.EquipmentSpeedBonus + (effect.Value * 0.01f);
                break;

            case "stun":
                player.IsMoving = false;
                player.MoveTarget = null;
                player.IsPathing = false;
                break;
        }
    }

    private void RemoveStatusEffect(RealTimePlayer player, StatusEffect effect)
    {
        switch (effect.EffectType.ToLower())
        {
            case "slow":
            case "speed":
            case "speed_boost":
                player.MovementSpeedModifier = 1f + player.EquipmentSpeedBonus;
                break;
            case "shield":
                player.DamageReduction = player.EquipmentDamageReduction;
                player.Shield = 0;
                break;
        }
    }

    #endregion
}
