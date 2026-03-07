using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.AI.Interface;
using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Engine.MobIASystem.Models;
using MazeWars.GameServer.Engine.Movement.Interface;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Services.Combat;
using MazeWars.GameServer.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading;

namespace MazeWars.GameServer.Services.AI;

public class MobAISystem : IMobAISystem
{
    private readonly ILogger<MobAISystem> _logger;
    private readonly GameServerSettings _settings;
    private readonly ProjectileSystem _projectileSystem;
    private readonly ICombatSystem _combatSystem;
    private readonly IMovementSystem _movementSystem;
    private AISettings _aiSettings;
    // Removed: _random field — use Random.Shared for thread safety in parallel contexts

    // Templates and configuration
    private readonly ConcurrentDictionary<string, MobTemplate> _mobTemplates = new();
    private readonly ConcurrentDictionary<string, NavigationMesh> _worldNavMeshes = new();

    // World-specific AI tracking
    private readonly ConcurrentDictionary<string, AIStats> _worldAIStats = new();
    private readonly ConcurrentDictionary<string, List<EnhancedMob>> _worldMobs = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, List<EnhancedMob>>> _mobGroups = new();

    // Performance optimization
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastDynamicSpawn = new();
    private int _updateCycle = 0;

    // Reusable buffers to reduce GC allocations on hot paths (ThreadStatic for parallel safety)
    [ThreadStatic] private static List<EnhancedMob>? t_priorityCritical;
    [ThreadStatic] private static List<EnhancedMob>? t_priorityHigh;
    [ThreadStatic] private static List<EnhancedMob>? t_priorityMedium;
    [ThreadStatic] private static List<EnhancedMob>? t_priorityLow;
    [ThreadStatic] private static List<EnhancedMob>? t_tempMobBuffer;
    [ThreadStatic] private static List<RealTimePlayer>? t_tempPlayerBuffer;
    [ThreadStatic] private static List<string>? t_tempExpiredKeys;

    // Events
    public event Action<string, Mob>? OnMobSpawned;
    public event Action<string, Mob, RealTimePlayer?>? OnMobDeath;
    public event Action<Mob, MobState, MobState>? OnMobStateChanged;
    public event Action<Mob, RealTimePlayer, int, bool>? OnMobAttack;
    public event Action<string, Mob>? OnBossSpawned;
    public event Action<RealTimePlayer, Mob>? OnPlayerKilledByMob;
    public event Action<string, CombatEvent>? OnMobAbilityUsed;

    public MobAISystem(ILogger<MobAISystem> logger, IOptions<GameServerSettings> settings, ProjectileSystem projectileSystem, ICombatSystem combatSystem, IMovementSystem movementSystem)
    {
        _logger = logger;
        _settings = settings.Value;
        _projectileSystem = projectileSystem;
        _combatSystem = combatSystem;
        _movementSystem = movementSystem;
        // Random.Shared is used directly for thread safety

        // Initialize AI settings
        _aiSettings = new AISettings
        {
            GlobalAggressionMultiplier = 1.0f,
            UpdateFrequency = 30.0f,
            MaxMobsPerRoom = 10,
            DifficultyScaling = 1.0f,
            EnableGroupBehavior = true,
            EnableDynamicSpawning = true,
            DynamicSpawnInterval = 20.0f,
            MaxDynamicMobs = 40,
            EnablePerformanceOptimization = true,
            OptimizationDistance = 50.0f,
            EnableBossAI = true,
            BossSpawnChance = 0.25f
        };

        // Initialize default mob templates
        InitializeDefaultTemplates();

        _logger.LogInformation("MobAISystem initialized with {TemplateCount} mob templates", _mobTemplates.Count);
    }

    // =============================================
    // CORE MOB MANAGEMENT
    // =============================================

    public void InitializeMobTemplates(Dictionary<string, MobTemplate> templates)
    {
        _mobTemplates.Clear();
        foreach (var (type, template) in templates)
        {
            _mobTemplates[type] = template;
        }

        _logger.LogInformation("Initialized {Count} mob templates: {Types}",
            _mobTemplates.Count, string.Join(", ", _mobTemplates.Keys));
    }

    public List<Mob> SpawnInitialMobs(GameWorld world)
    {
        var spawnedMobs = new List<Mob>();
        var settings = _settings.WorldGeneration;
        var stats = GetOrCreateWorldStats(world.WorldId);

        // Clear any existing mobs for this world
        _worldMobs[world.WorldId] = new List<EnhancedMob>();
        _mobGroups[world.WorldId] = new Dictionary<string, List<EnhancedMob>>();
        _lastDynamicSpawn[world.WorldId] = DateTime.UtcNow;

        // Spawn mobs in rooms (excluding extraction rooms and spawn rooms)
        var spawnableRooms = world.Rooms.Values
            .Where(r => !IsExtractionRoom(r.RoomId, world) && !IsSpawnRoom(r))
            .ToList();

        foreach (var room in spawnableRooms)
        {
            var mobCount = CalculateMobCountForRoom(room, world);

            for (int i = 0; i < mobCount; i++)
            {
                // Guarantee first mob matches the room's expected type
                string mobType;
                if (i == 0 && room.RoomType == "boss_arena")
                    mobType = "boss";
                else if (i == 0 && room.RoomType == "elite_chamber")
                    mobType = "elite";
                else
                    mobType = SelectMobTypeForRoom(room, world);

                var spawnPosition = CalculateSpawnPosition(room);

                var mob = SpawnMob(world, mobType, spawnPosition, room.RoomId);
                spawnedMobs.Add(mob);

                stats.TotalMobs++;
                stats.AliveMobs++;

                if (!stats.MobsByType.ContainsKey(mobType))
                    stats.MobsByType[mobType] = 0;
                stats.MobsByType[mobType]++;
            }
        }

        // Create navigation mesh for this world
        CreateNavigationMesh(world);

        _logger.LogInformation("Spawned {MobCount} initial mobs across {RoomCount} rooms in world {WorldId}",
            spawnedMobs.Count, spawnableRooms.Count, world.WorldId);

        return spawnedMobs;
    }

    public Mob SpawnMob(GameWorld world, string mobType, Vector2 position, string roomId)
    {
        if (!_mobTemplates.TryGetValue(mobType, out var template))
        {
            _logger.LogWarning("Unknown mob type: {MobType}, using default", mobType);
            template = _mobTemplates.Values.First(); // Fallback to first available
        }

        var mobId = $"mob_{roomId}_{Guid.NewGuid().ToString()[..8]}";
        var enhancedMob = new EnhancedMob
        {
            MobId = mobId,
            MobType = mobType,
            Position = position,
            OriginalPosition = position,
            PatrolTarget = position + GenerateRandomOffset(template.BehaviorSettings.PatrolRadius),
            RoomId = roomId,
            Template = template,
            EnhancedStats = CalculateMobStats(mobType, world, world.Rooms.TryGetValue(roomId, out var spawnRoom) ? spawnRoom : null!),
            CurrentState = MobState.Spawning,
            StateChangedAt = DateTime.UtcNow,
            AggressionLevel = template.BehaviorSettings.AggressionLevel * _aiSettings.GlobalAggressionMultiplier,
            RequiresUpdate = true,
            ProcessingPriority = AIProcessingPriority.Low
        };

        // Apply enhanced stats to base mob properties
        enhancedMob.Health = enhancedMob.EnhancedStats.Health;
        enhancedMob.MaxHealth = enhancedMob.EnhancedStats.MaxHealth;
        enhancedMob.State = "spawning";

        // Add to world collections
        world.Mobs[mobId] = enhancedMob;

        if (!_worldMobs.ContainsKey(world.WorldId))
            _worldMobs[world.WorldId] = new List<EnhancedMob>();
        _worldMobs[world.WorldId].Add(enhancedMob);

        // Handle group spawning
        if (template.BehaviorSettings.CanCallForHelp)
        {
            AssignToGroup(enhancedMob, world);
        }

        // Fire spawn event
        OnMobSpawned?.Invoke(world.WorldId, enhancedMob);

        // Check if this should be a boss
        if (template.IsBoss || (Random.Shared.NextDouble() < _aiSettings.BossSpawnChance && mobType == "elite"))
        {
            ConvertToBoss(enhancedMob);
            OnBossSpawned?.Invoke(world.WorldId, enhancedMob);
        }

        // Transition to idle after spawning
        ChangeState(enhancedMob, MobState.Idle, "Initial spawn");

        _logger.LogDebug("Spawned {MobType} mob {MobId} at {Position} in room {RoomId}",
            mobType, mobId, position, roomId);

        return enhancedMob;
    }

    public void UpdateMobs(GameWorld world, float deltaTime)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var worldMobs))
            return;

        var stats = GetOrCreateWorldStats(world.WorldId);
        Interlocked.Increment(ref _updateCycle);

        // Update spatial partitioning and mob activation based on player proximity
        if (_aiSettings.EnablePerformanceOptimization)
        {
            UpdateSpatialPartitioning(world);
            OptimizeAIProcessing(world);
        }

        // Process mobs by priority
        ProcessAIByPriority(world, deltaTime);

        // Handle group behaviors
        if (_aiSettings.EnableGroupBehavior)
        {
            ProcessGroupBehaviors(world);
        }

        // Dynamic spawning
        if (_aiSettings.EnableDynamicSpawning)
        {
            ProcessDynamicSpawning(world, deltaTime);
        }

        // Process dead mobs
        ProcessDeadMobs(world);

        // Update stats
        stats.AliveMobs = worldMobs.Count(m => m.Health > 0);
        stats.DeadMobs = worldMobs.Count(m => m.Health <= 0);
    }

    public void ProcessDeadMobs(GameWorld world)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var worldMobs))
            return;

        for (int i = 0; i < worldMobs.Count; i++)
        {
            var mob = worldMobs[i];
            if (mob.Health <= 0 && mob.CurrentState != MobState.Dead)
            {
                ProcessMobDeath(mob, world);
            }
        }
    }

    // =============================================
    // AI BEHAVIOR SYSTEM
    // =============================================

    public AIBehaviorResult UpdateMobBehavior(Mob mob, GameWorld world, float deltaTime)
    {
        if (mob == null) return new AIBehaviorResult();
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
        if (enhancedMob == null) return new AIBehaviorResult();
        var result = new AIBehaviorResult();

        // Skip if mob doesn't require update (performance optimization)
        if (!enhancedMob.RequiresUpdate && _aiSettings.EnablePerformanceOptimization)
        {
            return result;
        }

        var context = CreateDecisionContext(enhancedMob, world, deltaTime);
        var decision = MakeAIDecision(context);

        // Execute the decision
        ExecuteAIDecision(enhancedMob, decision, world, result);

        // Update mob's last update time
        enhancedMob.LastUpdate = DateTime.UtcNow;
        enhancedMob.RequiresUpdate = false;

        return result;
    }

    public RealTimePlayer? FindBestTarget(Mob mob, GameWorld world)
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
        var template = enhancedMob.Template;
        var detectionRange = enhancedMob.EnhancedStats.DetectionRange;
        var hasPreferred = template?.BehaviorSettings.PreferredTargets.Any() == true;

        RealTimePlayer? bestTarget = null;
        float bestDistance = float.MaxValue;
        bool bestIsPreferred = false;

        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive || player.CurrentRoomId != mob.RoomId) continue;

            var dist = Vector2.Distance(player.Position, mob.Position);
            if (dist > detectionRange) continue;

            bool isPreferred = hasPreferred &&
                template!.BehaviorSettings.PreferredTargets.Contains(player.PlayerClass);

            // Prefer preferred targets; among same preference, pick closest
            if (bestTarget == null ||
                (isPreferred && !bestIsPreferred) ||
                (isPreferred == bestIsPreferred && dist < bestDistance))
            {
                bestTarget = player;
                bestDistance = dist;
                bestIsPreferred = isPreferred;
            }
        }

        return bestTarget;
    }

    public List<Vector2> CalculatePath(Mob mob, Vector2 target, GameWorld world)
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);

        // Check if we can reuse existing path
        if (enhancedMob.CurrentPath.Any() &&
            (DateTime.UtcNow - enhancedMob.PathCalculatedAt).TotalSeconds < 2.0)
        {
            return enhancedMob.CurrentPath;
        }

        // Simple pathfinding implementation - can be enhanced with A* later
        var path = new List<Vector2>();
        var current = mob.Position;
        var direction = (target - current).GetNormalized();
        var distance = Vector2.Distance(current, target);
        var stepSize = 2.0f;
        var steps = (int)(distance / stepSize);

        for (int i = 1; i <= steps; i++)
        {
            var nextPoint = current + direction * (stepSize * i);
            if (IsValidPosition(nextPoint, world))
            {
                path.Add(nextPoint);
            }
        }

        if (path.Any())
        {
            path.Add(target);
        }

        // Cache the path
        enhancedMob.CurrentPath = path;
        enhancedMob.PathCalculatedAt = DateTime.UtcNow;
        enhancedMob.CurrentPathIndex = 0;

        return path;
    }

    public MobCombatResult ProcessMobCombat(Mob mob, RealTimePlayer target, GameWorld world)
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
        var result = new MobCombatResult();

        // Dead mobs can't attack, dead players shouldn't receive damage
        if (mob.Health <= 0 || !target.IsAlive) return result;

        // Block attacks through closed doors
        if (!HasLineOfSight(mob, target, world)) return result;

        // Check if mob can attack
        var timeSinceLastAttack = DateTime.UtcNow - enhancedMob.LastAttackTime;
        if (timeSinceLastAttack.TotalSeconds < enhancedMob.EnhancedStats.AttackCooldown)
        {
            return result;
        }

        // Check range
        var distance = Vector2.Distance(mob.Position, target.Position);
        if (distance > enhancedMob.EnhancedStats.AttackRange)
        {
            return result;
        }

        // Calculate damage
        var baseDamage = enhancedMob.EnhancedStats.Damage;
        bool isCrit = Random.Shared.NextDouble() < enhancedMob.EnhancedStats.CriticalChance;
        var finalDamage = CalculateFinalDamage(enhancedMob, target, baseDamage, isCrit);

        // Re-check target is alive (defense-in-depth against TOCTOU)
        if (!target.IsAlive) return result;

        // Apply damage with shield absorption
        var remainingDamage = finalDamage;
        if (target.Shield > 0)
        {
            var absorbed = Math.Min(target.Shield, remainingDamage);
            target.Shield -= absorbed;
            remainingDamage -= absorbed;
            if (target.Shield <= 0)
            {
                lock (target.StatusEffectsLock)
                {
                    target.StatusEffects.RemoveAll(e => e.EffectType == "shield");
                }
                target.DamageReduction = target.EquipmentDamageReduction;
            }
        }
        target.Health = Math.Max(0, target.Health - remainingDamage);
        target.LastDamageTime = DateTime.UtcNow;

        // Update mob state
        enhancedMob.LastAttackTime = DateTime.UtcNow;
        enhancedMob.AttacksPerformed++;
        enhancedMob.LastAttacker = target.PlayerId;

        // Set result
        result.AttackPerformed = true;
        result.DamageDealt = finalDamage;
        result.IsCrit = isCrit;
        result.TargetKilled = target.Health <= 0;

        // Fire attack event (include crit flag)
        OnMobAttack?.Invoke(enhancedMob, target, finalDamage, isCrit);

        // Check if target (player) died — use centralized death processing
        if (target.Health <= 0 && target.IsAlive)
        {
            result.TargetKilled = true;
            _combatSystem.ProcessPlayerDeath(target, null);

            var stats = GetOrCreateWorldStats(world.WorldId);
            stats.PlayerKills++;

            OnPlayerKilledByMob?.Invoke(target, enhancedMob);
        }

        _logger.LogDebug("Mob {MobId} attacked {PlayerName} for {Damage} damage",
            mob.MobId, target.PlayerName, finalDamage);

        return result;
    }

    // =============================================
    // MOB STATES AND TRANSITIONS
    // =============================================

    public bool ChangeState(Mob mob, MobState newState, string reason = "")
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
        var oldState = enhancedMob.CurrentState;

        if (!CanTransitionToState(enhancedMob, oldState, newState))
        {
            return false;
        }

        enhancedMob.PreviousState = oldState;
        enhancedMob.CurrentState = newState;
        enhancedMob.StateChangedAt = DateTime.UtcNow;
        enhancedMob.StateChangeReason = reason;
        enhancedMob.StateChanges++;
        enhancedMob.RequiresUpdate = true;

        // Update base mob state for compatibility
        enhancedMob.State = newState.ToString().ToLower();
        enhancedMob.LastStateChange = DateTime.UtcNow;

        // Update stats
        var stats = GetOrCreateWorldStats(GetWorldIdForMob(enhancedMob));
        if (stats != null)
        {
            if (!stats.MobsByState.ContainsKey(newState))
                stats.MobsByState[newState] = 0;
            stats.MobsByState[newState]++;

            if (stats.MobsByState.ContainsKey(oldState))
                stats.MobsByState[oldState] = Math.Max(0, stats.MobsByState[oldState] - 1);

            stats.StateChanges++;
        }

        OnMobStateChanged?.Invoke(enhancedMob, oldState, newState);

        _logger.LogDebug("Mob {MobId} changed state from {OldState} to {NewState}: {Reason}",
            enhancedMob.MobId, oldState, newState, reason);

        return true;
    }

    public bool CanTransitionToState(Mob mob, MobState currentState, MobState newState)
    {
        // Dead mobs can't transition to other states
        if (currentState == MobState.Dead)
            return false;

        // Some basic transition rules
        switch (newState)
        {
            case MobState.Dead:
                return mob.Health <= 0;

            case MobState.Spawning:
                return currentState == MobState.Dead; // Only for respawning

            case MobState.Stunned:
                return currentState != MobState.Dead;

            default:
                return currentState != MobState.Dead;
        }
    }

    public List<MobAction> GetAvailableActions(Mob mob, GameWorld world)
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
        var actions = new List<MobAction>();

        switch (enhancedMob.CurrentState)
        {
            case MobState.Idle:
            case MobState.Patrol:
                actions.AddRange(new[] { MobAction.Move, MobAction.Patrol, MobAction.Search });
                break;

            case MobState.Alert:
                actions.AddRange(new[] { MobAction.Move, MobAction.Search });
                break;

            case MobState.Pursuing:
                actions.AddRange(new[] { MobAction.Move, MobAction.Attack });
                break;

            case MobState.Attacking:
                actions.AddRange(new[] { MobAction.Attack, MobAction.Cast });
                break;

            case MobState.Fleeing:
                actions.Add(MobAction.Flee);
                break;

            case MobState.Guarding:
                actions.AddRange(new[] { MobAction.Guard, MobAction.Attack });
                break;
        }

        // Add abilities if available
        if (enhancedMob.Template?.Abilities.Any() == true)
        {
            actions.Add(MobAction.Cast);
        }

        // Add group actions if mob can call for help
        if (enhancedMob.Template?.BehaviorSettings.CanCallForHelp == true)
        {
            actions.Add(MobAction.Roar); // Call for help
        }

        return actions;
    }

    // =============================================
    // ADVANCED AI FEATURES
    // =============================================

    public void ProcessGroupBehavior(List<Mob> mobGroup, GameWorld world)
    {
        if (mobGroup.Count < 2) return;

        var enhancedMobs = new List<EnhancedMob>(mobGroup.Count);
        for (int i = 0; i < mobGroup.Count; i++)
        {
            if (mobGroup[i] is EnhancedMob em)
                enhancedMobs.Add(em);
        }
        if (enhancedMobs.Count < 2) return;

        EnhancedMob? leader = null;
        for (int i = 0; i < enhancedMobs.Count; i++)
        {
            if (enhancedMobs[i].IsGroupLeader) { leader = enhancedMobs[i]; break; }
        }
        leader ??= enhancedMobs[0];

        // Determine group behavior based on leader's state
        switch (leader.CurrentState)
        {
            case MobState.Alert:
            case MobState.Pursuing:
                // Coordinate pursuit
                CoordinatePursuit(enhancedMobs, world);
                break;

            case MobState.Attacking:
                // Focus fire or spread out
                CoordinateAttack(enhancedMobs, world);
                break;

            case MobState.Fleeing:
                // Group retreat
                CoordinateRetreat(enhancedMobs, world);
                break;
        }
    }

    public void ProcessDynamicSpawning(GameWorld world, float deltaTime)
    {
        if (!_lastDynamicSpawn.TryGetValue(world.WorldId, out var lastSpawn))
        {
            _lastDynamicSpawn[world.WorldId] = DateTime.UtcNow;
            return;
        }

        var timeSinceLastSpawn = DateTime.UtcNow - lastSpawn;
        if (timeSinceLastSpawn.TotalSeconds < _aiSettings.DynamicSpawnInterval)
            return;

        int currentMobCount = 0;
        if (_worldMobs.TryGetValue(world.WorldId, out var mobs))
        {
            for (int i = 0; i < mobs.Count; i++)
                if (mobs[i].Health > 0) currentMobCount++;
        }

        if (currentMobCount >= _aiSettings.MaxDynamicMobs)
            return;

        // Find rooms that need more mobs
        var roomsNeedingMobs = new List<Room>();
        foreach (var r in world.Rooms.Values)
        {
            // Skip completed boss/elite rooms to prevent infinite respawns
            if ((r.RoomType == "boss_arena" || r.RoomType == "elite_chamber") && r.IsCompleted)
                continue;

            if (!IsExtractionRoom(r.RoomId, world) && !IsSpawnRoom(r) &&
                GetMobCountInRoom(world, r.RoomId) < _aiSettings.MaxMobsPerRoom / 2)
                roomsNeedingMobs.Add(r);
        }

        if (roomsNeedingMobs.Count > 0)
        {
            var room = roomsNeedingMobs[Random.Shared.Next(roomsNeedingMobs.Count)];

            // If the room should have a guaranteed mob type and is missing it, respawn that type
            string mobType;
            if (room.RoomType == "boss_arena" && !RoomHasMobType(world, room.RoomId, "boss"))
                mobType = "boss";
            else if (room.RoomType == "elite_chamber" && !RoomHasMobType(world, room.RoomId, "elite"))
                mobType = "elite";
            else
                mobType = SelectMobTypeForRoom(room, world);

            var position = CalculateSpawnPosition(room);

            SpawnMob(world, mobType, position, room.RoomId);
            _lastDynamicSpawn[world.WorldId] = DateTime.UtcNow;

            _logger.LogDebug("Dynamically spawned {MobType} in room {RoomId}", mobType, room.RoomId);
        }
    }

    public void ProcessBossAI(Mob bossMob, GameWorld world, float deltaTime)
    {
        var enhancedBoss = bossMob as EnhancedMob ?? ConvertToEnhancedMob(bossMob);

        if (!enhancedBoss.Template?.IsBoss == true)
            return;

        // Boss-specific behaviors
        var healthPercentage = (float)enhancedBoss.Health / enhancedBoss.MaxHealth;

        // Enrage at low health
        if (healthPercentage < 0.3f && enhancedBoss.CurrentState != MobState.Enraged)
        {
            ChangeState(enhancedBoss, MobState.Enraged, "Low health enrage");
            enhancedBoss.AggressionLevel *= 1.5f;
            enhancedBoss.EnhancedStats.AttackCooldown *= 0.7f; // Faster attacks
        }

        // Boss abilities
        ProcessBossAbilities(enhancedBoss, world);

        // Summon minions if alone
        if (healthPercentage < 0.5f)
        {
            SummonMinions(enhancedBoss, world);
        }
    }

    public MobAbilityResult ProcessMobAbility(Mob mob, string abilityType, GameWorld world)
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
        var result = new MobAbilityResult { AbilityName = abilityType };

        // Check cooldown
        if (enhancedMob.AbilityCooldowns.TryGetValue(abilityType, out var lastUsed))
        {
            var cooldown = enhancedMob.Template?.BehaviorSettings.AbilityCooldown ?? 10.0f;
            if ((DateTime.UtcNow - lastUsed).TotalSeconds < cooldown)
            {
                result.ErrorMessage = "Ability on cooldown";
                return result;
            }
        }

        // Process ability based on type
        switch (abilityType.ToLower())
        {
            case "charge":
                result = ProcessChargeAbility(enhancedMob, world);
                break;
            case "heal":
                result = ProcessHealAbility(enhancedMob, world);
                break;
            case "roar":
                result = ProcessRoarAbility(enhancedMob, world);
                break;
            case "summon":
                result = ProcessSummonAbility(enhancedMob, world);
                break;
            case "mob_fireball":
                result = ProcessMobFireballAbility(enhancedMob, world);
                break;
            case "mob_aoe_blast":
                result = ProcessMobAoeBlastAbility(enhancedMob, world);
                break;
            case "mob_heal_ally":
                result = ProcessMobHealAllyAbility(enhancedMob, world);
                break;
            case "mob_heal_aoe":
                result = ProcessMobHealAoeAbility(enhancedMob, world);
                break;
            default:
                result.ErrorMessage = $"Unknown ability: {abilityType}";
                return result;
        }

        if (result.Success)
        {
            enhancedMob.AbilityCooldowns[abilityType] = DateTime.UtcNow;
            enhancedMob.AbilitiesUsed++;

            var stats = GetOrCreateWorldStats(world.WorldId);
            stats.AbilitiesUsed++;
        }

        return result;
    }

    // =============================================
    // DIFFICULTY AND SCALING
    // =============================================

    public void ScaleMobDifficulty(GameWorld world)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var mobs))
            return;

        var averagePlayerLevel = world.Players.Values.Any() ?
            world.Players.Values.Average(p => p.Level) : 1;

        var difficultyMultiplier = 1.0f + (averagePlayerLevel - 1) * 0.1f;
        difficultyMultiplier *= _aiSettings.DifficultyScaling;

        foreach (var mob in mobs.Where(m => m.Health > 0))
        {
            if (mob.Template != null)
            {
                if (!world.Rooms.TryGetValue(mob.RoomId, out var scalingRoom)) continue;
                var scaledStats = CalculateMobStats(mob.MobType, world, scalingRoom);
                mob.EnhancedStats = scaledStats;

                // Apply scaling to current health proportionally
                var healthRatio = (float)mob.Health / mob.MaxHealth;
                mob.MaxHealth = scaledStats.MaxHealth;
                mob.Health = (int)(mob.MaxHealth * healthRatio);
            }
        }
    }

    public MobStats CalculateMobStats(string mobType, GameWorld world, Room room)
    {
        if (!_mobTemplates.TryGetValue(mobType, out var template))
        {
            return new MobStats(); // Default stats
        }

        var stats = new MobStats
        {
            Health = template.BaseStats.Health,
            MaxHealth = template.BaseStats.MaxHealth,
            Damage = template.BaseStats.Damage,
            Speed = template.BaseStats.Speed,
            AttackRange = template.BaseStats.AttackRange,
            DetectionRange = template.BaseStats.DetectionRange,
            AttackCooldown = template.BaseStats.AttackCooldown,
            Armor = template.BaseStats.Armor,
            MagicResistance = template.BaseStats.MagicResistance,
            CriticalChance = template.BaseStats.CriticalChance,
            ExperienceReward = template.BaseStats.ExperienceReward
        };

        // Apply world-based scaling
        var worldAge = DateTime.UtcNow - world.CreatedAt;
        var timeMultiplier = 1.0f + (float)(worldAge.TotalMinutes / 60.0) * 0.1f; // 10% per hour

        // Apply player-based scaling
        var averagePlayerLevel = world.Players.Values.Any() ?
            world.Players.Values.Average(p => p.Level) : 1;
        var levelMultiplier = 1.0f + (averagePlayerLevel - 1) * 0.15f;

        // Apply difficulty setting
        var difficultyMultiplier = _aiSettings.DifficultyScaling;

        var totalMultiplier = timeMultiplier * levelMultiplier * difficultyMultiplier;

        // Apply game mode multipliers (solos = weaker mobs)
        var modeHealthMult = world.ModeConfig?.MobHealthMultiplier ?? 1.0f;
        var modeDamageMult = world.ModeConfig?.MobDamageMultiplier ?? 1.0f;

        // Scale stats
        stats.Health = (int)(stats.Health * totalMultiplier * modeHealthMult);
        stats.MaxHealth = (int)(stats.MaxHealth * totalMultiplier * modeHealthMult);
        stats.Damage = (int)(stats.Damage * totalMultiplier * modeDamageMult);
        stats.ExperienceReward = (int)(stats.ExperienceReward * totalMultiplier);

        return stats;
    }

    public float CalculateAggressionLevel(Mob mob, GameWorld world)
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
        var baseAggression = enhancedMob.Template?.BehaviorSettings.AggressionLevel ?? 1.0f;

        var modifiers = 1.0f;

        // Health-based modifier
        var healthRatio = (float)mob.Health / mob.MaxHealth;
        if (healthRatio < 0.3f)
            modifiers *= 1.5f; // More aggressive when low health

        // Group modifier
        if (!string.IsNullOrEmpty(enhancedMob.GroupId))
        {
            var groupSize = GetGroupSize(enhancedMob.GroupId, world.WorldId);
            modifiers *= 1.0f + (groupSize - 1) * 0.1f; // 10% per additional group member
        }

        // World completion modifier
        var completionRatio = world.Rooms.Values.Count(r => r.IsCompleted) / (float)world.Rooms.Count;
        modifiers *= 1.0f + completionRatio * 0.5f; // More aggressive as world progresses

        return baseAggression * modifiers * _aiSettings.GlobalAggressionMultiplier;
    }

    // =============================================
    // PERFORMANCE OPTIMIZATION
    // =============================================

    public void OptimizeAIProcessing(GameWorld world)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var mobs))
            return;

        for (int i = 0; i < mobs.Count; i++)
        {
            var mob = mobs[i];

            // Calculate distance to nearest player (manual loop avoids LINQ allocation)
            float nearestPlayerDistance = float.MaxValue;
            foreach (var player in world.Players.Values)
            {
                if (!player.IsAlive) continue;
                var dist = Vector2.Distance(player.Position, mob.Position);
                if (dist < nearestPlayerDistance)
                    nearestPlayerDistance = dist;
            }

            mob.DistanceToNearestPlayer = nearestPlayerDistance;

            // Set processing priority based on distance
            if (nearestPlayerDistance < 10.0f)
                mob.ProcessingPriority = AIProcessingPriority.Critical;
            else if (nearestPlayerDistance < 25.0f)
                mob.ProcessingPriority = AIProcessingPriority.High;
            else if (nearestPlayerDistance < 50.0f)
                mob.ProcessingPriority = AIProcessingPriority.Medium;
            else
                mob.ProcessingPriority = AIProcessingPriority.Low;

            // Determine if mob requires update
            mob.RequiresUpdate = nearestPlayerDistance < _aiSettings.OptimizationDistance ||
                                mob.CurrentState == MobState.Attacking ||
                                mob.CurrentState == MobState.Pursuing ||
                                mob.Template?.IsBoss == true;
        }
    }

    public void UpdateSpatialPartitioning(GameWorld world)
    {
        // Simple spatial partitioning - can be enhanced with quad-tree later
        var roomMobs = new Dictionary<string, List<EnhancedMob>>();

        if (_worldMobs.TryGetValue(world.WorldId, out var mobs))
        {
            foreach (var mob in mobs.Where(m => m.Health > 0))
            {
                if (!roomMobs.ContainsKey(mob.RoomId))
                    roomMobs[mob.RoomId] = new List<EnhancedMob>();

                roomMobs[mob.RoomId].Add(mob);
            }
        }

        // Store for quick room-based queries
        _lastUpdateTimes[world.WorldId] = DateTime.UtcNow;
    }

    public void ProcessAIByPriority(GameWorld world, float deltaTime)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var mobs))
            return;

        // Bucket mobs by priority (avoids GroupBy/OrderBy/ToList allocations)
        var priorityCritical = t_priorityCritical ??= new List<EnhancedMob>();
        var priorityHigh = t_priorityHigh ??= new List<EnhancedMob>();
        var priorityMedium = t_priorityMedium ??= new List<EnhancedMob>();
        var priorityLow = t_priorityLow ??= new List<EnhancedMob>();

        priorityCritical.Clear();
        priorityHigh.Clear();
        priorityMedium.Clear();
        priorityLow.Clear();

        for (int i = 0; i < mobs.Count; i++)
        {
            var mob = mobs[i];
            if (mob.Health <= 0) continue;

            switch (mob.ProcessingPriority)
            {
                case AIProcessingPriority.Critical: priorityCritical.Add(mob); break;
                case AIProcessingPriority.High: priorityHigh.Add(mob); break;
                case AIProcessingPriority.Medium: priorityMedium.Add(mob); break;
                case AIProcessingPriority.Low: priorityLow.Add(mob); break;
            }
        }

        // Process highest priority first, respecting budget
        ProcessPriorityBucket(priorityCritical, AIProcessingPriority.Critical, world, deltaTime);
        ProcessPriorityBucket(priorityHigh, AIProcessingPriority.High, world, deltaTime);
        ProcessPriorityBucket(priorityMedium, AIProcessingPriority.Medium, world, deltaTime);
        ProcessPriorityBucket(priorityLow, AIProcessingPriority.Low, world, deltaTime);
    }

    private void ProcessPriorityBucket(List<EnhancedMob> bucket, AIProcessingPriority priority, GameWorld world, float deltaTime)
    {
        var budget = GetProcessingBudget(priority);
        var count = Math.Min(bucket.Count, budget);
        for (int i = 0; i < count; i++)
        {
            UpdateMobBehavior(bucket[i], world, deltaTime);
        }
    }

    // =============================================
    // CLEANUP AND MANAGEMENT
    // =============================================

    public void CleanupWorldAI(string worldId)
    {
        _worldMobs.TryRemove(worldId, out _);
        _mobGroups.TryRemove(worldId, out _);
        _worldAIStats.TryRemove(worldId, out _);
        _worldNavMeshes.TryRemove(worldId, out _);
        _lastUpdateTimes.TryRemove(worldId, out _);
        _lastDynamicSpawn.TryRemove(worldId, out _);

        _logger.LogInformation("Cleaned up AI data for world {WorldId}", worldId);
    }

    public void CleanupInvalidMobs(GameWorld world)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var mobs))
            return;

        var tempMobBuffer = t_tempMobBuffer ??= new List<EnhancedMob>(32);
        tempMobBuffer.Clear();
        for (int i = 0; i < mobs.Count; i++)
        {
            var mob = mobs[i];
            if (mob.Health <= 0 && (DateTime.UtcNow - mob.StateChangedAt).TotalMinutes > 5)
                tempMobBuffer.Add(mob);
        }

        for (int i = 0; i < tempMobBuffer.Count; i++)
        {
            var mob = tempMobBuffer[i];
            mobs.Remove(mob);
            world.Mobs.TryRemove(mob.MobId, out _);

            if (!string.IsNullOrEmpty(mob.GroupId))
            {
                RemoveFromGroup(mob, world.WorldId);
            }
        }

        if (tempMobBuffer.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} invalid mobs from world {WorldId}",
                tempMobBuffer.Count, world.WorldId);
        }
    }

    public void OptimizeMemoryUsage()
    {
        var tempExpiredKeys = t_tempExpiredKeys ??= new List<string>(8);

        foreach (var (worldId, mobs) in _worldMobs)
        {
            // Clean up old tracking data
            foreach (var mob in mobs)
            {
                // Clear old position history
                if (mob.RecentPositions?.Count > 10)
                {
                    mob.RecentPositions = mob.RecentPositions.GetRange(
                        mob.RecentPositions.Count - 5, 5);
                }

                // Clear old path data
                if ((DateTime.UtcNow - mob.PathCalculatedAt).TotalMinutes > 1)
                {
                    mob.CurrentPath.Clear();
                    mob.CurrentPathIndex = 0;
                }

                // Clear old ability cooldowns
                foreach (var kvp in mob.AbilityCooldowns)
                {
                    if ((DateTime.UtcNow - kvp.Value).TotalMinutes > 10)
                        tempExpiredKeys.Add(kvp.Key);
                }

                foreach (var key in tempExpiredKeys)
                {
                    mob.AbilityCooldowns.Remove(key);
                }
                tempExpiredKeys.Clear();
            }
        }

        _logger.LogDebug("Optimized AI memory usage");
    }

    // =============================================
    // STATISTICS AND ANALYTICS
    // =============================================

    public AIStats GetAIStats(string worldId)
    {
        return GetOrCreateWorldStats(worldId);
    }

    public Dictionary<string, object> GetDetailedAIAnalytics()
    {
        var totalMobs = _worldMobs.Values.Sum(mobs => mobs.Count);
        var aliveMobs = _worldMobs.Values.Sum(mobs => mobs.Count(m => m.Health > 0));
        var totalAttacks = _worldAIStats.Values.Sum(stats => stats.PlayerKills);
        var totalAbilities = _worldAIStats.Values.Sum(stats => stats.AbilitiesUsed);

        return new Dictionary<string, object>
        {
            ["TotalMobs"] = totalMobs,
            ["AliveMobs"] = aliveMobs,
            ["DeadMobs"] = totalMobs - aliveMobs,
            ["TotalAttacks"] = totalAttacks,
            ["TotalAbilities"] = totalAbilities,
            ["WorldCount"] = _worldMobs.Count,
            ["AverageAggressionLevel"] = _worldAIStats.Values.Any() ?
                _worldAIStats.Values.Average(s => s.AverageAggressionLevel) : 0,
            ["MobsByType"] = _worldAIStats.Values
                .SelectMany(s => s.MobsByType)
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(g => g.Key, g => g.Sum(kvp => kvp.Value)),
            ["MobsByState"] = _worldAIStats.Values
                .SelectMany(s => s.MobsByState)
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(g => g.Key.ToString(), g => g.Sum(kvp => kvp.Value)),
            ["ProcessingEfficiency"] = CalculateProcessingEfficiency(),
            ["CurrentAISettings"] = _aiSettings
        };
    }

    public Dictionary<string, object> GetMobDistributionStats(string worldId)
    {
        if (!_worldMobs.TryGetValue(worldId, out var mobs))
            return new Dictionary<string, object>();

        var aliveMobs = mobs.Where(m => m.Health > 0).ToList();

        return new Dictionary<string, object>
        {
            ["TotalMobs"] = aliveMobs.Count,
            ["MobsByRoom"] = aliveMobs.GroupBy(m => m.RoomId)
                .ToDictionary(g => g.Key, g => g.Count()),
            ["MobsByType"] = aliveMobs.GroupBy(m => m.MobType)
                .ToDictionary(g => g.Key, g => g.Count()),
            ["MobsByState"] = aliveMobs.GroupBy(m => m.CurrentState)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            ["MobsByPriority"] = aliveMobs.GroupBy(m => m.ProcessingPriority)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            ["GroupedMobs"] = _mobGroups.TryGetValue(worldId, out var groups) ?
                groups.Values.Sum(group => group.Count) : 0,
            ["BossCount"] = aliveMobs.Count(m => m.Template?.IsBoss == true),
            ["AverageAggressionLevel"] = aliveMobs.Any() ?
                aliveMobs.Average(m => m.AggressionLevel) : 0
        };
    }

    // =============================================
    // CONFIGURATION
    // =============================================

    public void UpdateAISettings(AISettings newSettings)
    {
        _aiSettings = newSettings;
        _logger.LogInformation("AI settings updated");
    }

    public AISettings GetCurrentAISettings()
    {
        return _aiSettings;
    }

    public void UpdateMobTemplate(string mobType, MobTemplate template)
    {
        _mobTemplates[mobType] = template;

        // Update existing mobs of this type
        foreach (var (worldId, mobs) in _worldMobs)
        {
            var affectedMobs = mobs.Where(m => m.MobType == mobType).ToList();
            foreach (var mob in affectedMobs)
            {
                mob.Template = template;
                // Recalculate stats if needed
                if (_worldMobs.TryGetValue(worldId, out var worldMobList))
                {
                    var world = GetWorldById(worldId); // Would need world reference
                    if (world != null && world.Rooms.TryGetValue(mob.RoomId, out var room))
                    {
                        mob.EnhancedStats = CalculateMobStats(mobType, world, room);
                    }
                }
            }
        }

        _logger.LogInformation("Updated mob template for type {MobType}", mobType);
    }

    // =============================================
    // PRIVATE HELPER METHODS
    // =============================================

    private void InitializeDefaultTemplates()
    {
        // Guard template
        _mobTemplates["guard"] = new MobTemplate
        {
            MobType = "guard",
            DisplayName = "Guard",
            BaseStats = new MobStats
            {
                Health = 75,
                MaxHealth = 75,
                Damage = 25,
                Speed = 1.5f,
                AttackRange = 3.0f,
                DetectionRange = 12.0f,
                AttackCooldown = 2.5f,
                Armor = 5,
                ExperienceReward = 75
            },
            Abilities = new List<string> { "roar" },
            BehaviorSettings = new AIBehaviorSettings
            {
                PatrolRadius = 15.0f,
                AggressionLevel = 1.2f,
                FleeThreshold = 0.15f,
                CanCallForHelp = true,
                HelpCallRadius = 20.0f,
                PrefersMelee = true,
                AbilityCooldown = 15.0f
            },
            SpawnWeight = 1.0f,
            IsBoss = false,
            IsElite = false
        };

        // Patrol template
        _mobTemplates["patrol"] = new MobTemplate
        {
            MobType = "patrol",
            DisplayName = "Patrol",
            BaseStats = new MobStats
            {
                Health = 50,
                MaxHealth = 50,
                Damage = 15,
                Speed = 2.0f,
                AttackRange = 2.5f,
                DetectionRange = 8.0f,
                AttackCooldown = 2.0f,
                ExperienceReward = 50
            },
            Abilities = new List<string>(),
            BehaviorSettings = new AIBehaviorSettings
            {
                PatrolRadius = 25.0f,
                AggressionLevel = 1.0f,
                FleeThreshold = 0.2f,
                CanCallForHelp = true,
                HelpCallRadius = 15.0f,
                PrefersMelee = true,
                AbilityCooldown = 20.0f
            },
            SpawnWeight = 1.5f,
            IsBoss = false,
            IsElite = false
        };

        // Elite template
        _mobTemplates["elite"] = new MobTemplate
        {
            MobType = "elite",
            DisplayName = "Elite Warrior",
            BaseStats = new MobStats
            {
                Health = 150,
                MaxHealth = 150,
                Damage = 40,
                Speed = 1.8f,
                AttackRange = 4.0f,
                DetectionRange = 15.0f,
                AttackCooldown = 3.0f,
                Armor = 10,
                MagicResistance = 5,
                CriticalChance = 0.15f,
                ExperienceReward = 200
            },
            Abilities = new List<string> { "charge", "roar" },
            BehaviorSettings = new AIBehaviorSettings
            {
                PatrolRadius = 20.0f,
                AggressionLevel = 1.5f,
                FleeThreshold = 0.1f,
                CanCallForHelp = true,
                HelpCallRadius = 25.0f,
                PrefersMelee = true,
                AbilityCooldown = 12.0f,
                PreferredTargets = new List<string> { "tank", "support" }
            },
            SpawnWeight = 0.3f,
            IsBoss = false,
            IsElite = true
        };

        // Boss template
        _mobTemplates["boss"] = new MobTemplate
        {
            MobType = "boss",
            DisplayName = "Boss",
            BaseStats = new MobStats
            {
                Health = 500,
                MaxHealth = 500,
                Damage = 75,
                Speed = 1.2f,
                AttackRange = 5.0f,
                DetectionRange = 20.0f,
                AttackCooldown = 4.0f,
                Armor = 20,
                MagicResistance = 15,
                CriticalChance = 0.25f,
                ExperienceReward = 1000
            },
            Abilities = new List<string> { "charge", "roar", "summon", "heal" },
            BehaviorSettings = new AIBehaviorSettings
            {
                PatrolRadius = 30.0f,
                AggressionLevel = 2.0f,
                FleeThreshold = 0.05f,
                CanCallForHelp = true,
                HelpCallRadius = 50.0f,
                PrefersMelee = false,
                AbilityCooldown = 8.0f
            },
            SpawnWeight = 0.1f,
            IsBoss = true,
            IsElite = true
        };

        // Archer template — ranged, kites, fast
        _mobTemplates["archer"] = new MobTemplate
        {
            MobType = "archer",
            DisplayName = "Archer",
            BaseStats = new MobStats
            {
                Health = 40,
                MaxHealth = 40,
                Damage = 18,
                Speed = 2.2f,
                AttackRange = 10.0f,
                DetectionRange = 14.0f,
                AttackCooldown = 2.0f,
                ExperienceReward = 60
            },
            Abilities = new List<string>(),
            BehaviorSettings = new AIBehaviorSettings
            {
                PatrolRadius = 20.0f,
                AggressionLevel = 1.0f,
                FleeThreshold = 0.30f,
                CanCallForHelp = true,
                HelpCallRadius = 15.0f,
                PrefersMelee = false,
                AbilityCooldown = 20.0f
            },
            SpawnWeight = 1.2f,
            IsBoss = false,
            IsElite = false
        };

        // Caster template — ranged spells, fragile
        _mobTemplates["caster"] = new MobTemplate
        {
            MobType = "caster",
            DisplayName = "Dark Caster",
            BaseStats = new MobStats
            {
                Health = 35,
                MaxHealth = 35,
                Damage = 30,
                Speed = 1.5f,
                AttackRange = 8.0f,
                DetectionRange = 12.0f,
                AttackCooldown = 3.0f,
                MagicResistance = 8,
                ExperienceReward = 80
            },
            Abilities = new List<string> { "mob_fireball", "mob_aoe_blast" },
            BehaviorSettings = new AIBehaviorSettings
            {
                PatrolRadius = 15.0f,
                AggressionLevel = 1.1f,
                FleeThreshold = 0.25f,
                CanCallForHelp = true,
                HelpCallRadius = 18.0f,
                PrefersMelee = false,
                AbilityCooldown = 6.0f
            },
            SpawnWeight = 0.8f,
            IsBoss = false,
            IsElite = false
        };

        // Healer template — supports allies, low damage
        _mobTemplates["healer"] = new MobTemplate
        {
            MobType = "healer",
            DisplayName = "Shaman",
            BaseStats = new MobStats
            {
                Health = 45,
                MaxHealth = 45,
                Damage = 6,
                Speed = 1.8f,
                AttackRange = 8.0f,
                DetectionRange = 12.0f,
                AttackCooldown = 2.5f,
                MagicResistance = 10,
                ExperienceReward = 90
            },
            Abilities = new List<string> { "mob_heal_ally", "mob_heal_aoe" },
            BehaviorSettings = new AIBehaviorSettings
            {
                PatrolRadius = 12.0f,
                AggressionLevel = 0.6f,
                FleeThreshold = 0.35f,
                CanCallForHelp = true,
                HelpCallRadius = 20.0f,
                PrefersMelee = false,
                AbilityCooldown = 5.0f
            },
            SpawnWeight = 0.6f,
            IsBoss = false,
            IsElite = false
        };
    }

    private AIStats GetOrCreateWorldStats(string worldId)
    {
        return _worldAIStats.GetOrAdd(worldId, _ => new AIStats
        {
            LastStatsReset = DateTime.UtcNow,
            MobsByType = new Dictionary<string, int>(),
            MobsByState = new Dictionary<MobState, int>(),
            MobsByRoom = new Dictionary<string, int>()
        });
    }

    private bool IsExtractionRoom(string roomId, GameWorld world)
    {
        return world.ExtractionPoints.Values.Any(ep => ep.RoomId == roomId);
    }

    private bool IsSpawnRoom(Room room)
    {
        return room.RoomType == "spawn";
    }

    private int CalculateMobCountForRoom(Room room, GameWorld world)
    {
        return room.RoomType switch
        {
            "empty" or "spawn" => 0,
            "patrol" => Random.Shared.Next(2, 4),           // 2-3
            "guard_post" => Random.Shared.Next(3, 5),        // 3-4
            "ambush" => Random.Shared.Next(4, 7),            // 4-6
            "elite_chamber" => Random.Shared.Next(3, 6),     // 3-5
            "boss_arena" => Random.Shared.Next(3, 5),        // 3-4 (boss + guards)
            "treasure_vault" => Random.Shared.Next(3, 5),    // 3-4
            _ => Random.Shared.Next(2, 4)
        };
    }

    private string SelectMobTypeForRoom(Room room, GameWorld world)
    {
        // Room type determines mob composition (boss/elite guaranteed as mob[0] in SpawnInitialMobs)
        var roll = Random.Shared.NextDouble();
        return room.RoomType switch
        {
            "patrol" => roll < 0.45 ? "patrol" : roll < 0.75 ? "archer" : "caster",
            "guard_post" => roll < 0.35 ? "guard" : roll < 0.55 ? "archer" : roll < 0.75 ? "patrol" : roll < 0.90 ? "healer" : "elite",
            "ambush" => roll < 0.25 ? "archer" : roll < 0.45 ? "caster" : roll < 0.60 ? "patrol" : roll < 0.75 ? "guard" : roll < 0.90 ? "elite" : "healer",
            "elite_chamber" => roll < 0.35 ? "elite" : roll < 0.50 ? "caster" : roll < 0.65 ? "archer" : roll < 0.80 ? "healer" : "guard",
            "boss_arena" => roll < 0.20 ? "elite" : roll < 0.40 ? "healer" : roll < 0.60 ? "caster" : roll < 0.80 ? "guard" : "archer",
            "treasure_vault" => roll < 0.25 ? "guard" : roll < 0.45 ? "archer" : roll < 0.65 ? "elite" : roll < 0.80 ? "caster" : "healer",
            _ => roll < 0.45 ? "patrol" : roll < 0.75 ? "archer" : "caster"
        };
    }

    private Vector2 CalculateSpawnPosition(Room room)
    {
        var margin = 10.0f;
        var x = room.Position.X + Random.Shared.Next((int)-room.Size.X / 2 + (int)margin, (int)room.Size.X / 2 - (int)margin);
        var y = room.Position.Y + Random.Shared.Next((int)-room.Size.Y / 2 + (int)margin, (int)room.Size.Y / 2 - (int)margin);
        return new Vector2(x, y);
    }

    private Vector2 GenerateRandomOffset(float radius)
    {
        var angle = Random.Shared.NextDouble() * Math.PI * 2;
        var distance = Random.Shared.NextDouble() * radius;
        return new Vector2(
            (float)(Math.Cos(angle) * distance),
            (float)(Math.Sin(angle) * distance)
        );
    }

    private void CreateNavigationMesh(GameWorld world)
    {
        var navMesh = new NavigationMesh();

        foreach (var room in world.Rooms.Values)
        {
            var nodes = new List<PathfindingNode>();
            var nodeSpacing = navMesh.NodeSpacing;

            for (float x = room.Position.X - room.Size.X / 2; x <= room.Position.X + room.Size.X / 2; x += nodeSpacing)
            {
                for (float y = room.Position.Y - room.Size.Y / 2; y <= room.Position.Y + room.Size.Y / 2; y += nodeSpacing)
                {
                    nodes.Add(new PathfindingNode
                    {
                        Position = new Vector2(x, y),
                        IsWalkable = true // Can be enhanced with obstacle detection
                    });
                }
            }

            navMesh.RoomNodes[room.RoomId] = nodes;
            navMesh.RoomConnections[room.RoomId] = room.Connections
                .Where(c => world.Rooms.ContainsKey(c))
                .Select(c => world.Rooms[c].Position).ToList();
        }

        _worldNavMeshes[world.WorldId] = navMesh;
    }

    private void AssignToGroup(EnhancedMob mob, GameWorld world)
    {
        if (!_mobGroups.ContainsKey(world.WorldId))
            _mobGroups[world.WorldId] = new Dictionary<string, List<EnhancedMob>>();

        // Find existing group in same room
        var existingGroup = _mobGroups[world.WorldId].Values
            .FirstOrDefault(group => group.Any(m => m.RoomId == mob.RoomId && group.Count < 4));

        if (existingGroup != null)
        {
            mob.GroupId = existingGroup.First().GroupId;
            existingGroup.Add(mob);
        }
        else
        {
            // Create new group
            var groupId = Guid.NewGuid().ToString()[..8];
            mob.GroupId = groupId;
            mob.IsGroupLeader = true;
            _mobGroups[world.WorldId][groupId] = new List<EnhancedMob> { mob };
        }
    }

    private void RemoveFromGroup(EnhancedMob mob, string worldId)
    {
        if (string.IsNullOrEmpty(mob.GroupId) || !_mobGroups.TryGetValue(worldId, out var groups))
            return;

        if (groups.TryGetValue(mob.GroupId, out var group))
        {
            group.Remove(mob);

            if (group.Count == 0)
            {
                groups.Remove(mob.GroupId);
            }
            else if (mob.IsGroupLeader && group.Any())
            {
                group.First().IsGroupLeader = true;
            }
        }

        mob.GroupId = null;
        mob.IsGroupLeader = false;
    }

    private int GetGroupSize(string groupId, string worldId)
    {
        if (_mobGroups.TryGetValue(worldId, out var groups) &&
            groups.TryGetValue(groupId, out var group))
        {
            return group.Count(m => m.Health > 0);
        }
        return 0;
    }

    private EnhancedMob? ConvertToEnhancedMob(Mob baseMob)
    {
        if (baseMob == null) return null;
        if (baseMob is EnhancedMob enhanced)
            return enhanced;

        // Convert base mob to enhanced mob
        var enhancedMob = new EnhancedMob
        {
            MobId = baseMob.MobId,
            MobType = baseMob.MobType,
            Position = baseMob.Position,
            Health = baseMob.Health,
            MaxHealth = baseMob.MaxHealth,
            RoomId = baseMob.RoomId,
            PatrolTarget = baseMob.PatrolTarget,
            State = baseMob.State,
            LastStateChange = baseMob.LastStateChange,
            IsDirty = baseMob.IsDirty,
            OriginalPosition = baseMob.Position,
            CurrentState = Enum.TryParse<MobState>(baseMob.State, true, out var state) ? state : MobState.Idle,
            StateChangedAt = baseMob.LastStateChange,
            RequiresUpdate = true
        };

        // Try to find template
        if (_mobTemplates.TryGetValue(baseMob.MobType, out var template))
        {
            enhancedMob.Template = template;
            enhancedMob.EnhancedStats = template.BaseStats;
        }

        return enhancedMob;
    }

    /// <summary>
    /// Check if a mob can see/attack a player. Same room = always yes.
    /// Adjacent room = only if the door between them is open.
    /// </summary>
    private static bool HasLineOfSight(Mob mob, RealTimePlayer player, GameWorld world)
    {
        if (mob.RoomId == player.CurrentRoomId) return true;

        // Check if player's room is adjacent and door is open
        if (!world.Rooms.TryGetValue(mob.RoomId, out var mobRoom)) return false;
        if (!mobRoom.Connections.Contains(player.CurrentRoomId)) return false;

        var doorId = string.Compare(mob.RoomId, player.CurrentRoomId, StringComparison.Ordinal) < 0
            ? $"{mob.RoomId}_{player.CurrentRoomId}" : $"{player.CurrentRoomId}_{mob.RoomId}";

        // No door entry = open corridor (no door exists), allow LOS
        if (!world.Doors.TryGetValue(doorId, out var door)) return true;

        return door.IsOpen;
    }

    private AIDecisionContext CreateDecisionContext(EnhancedMob mob, GameWorld world, float deltaTime)
    {
        var detectionRange = mob.EnhancedStats.DetectionRange;
        var nearbyPlayers = new List<RealTimePlayer>();
        foreach (var p in world.Players.Values)
        {
            if (p.IsAlive && Vector2.Distance(p.Position, mob.Position) <= detectionRange
                && HasLineOfSight(mob, p, world))
                nearbyPlayers.Add(p);
        }

        var nearbyMobs = new List<Mob>();
        if (_worldMobs.TryGetValue(world.WorldId, out var mobs))
        {
            for (int i = 0; i < mobs.Count; i++)
            {
                var m = mobs[i];
                if (m.MobId != mob.MobId && m.Health > 0 &&
                    Vector2.Distance(m.Position, mob.Position) <= 20.0f)
                    nearbyMobs.Add(m);
            }
        }

        return new AIDecisionContext
        {
            Mob = mob,
            World = world,
            NearbyPlayers = nearbyPlayers,
            NearbyMobs = nearbyMobs,
            CurrentRoom = world.Rooms.TryGetValue(mob.RoomId, out var mobRoom) ? mobRoom : null!,
            DeltaTime = deltaTime,
            ContextData = new Dictionary<string, object>
            {
                ["DistanceToNearestPlayer"] = mob.DistanceToNearestPlayer,
                ["HealthPercentage"] = (float)mob.Health / mob.MaxHealth,
                ["AggressionLevel"] = mob.AggressionLevel,
                ["GroupSize"] = GetGroupSize(mob.GroupId ?? "", world.WorldId)
            }
        };
    }

    private AIDecision MakeAIDecision(AIDecisionContext context)
    {
        var mob = context.Mob as EnhancedMob;
        var decisions = new List<AIDecision>();

        // Generate possible decisions based on current state
        switch (mob.CurrentState)
        {
            case MobState.Idle:
            case MobState.Patrol:
                decisions.AddRange(GeneratePatrolDecisions(context));
                break;

            case MobState.Alert:
                decisions.AddRange(GenerateAlertDecisions(context));
                break;

            case MobState.Pursuing:
                decisions.AddRange(GeneratePursuitDecisions(context));
                break;

            case MobState.Attacking:
                decisions.AddRange(GenerateAttackDecisions(context));
                break;

            case MobState.Fleeing:
                decisions.AddRange(GenerateFleeDecisions(context));
                break;

            case MobState.Casting:
                // Boss is telegraphing — stay still, don't move or attack
                return new AIDecision { Action = MobAction.Guard, Priority = 100f, Reason = "Casting/telegraph in progress" };
        }

        // Add context-based decisions
        if (context.NearbyPlayers.Any())
        {
            decisions.AddRange(GeneratePlayerInteractionDecisions(context));
        }

        // Return highest priority decision
        return decisions.OrderByDescending(d => d.Priority).FirstOrDefault() ??
               new AIDecision { Action = MobAction.Patrol, Priority = 0.1f, Reason = "Default action" };
    }

    private List<AIDecision> GeneratePatrolDecisions(AIDecisionContext context)
    {
        var decisions = new List<AIDecision>();
        var mob = context.Mob as EnhancedMob;

        // Check for nearby players
        if (context.NearbyPlayers.Any())
        {
            var nearestPlayer = context.NearbyPlayers
                .OrderBy(p => Vector2.Distance(p.Position, mob.Position))
                .First();

            decisions.Add(new AIDecision
            {
                Action = MobAction.Search,
                Priority = 0.7f,
                TargetPosition = nearestPlayer.Position,
                TargetId = nearestPlayer.PlayerId,
                Reason = "Player detected during patrol"
            });
        }

        // Continue patrol
        var distanceToPatrolTarget = Vector2.Distance(mob.Position, mob.PatrolTarget);
        if (distanceToPatrolTarget < 2.0f)
        {
            // Generate new patrol target
            var newTarget = mob.OriginalPosition + GenerateRandomOffset(mob.Template?.BehaviorSettings.PatrolRadius ?? 20.0f);
            decisions.Add(new AIDecision
            {
                Action = MobAction.Patrol,
                Priority = 0.3f,
                TargetPosition = newTarget,
                Reason = "Reached patrol point, selecting new target"
            });
        }
        else
        {
            decisions.Add(new AIDecision
            {
                Action = MobAction.Move,
                Priority = 0.2f,
                TargetPosition = mob.PatrolTarget,
                Reason = "Moving to patrol target"
            });
        }

        return decisions;
    }

    private List<AIDecision> GenerateAlertDecisions(AIDecisionContext context)
    {
        var decisions = new List<AIDecision>();
        var mob = context.Mob as EnhancedMob;

        if (context.NearbyPlayers.Any())
        {
            var target = context.NearbyPlayers.First();
            var distance = Vector2.Distance(mob.Position, target.Position);

            if (distance <= mob.EnhancedStats.AttackRange)
            {
                decisions.Add(new AIDecision
                {
                    Action = MobAction.Attack,
                    Priority = 0.9f,
                    TargetId = target.PlayerId,
                    Reason = "Player in attack range"
                });
            }
            else
            {
                decisions.Add(new AIDecision
                {
                    Action = MobAction.Move,
                    Priority = 0.8f,
                    TargetPosition = target.Position,
                    TargetId = target.PlayerId,
                    Reason = "Pursuing detected player"
                });
            }
        }
        else
        {
            // No players visible, return to patrol
            decisions.Add(new AIDecision
            {
                Action = MobAction.Patrol,
                Priority = 0.3f,
                Reason = "No players detected, returning to patrol"
            });
        }

        return decisions;
    }

    private const float LeashDistance = 18f; // Max distance from spawn before returning (~1 room)

    private List<AIDecision> GeneratePursuitDecisions(AIDecisionContext context)
    {
        var decisions = new List<AIDecision>();
        var mob = context.Mob as EnhancedMob;

        // Leash check: if mob is too far from spawn, return home (full heal on arrival, not gradual)
        var distFromSpawn = Vector2.Distance(mob.Position, mob.OriginalPosition);
        if (distFromSpawn > LeashDistance)
        {
            mob.TargetPlayerId = null;
            mob.LastKnownPlayerPosition = mob.OriginalPosition;
            mob.IsDirty = true;
            decisions.Add(new AIDecision
            {
                Action = MobAction.Move,
                Priority = 1.0f,
                TargetPosition = mob.OriginalPosition,
                Reason = "Leashing back to spawn"
            });
            return decisions;
        }

        // Full heal when mob arrives back at spawn after leashing
        if (string.IsNullOrEmpty(mob.TargetPlayerId) && distFromSpawn < 2.0f && mob.Health < mob.MaxHealth)
        {
            mob.Health = mob.MaxHealth;
            mob.IsDirty = true;
        }

        if (!string.IsNullOrEmpty(mob.TargetPlayerId))
        {
            var target = context.World.Players.Values.FirstOrDefault(p => p.PlayerId == mob.TargetPlayerId);
            if (target != null && target.IsAlive)
            {
                var distance = Vector2.Distance(mob.Position, target.Position);
                var isRanged = mob.Template?.BehaviorSettings.PrefersMelee == false;

                // Healer priority: heal wounded allies instead of chasing
                if (isRanged && mob.Template?.Abilities.Any(a => a.StartsWith("mob_heal")) == true)
                {
                    var healDecision = TryGenerateHealDecision(mob, context.World);
                    if (healDecision != null)
                    {
                        decisions.Add(healDecision);
                    }
                }

                if (isRanged && distance < mob.EnhancedStats.AttackRange * 0.5f)
                {
                    // Too close — kite away from target
                    var kiteDir = (mob.Position - target.Position).GetNormalized();
                    var kiteTarget = mob.Position + kiteDir * mob.EnhancedStats.AttackRange * 0.6f;
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Move,
                        Priority = 1.0f,
                        TargetPosition = kiteTarget,
                        TargetId = target.PlayerId,
                        Reason = "Kiting away from target"
                    });
                }
                else if (distance <= mob.EnhancedStats.AttackRange)
                {
                    decisions.Add(new AIDecision
                    {
                        Action = isRanged ? MobAction.RangedAttack : MobAction.Attack,
                        Priority = 1.0f,
                        TargetId = target.PlayerId,
                        Reason = isRanged ? "Target in ranged attack range" : "Target in attack range"
                    });
                }
                else if (distance <= mob.EnhancedStats.DetectionRange)
                {
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Move,
                        Priority = 0.9f,
                        TargetPosition = target.Position,
                        TargetId = target.PlayerId,
                        Reason = "Continuing pursuit"
                    });
                }
                else
                {
                    // Lost target — return to spawn
                    mob.TargetPlayerId = null;
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Move,
                        Priority = 0.5f,
                        TargetPosition = mob.OriginalPosition,
                        Reason = "Lost target, returning to patrol area"
                    });
                }
            }
        }

        return decisions;
    }

    private List<AIDecision> GenerateAttackDecisions(AIDecisionContext context)
    {
        var decisions = new List<AIDecision>();
        var mob = context.Mob as EnhancedMob;

        // Leash check: if too far from spawn, drop target and return (full heal on arrival)
        var distFromSpawn = Vector2.Distance(mob.Position, mob.OriginalPosition);
        if (distFromSpawn > LeashDistance)
        {
            mob.TargetPlayerId = null;
            mob.LastKnownPlayerPosition = mob.OriginalPosition;
            mob.IsDirty = true;
            decisions.Add(new AIDecision
            {
                Action = MobAction.Move,
                Priority = 1.0f,
                TargetPosition = mob.OriginalPosition,
                Reason = "Leashing back to spawn"
            });
            return decisions;
        }

        // Full heal when mob arrives back at spawn after leashing
        if (string.IsNullOrEmpty(mob.TargetPlayerId) && distFromSpawn < 2.0f && mob.Health < mob.MaxHealth)
        {
            mob.Health = mob.MaxHealth;
            mob.IsDirty = true;
        }

        if (!string.IsNullOrEmpty(mob.TargetPlayerId))
        {
            var target = context.World.Players.Values.FirstOrDefault(p => p.PlayerId == mob.TargetPlayerId);
            if (target != null && target.IsAlive)
            {
                var distance = Vector2.Distance(mob.Position, target.Position);
                var isRanged = mob.Template?.BehaviorSettings.PrefersMelee == false;

                // Healer priority: heal wounded allies over attacking
                if (isRanged && mob.Template?.Abilities.Any(a => a.StartsWith("mob_heal")) == true)
                {
                    var healDecision = TryGenerateHealDecision(mob, context.World);
                    if (healDecision != null)
                    {
                        decisions.Add(healDecision);
                    }
                }

                // Ranged kiting: move away if target is too close
                if (isRanged && distance < mob.EnhancedStats.AttackRange * 0.5f)
                {
                    var kiteDir = (mob.Position - target.Position).GetNormalized();
                    var kiteTarget = mob.Position + kiteDir * mob.EnhancedStats.AttackRange * 0.6f;
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Move,
                        Priority = 1.1f, // Higher than attack to prioritize kiting
                        TargetPosition = kiteTarget,
                        TargetId = target.PlayerId,
                        Reason = "Kiting away from target"
                    });
                }

                if (distance <= mob.EnhancedStats.AttackRange)
                {
                    // Continue attacking (ranged or melee)
                    decisions.Add(new AIDecision
                    {
                        Action = isRanged ? MobAction.RangedAttack : MobAction.Attack,
                        Priority = 1.0f,
                        TargetId = target.PlayerId,
                        Reason = isRanged ? "Ranged attack on target" : "Continuing attack on target"
                    });

                    // Consider using abilities
                    if (mob.Template?.Abilities.Any() == true)
                    {
                        foreach (var ability in mob.Template.Abilities)
                        {
                            if (mob.AbilityCooldowns.ContainsKey(ability) &&
                                (DateTime.UtcNow - mob.AbilityCooldowns[ability]).TotalSeconds <
                                mob.Template.BehaviorSettings.AbilityCooldown)
                                continue;

                            decisions.Add(new AIDecision
                            {
                                Action = MobAction.Cast,
                                Priority = isRanged ? 1.05f : 0.8f, // Ranged mobs prefer abilities
                                TargetId = target.PlayerId,
                                Reason = $"Using ability: {ability}",
                                Parameters = new Dictionary<string, object> { ["AbilityType"] = ability }
                            });
                        }
                    }
                }
                else
                {
                    // Target moved out of range, pursue
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Move,
                        Priority = 0.9f,
                        TargetPosition = target.Position,
                        TargetId = target.PlayerId,
                        Reason = "Target moved out of attack range"
                    });
                }
            }
        }

        // Check health for flee decision
        var healthPercentage = (float)mob.Health / mob.MaxHealth;
        var fleeThreshold = mob.Template?.BehaviorSettings.FleeThreshold ?? 0.2f;

        if (healthPercentage <= fleeThreshold)
        {
            decisions.Add(new AIDecision
            {
                Action = MobAction.Flee,
                Priority = 1.2f, // Higher than attack
                Reason = "Health too low, fleeing"
            });
        }

        return decisions;
    }

    private List<AIDecision> GenerateFleeDecisions(AIDecisionContext context)
    {
        var decisions = new List<AIDecision>();
        var mob = context.Mob as EnhancedMob;

        // Find safe position away from players
        var fleeDirection = Vector2.Zero;
        foreach (var player in context.NearbyPlayers)
        {
            var directionFromPlayer = (mob.Position - player.Position).GetNormalized();
            fleeDirection += directionFromPlayer;
        }

        if (fleeDirection != Vector2.Zero)
        {
            fleeDirection = fleeDirection.GetNormalized();
            var fleeTarget = mob.Position + fleeDirection * 30.0f; // Flee 30 units away

            decisions.Add(new AIDecision
            {
                Action = MobAction.Flee,
                Priority = 1.0f,
                TargetPosition = fleeTarget,
                Reason = "Fleeing from players"
            });
        }

        // Check if health recovered enough to stop fleeing
        var healthPercentage = (float)mob.Health / mob.MaxHealth;
        if (healthPercentage > 0.5f && !context.NearbyPlayers.Any())
        {
            decisions.Add(new AIDecision
            {
                Action = MobAction.Patrol,
                Priority = 0.6f,
                Reason = "Health recovered, returning to patrol"
            });
        }

        return decisions;
    }

    private List<AIDecision> GeneratePlayerInteractionDecisions(AIDecisionContext context)
    {
        var decisions = new List<AIDecision>();
        var mob = context.Mob as EnhancedMob;

        var nearestPlayer = context.NearbyPlayers
            .OrderBy(p => Vector2.Distance(p.Position, mob.Position))
            .First();

        var distance = Vector2.Distance(mob.Position, nearestPlayer.Position);

        // Call for help if mob can and is in danger
        if (mob.Template?.BehaviorSettings.CanCallForHelp == true &&
            (DateTime.UtcNow - mob.LastHelpCall).TotalSeconds > 30.0f &&
            distance <= mob.EnhancedStats.AttackRange * 2)
        {
            decisions.Add(new AIDecision
            {
                Action = MobAction.Roar,
                Priority = 0.7f,
                Reason = "Calling for help",
                Parameters = new Dictionary<string, object> { ["HelpCall"] = true }
            });
        }

        return decisions;
    }

    private void ExecuteAIDecision(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        switch (decision.Action)
        {
            case MobAction.Move:
                ExecuteMoveAction(mob, decision, world, result);
                break;

            case MobAction.Attack:
                ExecuteAttackAction(mob, decision, world, result);
                break;

            case MobAction.Patrol:
                ExecutePatrolAction(mob, decision, world, result);
                break;

            case MobAction.Search:
                ExecuteSearchAction(mob, decision, world, result);
                break;

            case MobAction.Flee:
                ExecuteFleeAction(mob, decision, world, result);
                break;

            case MobAction.Cast:
                ExecuteCastAction(mob, decision, world, result);
                break;

            case MobAction.Roar:
                ExecuteRoarAction(mob, decision, world, result);
                break;

            case MobAction.RangedAttack:
                ExecuteRangedAttackAction(mob, decision, world, result);
                break;
        }

        result.ActionsPerformed.Add(decision.Action);
        result.DebugInfo = decision.Reason;
    }

    private void ExecuteMoveAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        if (decision.TargetPosition.HasValue)
        {
            var direction = (decision.TargetPosition.Value - mob.Position).GetNormalized();
            var speed = mob.EnhancedStats.Speed;
            var deltaTime = 1.0f / _aiSettings.UpdateFrequency;

            var movement = direction * speed * deltaTime;
            var newPosition = mob.Position + movement;

            // Try full movement first, then slide along axes if blocked
            Vector2? validPosition = null;
            if (IsValidPosition(newPosition, world))
            {
                validPosition = newPosition;
            }
            else
            {
                // Wall sliding: try each axis separately
                var slideX = new Vector2(newPosition.X, mob.Position.Y);
                var slideY = new Vector2(mob.Position.X, newPosition.Y);

                if (Math.Abs(movement.X) > 0.001f && IsValidPosition(slideX, world))
                    validPosition = slideX;
                else if (Math.Abs(movement.Y) > 0.001f && IsValidPosition(slideY, world))
                    validPosition = slideY;
            }

            if (validPosition.HasValue)
            {
                mob.Position = validPosition.Value;
                mob.IsDirty = true; // Sync position to clients
                result.NewPosition = validPosition.Value;

                // Update target if pursuing
                if (!string.IsNullOrEmpty(decision.TargetId))
                {
                    mob.TargetPlayerId = decision.TargetId;
                    mob.LastKnownPlayerPosition = decision.TargetPosition.Value;
                    mob.LastPlayerSeen = DateTime.UtcNow;

                    // Transition to pursuing if not already
                    if (mob.CurrentState != MobState.Pursuing)
                    {
                        ChangeState(mob, MobState.Pursuing, "Moving towards player");
                        result.StateChanged = true;
                        result.NewState = MobState.Pursuing;
                    }
                }
            }
        }
    }

    private void ExecuteAttackAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        if (mob.Health <= 0) return; // Dead mobs can't attack

        if (!string.IsNullOrEmpty(decision.TargetId))
        {
            var target = world.Players.Values.FirstOrDefault(p => p.PlayerId == decision.TargetId);
            if (target != null && target.IsAlive)
            {
                var combatResult = ProcessMobCombat(mob, target, world);

                if (combatResult.AttackPerformed)
                {
                    // Transition to attacking state
                    if (mob.CurrentState != MobState.Attacking)
                    {
                        ChangeState(mob, MobState.Attacking, "Engaged in combat");
                        result.StateChanged = true;
                        result.NewState = MobState.Attacking;
                    }
                }
            }
        }
    }

    private void ExecutePatrolAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        // Set new patrol target if provided
        if (decision.TargetPosition.HasValue)
        {
            mob.PatrolTarget = decision.TargetPosition.Value;
        }

        // Transition to patrol state
        if (mob.CurrentState != MobState.Patrol)
        {
            ChangeState(mob, MobState.Patrol, "Resuming patrol");
            result.StateChanged = true;
            result.NewState = MobState.Patrol;
        }

        result.NewTarget = mob.PatrolTarget;
    }

    private void ExecuteSearchAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        // Transition to alert state
        if (mob.CurrentState != MobState.Alert)
        {
            ChangeState(mob, MobState.Alert, "Searching for players");
            result.StateChanged = true;
            result.NewState = MobState.Alert;
        }

        // Move toward search target (player position)
        if (decision.TargetPosition.HasValue)
        {
            var direction = (decision.TargetPosition.Value - mob.Position).GetNormalized();
            var speed = mob.EnhancedStats.Speed * 0.7f; // Cautious approach
            var deltaTime = 1.0f / _aiSettings.UpdateFrequency;

            var newPosition = mob.Position + direction * speed * deltaTime;

            if (IsValidPosition(newPosition, world))
            {
                mob.Position = newPosition;
                mob.IsDirty = true;
                result.NewPosition = newPosition;
            }

            // Track target player
            if (!string.IsNullOrEmpty(decision.TargetId))
            {
                mob.TargetPlayerId = decision.TargetId;
                mob.LastKnownPlayerPosition = decision.TargetPosition.Value;
                mob.LastPlayerSeen = DateTime.UtcNow;
            }

            result.NewTarget = decision.TargetPosition.Value;
        }
    }

    private void ExecuteFleeAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        // Transition to fleeing state
        if (mob.CurrentState != MobState.Fleeing)
        {
            ChangeState(mob, MobState.Fleeing, "Fleeing from danger");
            result.StateChanged = true;
            result.NewState = MobState.Fleeing;
        }

        // Execute movement away from danger
        if (decision.TargetPosition.HasValue)
        {
            var direction = (decision.TargetPosition.Value - mob.Position).GetNormalized();
            var speed = mob.EnhancedStats.Speed * 1.5f; // Flee faster
            var deltaTime = 1.0f / _aiSettings.UpdateFrequency;

            var newPosition = mob.Position + direction * speed * deltaTime;

            if (IsValidPosition(newPosition, world))
            {
                mob.Position = newPosition;
                mob.IsDirty = true; // Sync position to clients
                result.NewPosition = newPosition;
            }
        }
    }

    private void ExecuteCastAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        if (decision.Parameters.TryGetValue("AbilityType", out var abilityTypeObj) &&
            abilityTypeObj is string abilityType)
        {
            var MobAbilityResult = ProcessMobAbility(mob, abilityType, world);

            if (MobAbilityResult.Success)
            {
                // Only bosses enter Casting state (they use telegraph timer to exit).
                // Regular mobs stay in Attacking — Casting has no exit timer for non-bosses.
                var isBoss = mob.Template?.MobType?.Contains("boss", StringComparison.OrdinalIgnoreCase) == true;
                if (isBoss)
                {
                    ChangeState(mob, MobState.Casting, $"Casting {abilityType}");
                    result.StateChanged = true;
                    result.NewState = MobState.Casting;
                }

                // Record cooldown so mob doesn't spam abilities every tick
                mob.AbilityCooldowns[abilityType] = DateTime.UtcNow;
            }
        }
    }

    private void ExecuteRoarAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        if (decision.Parameters.TryGetValue("HelpCall", out var helpCallObj) &&
            helpCallObj is bool helpCall && helpCall)
        {
            // Call nearby mobs for help
            CallForHelp(mob, world);
            mob.LastHelpCall = DateTime.UtcNow;
        }
    }

    private void ExecuteRangedAttackAction(EnhancedMob mob, AIDecision decision, GameWorld world, AIBehaviorResult result)
    {
        if (mob.Health <= 0) return;

        if (!string.IsNullOrEmpty(decision.TargetId))
        {
            var target = world.Players.Values.FirstOrDefault(p => p.PlayerId == decision.TargetId);
            if (target != null && target.IsAlive)
            {
                var timeSinceLastAttack = DateTime.UtcNow - mob.LastAttackTime;
                if (timeSinceLastAttack.TotalSeconds < mob.EnhancedStats.AttackCooldown) return;

                // Instant ranged damage (no projectile — archers use hitscan)
                var baseDamage = mob.EnhancedStats.Damage;
                bool isCrit = Random.Shared.NextDouble() < mob.EnhancedStats.CriticalChance;
                var finalDamage = CalculateFinalDamage(mob, target, baseDamage, isCrit);

                // Shield absorption for ranged attack
                var remainingDamage = finalDamage;
                if (target.Shield > 0)
                {
                    var absorbed = Math.Min(target.Shield, remainingDamage);
                    target.Shield -= absorbed;
                    remainingDamage -= absorbed;
                    if (target.Shield <= 0)
                    {
                        lock (target.StatusEffectsLock)
                        {
                            target.StatusEffects.RemoveAll(e => e.EffectType == "shield");
                        }
                        target.DamageReduction = target.EquipmentDamageReduction;
                    }
                }
                target.Health = Math.Max(0, target.Health - remainingDamage);
                target.LastDamageTime = DateTime.UtcNow;
                mob.LastAttackTime = DateTime.UtcNow;
                mob.AttacksPerformed++;

                OnMobAttack?.Invoke(mob, target, finalDamage, isCrit);

                if (mob.CurrentState != MobState.Attacking)
                {
                    ChangeState(mob, MobState.Attacking, "Ranged attack");
                    result.StateChanged = true;
                    result.NewState = MobState.Attacking;
                }

                if (target.Health <= 0 && target.IsAlive)
                {
                    _combatSystem.ProcessPlayerDeath(target, null);
                    var stats = GetOrCreateWorldStats(world.WorldId);
                    stats.PlayerKills++;
                    OnPlayerKilledByMob?.Invoke(target, mob);
                }
            }
        }
    }

    // =============================================
    // SPECIALIZED AI BEHAVIORS
    // =============================================

    private void ProcessGroupBehaviors(GameWorld world)
    {
        if (!_mobGroups.TryGetValue(world.WorldId, out var groups))
            return;

        foreach (var group in groups.Values)
        {
            // Snapshot the group to avoid concurrent modification
            List<EnhancedMob> snapshot;
            try
            {
                snapshot = new List<EnhancedMob>(group);
            }
            catch (ArgumentException)
            {
                continue; // Collection was modified during copy
            }

            var aliveMobs = new List<Mob>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].Health > 0)
                    aliveMobs.Add(snapshot[i]);
            }

            if (aliveMobs.Count > 1)
            {
                ProcessGroupBehavior(aliveMobs, world);
            }
        }
    }

    private void CoordinatePursuit(List<EnhancedMob> mobGroup, GameWorld world)
    {
        var leader = mobGroup.FirstOrDefault(m => m.IsGroupLeader);
        if (leader?.TargetPlayerId == null) return;

        var target = world.Players.Values.FirstOrDefault(p => p.PlayerId == leader.TargetPlayerId);
        if (target == null) return;

        // Spread out around target
        var angles = new float[mobGroup.Count];
        for (int i = 0; i < mobGroup.Count; i++)
        {
            angles[i] = (float)(2 * Math.PI * i / mobGroup.Count);
        }

        for (int i = 0; i < mobGroup.Count; i++)
        {
            var mob = mobGroup[i];
            var angle = angles[i];
            var offset = new Vector2(
                (float)Math.Cos(angle) * 5.0f,
                (float)Math.Sin(angle) * 5.0f
            );

            var targetPosition = target.Position + offset;
            mob.TargetPlayerId = target.PlayerId;
            mob.LastKnownPlayerPosition = targetPosition;
        }
    }

    private void CoordinateAttack(List<EnhancedMob> mobGroup, GameWorld world)
    {
        // Focus fire on one target or spread attacks
        var leader = mobGroup.FirstOrDefault(m => m.IsGroupLeader);
        if (leader?.TargetPlayerId != null)
        {
            // All focus on leader's target
            foreach (var mob in mobGroup)
            {
                mob.TargetPlayerId = leader.TargetPlayerId;
            }
        }
    }

    private void CoordinateRetreat(List<EnhancedMob> mobGroup, GameWorld world)
    {
        // Find common retreat direction
        var centerPosition = new Vector2(
            mobGroup.Average(m => m.Position.X),
            mobGroup.Average(m => m.Position.Y)
        );

        var fleeDirection = Vector2.Zero;
        bool hasNearbyPlayer = false;
        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive || Vector2.Distance(player.Position, centerPosition) > 30.0f)
                continue;
            fleeDirection += (centerPosition - player.Position).GetNormalized();
            hasNearbyPlayer = true;
        }

        if (hasNearbyPlayer)
        {
            fleeDirection = fleeDirection.GetNormalized();

            foreach (var mob in mobGroup)
            {
                ChangeState(mob, MobState.Fleeing, "Group retreat");
            }
        }
    }

    private void CallForHelp(EnhancedMob mob, GameWorld world)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var worldMobs))
            return;

        var helpRadius = mob.Template?.BehaviorSettings.HelpCallRadius ?? 15.0f;
        int respondCount = 0;

        for (int i = 0; i < worldMobs.Count; i++)
        {
            var m = worldMobs[i];
            if (m.MobId == mob.MobId || m.Health <= 0 || m.CurrentState != MobState.Patrol)
                continue;
            if (Vector2.Distance(m.Position, mob.Position) > helpRadius)
                continue;

            ChangeState(m, MobState.Alert, "Responding to help call");
            m.TargetPlayerId = mob.TargetPlayerId;
            m.LastKnownPlayerPosition = mob.LastKnownPlayerPosition;
            respondCount++;
        }

        _logger.LogDebug("Mob {MobId} called for help, {Count} mobs responded",
            mob.MobId, respondCount);
    }

    private void ProcessMobDeath(EnhancedMob mob, GameWorld world)
    {
        // Change state to dead
        ChangeState(mob, MobState.Dead, "Mob died");

        // Remove from group
        if (!string.IsNullOrEmpty(mob.GroupId))
        {
            RemoveFromGroup(mob, world.WorldId);
        }

        // Update stats
        var stats = GetOrCreateWorldStats(world.WorldId);
        stats.MobKills++;

        // Fire death event
        var killer = FindMobKiller(mob, world);
        OnMobDeath?.Invoke(world.WorldId, mob, killer);

        _logger.LogDebug("Mob {MobId} died in world {WorldId}", mob.MobId, world.WorldId);
    }

    private RealTimePlayer? FindMobKiller(EnhancedMob mob, GameWorld world)
    {
        // Simple implementation - find nearest player who recently attacked
        return world.Players.Values
            .Where(p => p.IsAlive &&
                       Vector2.Distance(p.Position, mob.Position) <= 10.0f &&
                       (DateTime.UtcNow - p.LastActivity).TotalSeconds <= 5.0)
            .OrderBy(p => Vector2.Distance(p.Position, mob.Position))
            .FirstOrDefault();
    }

    private void ConvertToBoss(EnhancedMob mob)
    {
        if (_mobTemplates.TryGetValue("boss", out var bossTemplate))
        {
            mob.Template = bossTemplate;
            mob.EnhancedStats = bossTemplate.BaseStats;
            mob.Health = mob.EnhancedStats.Health;
            mob.MaxHealth = mob.EnhancedStats.MaxHealth;
            mob.AggressionLevel = bossTemplate.BehaviorSettings.AggressionLevel;
            mob.ProcessingPriority = AIProcessingPriority.Critical;
        }
    }

    /// <summary>Get boss phase (1-3) based on health percentage.</summary>
    private static int GetBossPhase(float healthPct)
    {
        if (healthPct > 0.6f) return 1;
        if (healthPct > 0.3f) return 2;
        return 3;
    }

    private void ProcessBossAbilities(EnhancedMob boss, GameWorld world)
    {
        var healthPct = (float)boss.Health / boss.MaxHealth;
        var phase = GetBossPhase(healthPct);

        // Apply phase-based stat modifiers only when phase transitions (not every frame)
        if (phase != boss.BossPhase)
        {
            boss.BossPhase = phase;
            boss.IsDirty = true;
            if (phase >= 3)
            {
                // Enrage: damage +50%, speed +30%
                boss.EnhancedStats.Damage = (int)(boss.Template?.BaseStats.Damage * 1.5f ?? boss.EnhancedStats.Damage);
                boss.EnhancedStats.Speed = (boss.Template?.BaseStats.Speed ?? 3f) * 1.3f;
            }
            else if (phase >= 2)
            {
                // Phase 2: damage +20%
                boss.EnhancedStats.Damage = (int)(boss.Template?.BaseStats.Damage * 1.2f ?? boss.EnhancedStats.Damage);
            }
        }

        // If boss is casting (telegraph in progress), check if duration elapsed
        if (boss.CurrentState == MobState.Casting && !string.IsNullOrEmpty(boss.PendingAbility))
        {
            var elapsed = (DateTime.UtcNow - boss.TelegraphStart).TotalSeconds;
            if (elapsed >= boss.TelegraphDuration)
            {
                // Telegraph finished — execute the ability
                ProcessMobAbility(boss, boss.PendingAbility, world);
                boss.LastAbilityTime = DateTime.UtcNow;
                boss.PendingAbility = null;
                ChangeState(boss, MobState.Attacking, "Telegraph finished, executing ability");
            }
            return; // Boss doesn't select new abilities while casting
        }

        // Select and telegraph a new ability
        var abilityCooldown = (boss.Template?.BehaviorSettings.AbilityCooldown ?? 5.0) * (phase >= 2 ? 0.7 : 1.0);
        var timeSinceLastAbility = DateTime.UtcNow - boss.LastAbilityTime;

        if (timeSinceLastAbility.TotalSeconds >= abilityCooldown)
        {
            var availableAbilities = boss.Template?.Abilities ?? new List<string>();

            // Phase 3: unlock summon ability
            if (phase >= 2 && !availableAbilities.Contains("summon"))
                availableAbilities = new List<string>(availableAbilities) { "summon" };

            if (availableAbilities.Any())
            {
                var abilityToUse = SelectBossAbility(boss, healthPct, availableAbilities);
                if (!string.IsNullOrEmpty(abilityToUse))
                {
                    // Get telegraph parameters — shorter duration in later phases
                    var (telegraphRadius, baseDuration, telegraphPosition) = GetTelegraphParams(boss, abilityToUse, world);
                    var telegraphDuration = baseDuration * (phase switch { 1 => 1.0f, 2 => 0.8f, _ => 0.6f });

                    // Enter casting state with telegraph
                    boss.PendingAbility = abilityToUse;
                    boss.TelegraphStart = DateTime.UtcNow;
                    boss.TelegraphDuration = telegraphDuration;
                    ChangeState(boss, MobState.Casting, $"Telegraphing {abilityToUse} (phase {phase})");

                    // Emit boss_telegraph event for client VFX
                    OnMobAbilityUsed?.Invoke(world.WorldId, new CombatEvent
                    {
                        EventType = "boss_telegraph",
                        SourceId = boss.MobId,
                        Value = (int)(telegraphRadius * 100),
                        Position = telegraphPosition,
                        RoomId = boss.RoomId,
                        Speed = telegraphDuration,
                        AbilityId = abilityToUse
                    });
                }
            }
        }

        // Phase 3: periodic summon every 20s
        if (phase >= 3)
        {
            if ((DateTime.UtcNow - boss.LastSummonTime).TotalSeconds >= 20)
            {
                boss.LastSummonTime = DateTime.UtcNow;
                SummonMinions(boss, world);
            }
        }
    }

    private (float radius, float duration, Vector2 position) GetTelegraphParams(EnhancedMob boss, string ability, GameWorld world)
    {
        // Find target position for directional abilities
        var targetPos = boss.Position;
        if (!string.IsNullOrEmpty(boss.TargetPlayerId) &&
            world.Players.TryGetValue(boss.TargetPlayerId, out var targetPlayer))
        {
            targetPos = targetPlayer.Position;
        }

        return ability switch
        {
            "charge" => (2f, 1.5f, targetPos),
            "summon" => (8f, 2.0f, boss.Position),
            "roar" => (10f, 1.0f, boss.Position),
            _ => (5f, 1.2f, boss.Position)
        };
    }

    private string? SelectBossAbility(EnhancedMob boss, float healthPercentage, List<string> abilities)
    {
        // Select ability based on health and situation
        if (healthPercentage < 0.3f && abilities.Contains("heal"))
            return "heal";

        if (healthPercentage < 0.5f && abilities.Contains("summon"))
            return "summon";

        if (abilities.Contains("charge"))
            return "charge";

        return abilities.FirstOrDefault();
    }

    private void SummonMinions(EnhancedMob boss, GameWorld world)
    {
        var minionCount = GetMobCountInRoom(world, boss.RoomId);
        if (minionCount >= _aiSettings.MaxMobsPerRoom) return;

        var summonPositions = GenerateSummonPositions(boss.Position, 3);

        foreach (var position in summonPositions)
        {
            var minion = SpawnMob(world, "patrol", position, boss.RoomId);
            var enhancedMinion = minion as EnhancedMob;
            if (enhancedMinion != null)
            {
                enhancedMinion.GroupId = boss.GroupId ?? Guid.NewGuid().ToString()[..8];
                enhancedMinion.AggressionLevel = 1.5f; // Summoned minions are more aggressive
            }
        }

        _logger.LogInformation("Boss {BossId} summoned minions in room {RoomId}",
            boss.MobId, boss.RoomId);
    }

    private List<Vector2> GenerateSummonPositions(Vector2 centerPosition, int count)
    {
        var positions = new List<Vector2>();
        var radius = 8.0f;

        for (int i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            var position = centerPosition + new Vector2(
                (float)(Math.Cos(angle) * radius),
                (float)(Math.Sin(angle) * radius)
            );
            positions.Add(position);
        }

        return positions;
    }

    // =============================================
    // ABILITY IMPLEMENTATIONS
    // =============================================

    private MobAbilityResult ProcessChargeAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Charge", Success = true };

        if (!string.IsNullOrEmpty(mob.TargetPlayerId))
        {
            var target = world.Players.Values.FirstOrDefault(p => p.PlayerId == mob.TargetPlayerId);
            if (target != null)
            {
                // Move rapidly towards target
                var direction = (target.Position - mob.Position).GetNormalized();
                var chargeDistance = 15.0f;
                var newPosition = mob.Position + direction * chargeDistance;

                if (IsValidPosition(newPosition, world))
                {
                    mob.Position = newPosition;

                    // Deal damage if close enough
                    if (Vector2.Distance(mob.Position, target.Position) <= mob.EnhancedStats.AttackRange * 1.5f)
                    {
                        var baseDamage = (int)(mob.EnhancedStats.Damage * 1.5f);
                        bool isCrit = Random.Shared.NextDouble() < mob.EnhancedStats.CriticalChance;
                        var finalDamage = CalculateFinalDamage(mob, target, baseDamage, isCrit);

                        // Shield absorption for charge attack
                        var remainingDamage = finalDamage;
                        if (target.Shield > 0)
                        {
                            var absorbed = Math.Min(target.Shield, remainingDamage);
                            target.Shield -= absorbed;
                            remainingDamage -= absorbed;
                            if (target.Shield <= 0)
                            {
                                lock (target.StatusEffectsLock)
                                {
                                    target.StatusEffects.RemoveAll(e => e.EffectType == "shield");
                                }
                                target.DamageReduction = target.EquipmentDamageReduction;
                            }
                        }
                        target.Health = Math.Max(0, target.Health - remainingDamage);
                        target.LastDamageTime = DateTime.UtcNow;
                        result.Damage = finalDamage;
                        result.AffectedPlayers.Add(target);

                        // Fire attack event so client shows damage number
                        OnMobAttack?.Invoke(mob, target, finalDamage, isCrit);

                        // Handle death — use centralized death processing
                        if (target.Health <= 0 && target.IsAlive)
                        {
                            _combatSystem.ProcessPlayerDeath(target, null);

                            var stats = GetOrCreateWorldStats(world.WorldId);
                            stats.PlayerKills++;

                            OnPlayerKilledByMob?.Invoke(target, mob);
                        }
                    }
                }
            }
        }

        return result;
    }

    private MobAbilityResult ProcessHealAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Heal", Success = true };

        var healAmount = mob.MaxHealth / 4; // Heal 25% of max health
        mob.Health = Math.Min(mob.MaxHealth, mob.Health + healAmount);

        return result;
    }

    private MobAbilityResult ProcessRoarAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Roar", Success = true };

        CallForHelp(mob, world);

        // Intimidate nearby players
        foreach (var player in world.Players.Values)
        {
            if (player.IsAlive && Vector2.Distance(player.Position, mob.Position) <= 10.0f)
                result.AffectedPlayers.Add(player);
        }

        // Emit CombatEvent so clients see the roar visual/audio
        OnMobAbilityUsed?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "mob_roar",
            SourceId = mob.MobId,
            TargetId = "",
            Value = result.AffectedPlayers.Count,
            Position = mob.Position,
            RoomId = mob.RoomId,
            AbilityId = "roar"
        });

        return result;
    }

    private MobAbilityResult ProcessSummonAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Summon", Success = true };

        SummonMinions(mob, world);

        return result;
    }

    private MobAbilityResult ProcessMobFireballAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Mob Fireball", Success = false };

        if (string.IsNullOrEmpty(mob.TargetPlayerId)) return result;
        var target = world.Players.Values.FirstOrDefault(p => p.PlayerId == mob.TargetPlayerId && p.IsAlive);
        if (target == null) return result;

        var direction = (target.Position - mob.Position).GetNormalized();
        var damage = mob.EnhancedStats.Damage * 1.2f;

        _projectileSystem.SpawnMobProjectile(
            mob, "mob_fireball", direction,
            speed: 12f, hitRadius: 0.4f, maxRange: 14f,
            damage: damage, areaRadius: 0f, world);

        result.Success = true;
        result.Damage = (int)damage;
        return result;
    }

    private MobAbilityResult ProcessMobAoeBlastAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "AOE Blast", Success = false };

        if (string.IsNullOrEmpty(mob.TargetPlayerId)) return result;
        var target = world.Players.Values.FirstOrDefault(p => p.PlayerId == mob.TargetPlayerId && p.IsAlive);
        if (target == null) return result;

        var blastRadius = 4.0f;
        var blastDamage = (int)(mob.EnhancedStats.Damage * 0.8f);

        // Instant AOE at target position — hit all players in radius
        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive) continue;
            if (player.CurrentRoomId != mob.RoomId) continue;

            var dist = GameMathUtils.Distance(target.Position, player.Position);
            if (dist > blastRadius) continue;

            var dmg = Math.Max(1, (int)(blastDamage * (1f - player.DamageReduction)));
            player.Health = Math.Max(0, player.Health - dmg);
            player.LastDamageTime = DateTime.UtcNow;
            result.AffectedPlayers.Add(player);

            OnMobAbilityUsed?.Invoke(world.WorldId, new CombatEvent
            {
                EventType = "ability_damage",
                SourceId = mob.MobId,
                TargetId = player.PlayerId,
                Value = dmg,
                Position = player.Position,
                RoomId = mob.RoomId,
                AbilityId = "mob_aoe_blast"
            });

            if (player.Health <= 0 && player.IsAlive)
            {
                player.IsAlive = false;
                player.Health = 0;
                player.DeathTime = DateTime.UtcNow;
                player.ForceNextUpdate();

                // Drop soul if carrying one (matches CombatSystem.ProcessPlayerDeath)
                if (player.CarryingSoulOfPlayerId != null)
                {
                    _logger.LogInformation("Player {PlayerName} died (mob AOE) while carrying soul of {SoulId}",
                        player.PlayerName, player.CarryingSoulOfPlayerId);
                    player.CarryingSoulOfPlayerId = null;
                }

                var stats = GetOrCreateWorldStats(world.WorldId);
                stats.PlayerKills++;
                OnPlayerKilledByMob?.Invoke(player, mob);
            }
        }

        // Emit the blast visual event at target position
        OnMobAbilityUsed?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "mob_aoe_blast",
            SourceId = mob.MobId,
            TargetId = "",
            Value = blastDamage,
            Position = target.Position,
            RoomId = mob.RoomId,
            AbilityId = "mob_aoe_blast"
        });

        result.Success = true;
        result.Damage = blastDamage;
        return result;
    }

    private MobAbilityResult ProcessMobHealAllyAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Heal Ally", Success = false };

        // Find most injured ally mob in radius 10
        var healRadius = 10.0f;
        var bestTarget = world.Mobs.Values
            .Where(m => m.Health > 0 && m.MobId != mob.MobId && m.RoomId == mob.RoomId)
            .Where(m => GameMathUtils.Distance(mob.Position, m.Position) <= healRadius)
            .Where(m => (float)m.Health / m.MaxHealth < 0.9f)
            .OrderBy(m => (float)m.Health / m.MaxHealth)
            .FirstOrDefault();

        if (bestTarget == null) return result;

        var healAmount = (int)(mob.MaxHealth * 0.5f);
        bestTarget.Health = Math.Min(bestTarget.MaxHealth, bestTarget.Health + healAmount);
        bestTarget.IsDirty = true;
        if (bestTarget is EnhancedMob em) em.RequiresUpdate = true;

        OnMobAbilityUsed?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "mob_heal",
            SourceId = mob.MobId,
            TargetId = bestTarget.MobId,
            Value = healAmount,
            Position = bestTarget.Position,
            RoomId = mob.RoomId,
            AbilityId = "mob_heal_ally"
        });

        result.Success = true;
        result.Damage = -healAmount; // negative = healing
        return result;
    }

    private MobAbilityResult ProcessMobHealAoeAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Heal AOE", Success = false };

        var healRadius = 5.0f;
        var healAmount = (int)(mob.MaxHealth * 0.25f);
        int healed = 0;

        foreach (var ally in world.Mobs.Values)
        {
            if (ally.Health <= 0 || ally.RoomId != mob.RoomId) continue;
            if (GameMathUtils.Distance(mob.Position, ally.Position) > healRadius) continue;
            if ((float)ally.Health / ally.MaxHealth >= 1f) continue;

            ally.Health = Math.Min(ally.MaxHealth, ally.Health + healAmount);
            ally.IsDirty = true;
            if (ally is EnhancedMob em) em.RequiresUpdate = true;
            healed++;
        }

        if (healed == 0) return result;

        OnMobAbilityUsed?.Invoke(world.WorldId, new CombatEvent
        {
            EventType = "mob_heal_aoe",
            SourceId = mob.MobId,
            TargetId = "",
            Value = healAmount,
            Position = mob.Position,
            RoomId = mob.RoomId,
            AbilityId = "mob_heal_aoe"
        });

        result.Success = true;
        result.Damage = -healAmount * healed;
        return result;
    }

    // =============================================
    // HEALER AI HELPERS
    // =============================================

    private AIDecision? TryGenerateHealDecision(EnhancedMob mob, GameWorld world)
    {
        var healAbility = mob.Template?.Abilities.FirstOrDefault(a => a.StartsWith("mob_heal"));
        if (healAbility == null) return null;

        // Check cooldown
        if (mob.AbilityCooldowns.TryGetValue(healAbility, out var lastUsed))
        {
            var cd = mob.Template?.BehaviorSettings.AbilityCooldown ?? 5.0f;
            if ((DateTime.UtcNow - lastUsed).TotalSeconds < cd) return null;
        }

        // Check if any ally in range needs healing
        var healRadius = 10.0f;
        var woundedAlly = world.Mobs.Values
            .Where(m => m.Health > 0 && m.MobId != mob.MobId && m.RoomId == mob.RoomId)
            .Where(m => GameMathUtils.Distance(mob.Position, m.Position) <= healRadius)
            .Where(m => (float)m.Health / m.MaxHealth < 0.85f)
            .OrderBy(m => (float)m.Health / m.MaxHealth)
            .FirstOrDefault();

        if (woundedAlly == null) return null;

        return new AIDecision
        {
            Action = MobAction.Cast,
            Priority = 1.3f, // Higher than attack (1.0) to prioritize healing
            TargetId = woundedAlly.MobId,
            Reason = $"Healing wounded ally ({(float)woundedAlly.Health / woundedAlly.MaxHealth:P0} HP)",
            Parameters = new Dictionary<string, object> { ["AbilityType"] = healAbility }
        };
    }

    // =============================================
    // UTILITY METHODS
    // =============================================

    private bool IsValidPosition(Vector2 position, GameWorld world)
    {
        return _movementSystem.IsValidPosition(world, position);
    }

    private int GetMobCountInRoom(GameWorld world, string roomId)
    {
        return world.Mobs.Values.Count(m => m.RoomId == roomId && m.Health > 0);
    }

    private bool RoomHasMobType(GameWorld world, string roomId, string mobType)
    {
        return world.Mobs.Values.Any(m =>
            m.RoomId == roomId &&
            m.Health > 0 &&
            (m.MobType == mobType || (mobType == "boss" && m is EnhancedMob em && em.Template?.IsBoss == true)));
    }

    private int GetProcessingBudget(AIProcessingPriority priority)
    {
        return priority switch
        {
            AIProcessingPriority.Critical => 50,
            AIProcessingPriority.High => 30,
            AIProcessingPriority.Medium => 20,
            AIProcessingPriority.Low => 10,
            _ => 10
        };
    }

    private int CalculateFinalDamage(EnhancedMob attacker, RealTimePlayer target, int baseDamage, bool isCrit)
    {
        var damage = baseDamage;

        // Apply critical hit
        if (isCrit)
        {
            damage = (int)(damage * 1.5f);
        }

        // Apply target's damage reduction from equipment
        damage = (int)(damage * (1f - Math.Clamp(target.DamageReduction, 0f, 0.90f)));

        return Math.Max(1, damage);
    }

    private double CalculateProcessingEfficiency()
    {
        var totalMobs = _worldMobs.Values.Sum(mobs => mobs.Count);
        if (totalMobs == 0) return 1.0;

        var activeMobs = _worldMobs.Values.Sum(mobs => mobs.Count(m => m.RequiresUpdate));
        return totalMobs > 0 ? (double)activeMobs / totalMobs : 1.0;
    }

    private string GetWorldIdForMob(EnhancedMob mob)
    {
        return _worldMobs.FirstOrDefault(kvp => kvp.Value.Contains(mob)).Key ?? "";
    }

    private GameWorld? GetWorldById(string worldId)
    {
        // This would need to be injected or passed as parameter
        // For now, return null as we don't have direct access to world collection
        return null;
    }

    // =============================================
    // DISPOSAL
    // =============================================

    public void Dispose()
    {
        _worldMobs.Clear();
        _mobGroups.Clear();
        _worldAIStats.Clear();
        _worldNavMeshes.Clear();
        _lastUpdateTimes.Clear();
        _lastDynamicSpawn.Clear();

        _logger.LogInformation("MobAISystem disposed");
    }
}