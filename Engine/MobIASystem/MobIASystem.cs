using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.AI.Interface;
using MazeWars.GameServer.Engine.MobIASystem.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MazeWars.GameServer.Services.AI;

public class MobAISystem : IMobAISystem
{
    private readonly ILogger<MobAISystem> _logger;
    private readonly GameServerSettings _settings;
    private AISettings _aiSettings;
    private readonly Random _random;

    // Templates and configuration
    private readonly Dictionary<string, MobTemplate> _mobTemplates = new();
    private readonly Dictionary<string, NavigationMesh> _worldNavMeshes = new();

    // World-specific AI tracking
    private readonly ConcurrentDictionary<string, AIStats> _worldAIStats = new();
    private readonly ConcurrentDictionary<string, List<EnhancedMob>> _worldMobs = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, List<EnhancedMob>>> _mobGroups = new();

    // Performance optimization
    private readonly Dictionary<string, DateTime> _lastUpdateTimes = new();
    private readonly Dictionary<string, DateTime> _lastDynamicSpawn = new();
    private int _updateCycle = 0;

    // Events
    public event Action<string, Mob>? OnMobSpawned;
    public event Action<string, Mob, RealTimePlayer?>? OnMobDeath;
    public event Action<Mob, MobState, MobState>? OnMobStateChanged;
    public event Action<Mob, RealTimePlayer, int>? OnMobAttack;
    public event Action<string, Mob>? OnBossSpawned;

    public MobAISystem(ILogger<MobAISystem> logger, IOptions<GameServerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _random = new Random();

        // Initialize AI settings
        _aiSettings = new AISettings
        {
            GlobalAggressionMultiplier = 1.0f,
            UpdateFrequency = 30.0f,
            MaxMobsPerRoom = 8,
            DifficultyScaling = 1.0f,
            EnableGroupBehavior = true,
            EnableDynamicSpawning = true,
            DynamicSpawnInterval = 60.0f,
            MaxDynamicMobs = 20,
            EnablePerformanceOptimization = true,
            OptimizationDistance = 50.0f,
            EnableBossAI = true,
            BossSpawnChance = 0.1f
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

        // Spawn mobs in rooms (excluding corner extraction rooms)
        var spawnableRooms = world.Rooms.Values
            .Where(r => !IsExtractionRoom(r.RoomId, world))
            .ToList();

        foreach (var room in spawnableRooms)
        {
            var mobCount = CalculateMobCountForRoom(room, world);

            for (int i = 0; i < mobCount; i++)
            {
                var mobType = SelectMobTypeForRoom(room, world);
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
            EnhancedStats = CalculateMobStats(mobType, world, world.Rooms[roomId]),
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
        if (template.IsBoss || (_random.NextDouble() < _aiSettings.BossSpawnChance && mobType == "elite"))
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
        _updateCycle++;

        // Update spatial partitioning for optimization
        if (_aiSettings.EnablePerformanceOptimization)
        {
            UpdateSpatialPartitioning(world);
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

        var deadMobs = worldMobs
            .Where(m => m.Health <= 0 && m.CurrentState != MobState.Dead)
            .ToList();

        foreach (var mob in deadMobs)
        {
            ProcessMobDeath(mob, world);
        }
    }

    // =============================================
    // AI BEHAVIOR SYSTEM
    // =============================================

    public AIBehaviorResult UpdateMobBehavior(Mob mob, GameWorld world, float deltaTime)
    {
        var enhancedMob = mob as EnhancedMob ?? ConvertToEnhancedMob(mob);
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

        var potentialTargets = world.Players.Values
            .Where(p => p.IsAlive && p.CurrentRoomId == mob.RoomId)
            .ToList();

        if (!potentialTargets.Any())
            return null;

        // Apply targeting preferences
        var preferredTargets = potentialTargets;

        if (template?.BehaviorSettings.PreferredTargets.Any() == true)
        {
            var preferred = potentialTargets
                .Where(p => template.BehaviorSettings.PreferredTargets.Contains(p.PlayerClass))
                .ToList();

            if (preferred.Any())
                preferredTargets = preferred;
        }

        // Find closest target within detection range
        var detectionRange = enhancedMob.EnhancedStats.DetectionRange;
        var validTargets = preferredTargets
            .Where(p => Vector2.Distance(p.Position, mob.Position) <= detectionRange)
            .OrderBy(p => Vector2.Distance(p.Position, mob.Position))
            .ToList();

        return validTargets.FirstOrDefault();
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
        var finalDamage = CalculateFinalDamage(enhancedMob, target, baseDamage);

        // Apply damage
        target.Health = Math.Max(0, target.Health - finalDamage);
        target.LastDamageTime = DateTime.UtcNow;

        // Update mob state
        enhancedMob.LastAttackTime = DateTime.UtcNow;
        enhancedMob.AttacksPerformed++;
        enhancedMob.LastAttacker = target.PlayerId;

        // Set result
        result.AttackPerformed = true;
        result.DamageDealt = finalDamage;
        result.TargetKilled = target.Health <= 0;

        // Fire attack event
        OnMobAttack?.Invoke(enhancedMob, target, finalDamage);

        // Check if target died
        if (target.Health <= 0 && target.IsAlive)
        {
            result.TargetKilled = true;
            var stats = GetOrCreateWorldStats(world.WorldId);
            stats.PlayerKills++;

            OnMobDeath?.Invoke(world.WorldId, enhancedMob, target);
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

        var enhancedMobs = mobGroup.Cast<EnhancedMob>().ToList();
        var leader = enhancedMobs.FirstOrDefault(m => m.IsGroupLeader) ?? enhancedMobs.First();

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

        var currentMobCount = _worldMobs.TryGetValue(world.WorldId, out var mobs) ?
            mobs.Count(m => m.Health > 0) : 0;

        if (currentMobCount >= _aiSettings.MaxDynamicMobs)
            return;

        // Find rooms that need more mobs
        var roomsNeedingMobs = world.Rooms.Values
            .Where(r => !IsExtractionRoom(r.RoomId, world))
            .Where(r => GetMobCountInRoom(world, r.RoomId) < _aiSettings.MaxMobsPerRoom / 2)
            .ToList();

        if (roomsNeedingMobs.Any())
        {
            var room = roomsNeedingMobs[_random.Next(roomsNeedingMobs.Count)];
            var mobType = SelectMobTypeForRoom(room, world);
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
                var scaledStats = CalculateMobStats(mob.MobType, world, world.Rooms[mob.RoomId]);
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

        // Scale stats
        stats.Health = (int)(stats.Health * totalMultiplier);
        stats.MaxHealth = (int)(stats.MaxHealth * totalMultiplier);
        stats.Damage = (int)(stats.Damage * totalMultiplier);
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

        foreach (var mob in mobs)
        {
            // Calculate distance to nearest player
            var nearestPlayerDistance = world.Players.Values
                .Where(p => p.IsAlive)
                .Select(p => Vector2.Distance(p.Position, mob.Position))
                .DefaultIfEmpty(float.MaxValue)
                .Min();

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

        // Group mobs by priority
        var mobsByPriority = mobs
            .Where(m => m.Health > 0)
            .GroupBy(m => m.ProcessingPriority)
            .OrderByDescending(g => g.Key)
            .ToList();

        foreach (var priorityGroup in mobsByPriority)
        {
            var processingBudget = GetProcessingBudget(priorityGroup.Key);
            var mobsToProcess = priorityGroup.Take(processingBudget).ToList();

            foreach (var mob in mobsToProcess)
            {
                UpdateMobBehavior(mob, world, deltaTime);
            }
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
        _worldNavMeshes.Remove(worldId);
        _lastUpdateTimes.Remove(worldId);
        _lastDynamicSpawn.Remove(worldId);

        _logger.LogInformation("Cleaned up AI data for world {WorldId}", worldId);
    }

    public void CleanupInvalidMobs(GameWorld world)
    {
        if (!_worldMobs.TryGetValue(world.WorldId, out var mobs))
            return;

        var invalidMobs = mobs.Where(m =>
            m.Health <= 0 &&
            (DateTime.UtcNow - m.StateChangedAt).TotalMinutes > 5 // Dead for 5+ minutes
        ).ToList();

        foreach (var mob in invalidMobs)
        {
            mobs.Remove(mob);
            world.Mobs.TryRemove(mob.MobId, out _);

            // Remove from group
            if (!string.IsNullOrEmpty(mob.GroupId))
            {
                RemoveFromGroup(mob, world.WorldId);
            }
        }

        if (invalidMobs.Any())
        {
            _logger.LogDebug("Cleaned up {Count} invalid mobs from world {WorldId}",
                invalidMobs.Count, world.WorldId);
        }
    }

    public void OptimizeMemoryUsage()
    {
        foreach (var (worldId, mobs) in _worldMobs.ToList())
        {
            // Clean up old tracking data
            foreach (var mob in mobs)
            {
                // Clear old position history
                if (mob.RecentPositions?.Count > 10)
                {
                    mob.RecentPositions = mob.RecentPositions.TakeLast(5).ToList();
                }

                // Clear old path data
                if ((DateTime.UtcNow - mob.PathCalculatedAt).TotalMinutes > 1)
                {
                    mob.CurrentPath.Clear();
                    mob.CurrentPathIndex = 0;
                }

                // Clear old ability cooldowns
                var expiredCooldowns = mob.AbilityCooldowns
                    .Where(kvp => (DateTime.UtcNow - kvp.Value).TotalMinutes > 10)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var cooldown in expiredCooldowns)
                {
                    mob.AbilityCooldowns.Remove(cooldown);
                }
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

    private int CalculateMobCountForRoom(Room room, GameWorld world)
    {
        var baseCount = _random.Next(1, _aiSettings.MaxMobsPerRoom / 2 + 1);

        // Boss rooms get fewer but stronger mobs
        if (room.RoomId.Contains("boss"))
            return Math.Max(1, baseCount / 2);

        return baseCount;
    }

    private string SelectMobTypeForRoom(Room room, GameWorld world)
    {
        var availableTypes = _mobTemplates.Values
            .Where(t => t.SpawnRooms.Count == 0 || t.SpawnRooms.Contains(room.RoomId))
            .ToList();

        if (!availableTypes.Any())
            return "patrol"; // Fallback

        // Weighted selection
        var totalWeight = availableTypes.Sum(t => t.SpawnWeight);
        var randomValue = _random.NextDouble() * totalWeight;
        var currentWeight = 0.0f;

        foreach (var template in availableTypes)
        {
            currentWeight += template.SpawnWeight;
            if (randomValue <= currentWeight)
                return template.MobType;
        }

        return availableTypes.First().MobType;
    }

    private Vector2 CalculateSpawnPosition(Room room)
    {
        var margin = 10.0f;
        var x = room.Position.X + _random.Next((int)-room.Size.X / 2 + (int)margin, (int)room.Size.X / 2 - (int)margin);
        var y = room.Position.Y + _random.Next((int)-room.Size.Y / 2 + (int)margin, (int)room.Size.Y / 2 - (int)margin);
        return new Vector2(x, y);
    }

    private Vector2 GenerateRandomOffset(float radius)
    {
        var angle = _random.NextDouble() * Math.PI * 2;
        var distance = _random.NextDouble() * radius;
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
            navMesh.RoomConnections[room.RoomId] = room.Connections.Select(c => world.Rooms[c].Position).ToList();
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

    private EnhancedMob ConvertToEnhancedMob(Mob baseMob)
    {
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

    private AIDecisionContext CreateDecisionContext(EnhancedMob mob, GameWorld world, float deltaTime)
    {
        var nearbyPlayers = world.Players.Values
            .Where(p => p.IsAlive && Vector2.Distance(p.Position, mob.Position) <= mob.EnhancedStats.DetectionRange)
            .ToList();

        var nearbyMobs = _worldMobs.TryGetValue(world.WorldId, out var mobs) ?
            mobs.Where(m => m.MobId != mob.MobId &&
                           Vector2.Distance(m.Position, mob.Position) <= 20.0f &&
                           m.Health > 0)
                .Cast<Mob>()
                .ToList() : new List<Mob>();

        return new AIDecisionContext
        {
            Mob = mob,
            World = world,
            NearbyPlayers = nearbyPlayers,
            NearbyMobs = nearbyMobs,
            CurrentRoom = world.Rooms[mob.RoomId],
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

    private List<AIDecision> GeneratePursuitDecisions(AIDecisionContext context)
    {
        var decisions = new List<AIDecision>();
        var mob = context.Mob as EnhancedMob;

        if (!string.IsNullOrEmpty(mob.TargetPlayerId))
        {
            var target = context.World.Players.Values.FirstOrDefault(p => p.PlayerId == mob.TargetPlayerId);
            if (target != null && target.IsAlive)
            {
                var distance = Vector2.Distance(mob.Position, target.Position);

                if (distance <= mob.EnhancedStats.AttackRange)
                {
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Attack,
                        Priority = 1.0f,
                        TargetId = target.PlayerId,
                        Reason = "Target in attack range"
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
                    // Lost target
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Search,
                        Priority = 0.5f,
                        TargetPosition = mob.LastKnownPlayerPosition,
                        Reason = "Lost target, searching last known position"
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

        if (!string.IsNullOrEmpty(mob.TargetPlayerId))
        {
            var target = context.World.Players.Values.FirstOrDefault(p => p.PlayerId == mob.TargetPlayerId);
            if (target != null && target.IsAlive)
            {
                var distance = Vector2.Distance(mob.Position, target.Position);

                if (distance <= mob.EnhancedStats.AttackRange)
                {
                    // Continue attacking
                    decisions.Add(new AIDecision
                    {
                        Action = MobAction.Attack,
                        Priority = 1.0f,
                        TargetId = target.PlayerId,
                        Reason = "Continuing attack on target"
                    });

                    // Consider using abilities
                    if (mob.Template?.Abilities.Any() == true)
                    {
                        var availableAbilities = mob.Template.Abilities
                            .Where(ability => !mob.AbilityCooldowns.ContainsKey(ability) ||
                                            (DateTime.UtcNow - mob.AbilityCooldowns[ability]).TotalSeconds >=
                                            mob.Template.BehaviorSettings.AbilityCooldown)
                            .ToList();

                        foreach (var ability in availableAbilities)
                        {
                            decisions.Add(new AIDecision
                            {
                                Action = MobAction.Cast,
                                Priority = 0.8f,
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

            var newPosition = mob.Position + direction * speed * deltaTime;

            if (IsValidPosition(newPosition, world))
            {
                mob.Position = newPosition;
                result.NewPosition = newPosition;

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
        if (!string.IsNullOrEmpty(decision.TargetId))
        {
            var target = world.Players.Values.FirstOrDefault(p => p.PlayerId == decision.TargetId);
            if (target != null)
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

        // Set search target
        if (decision.TargetPosition.HasValue)
        {
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
                // Transition to casting state briefly
                ChangeState(mob, MobState.Casting, $"Casting {abilityType}");
                result.StateChanged = true;
                result.NewState = MobState.Casting;
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

    // =============================================
    // SPECIALIZED AI BEHAVIORS
    // =============================================

    private void ProcessGroupBehaviors(GameWorld world)
    {
        if (!_mobGroups.TryGetValue(world.WorldId, out var groups))
            return;

        foreach (var group in groups.Values)
        {
            var aliveMobs = group.Where(m => m.Health > 0).ToList();
            if (aliveMobs.Count > 1)
            {
                ProcessGroupBehavior(aliveMobs.Cast<Mob>().ToList(), world);
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

        var nearbyPlayers = world.Players.Values
            .Where(p => p.IsAlive && Vector2.Distance(p.Position, centerPosition) <= 30.0f)
            .ToList();

        if (nearbyPlayers.Any())
        {
            var fleeDirection = Vector2.Zero;
            foreach (var player in nearbyPlayers)
            {
                fleeDirection += (centerPosition - player.Position).GetNormalized();
            }

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
        var nearbyMobs = worldMobs
            .Where(m => m.MobId != mob.MobId &&
                       m.Health > 0 &&
                       Vector2.Distance(m.Position, mob.Position) <= helpRadius &&
                       m.CurrentState == MobState.Patrol)
            .ToList();

        foreach (var nearbyMob in nearbyMobs)
        {
            ChangeState(nearbyMob, MobState.Alert, "Responding to help call");
            nearbyMob.TargetPlayerId = mob.TargetPlayerId;
            nearbyMob.LastKnownPlayerPosition = mob.LastKnownPlayerPosition;
        }

        _logger.LogDebug("Mob {MobId} called for help, {Count} mobs responded",
            mob.MobId, nearbyMobs.Count);
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

    private void ProcessBossAbilities(EnhancedMob boss, GameWorld world)
    {
        var healthPercentage = (float)boss.Health / boss.MaxHealth;
        var timeSinceLastAbility = DateTime.UtcNow - boss.LastAbilityTime;

        if (timeSinceLastAbility.TotalSeconds >= boss.Template?.BehaviorSettings.AbilityCooldown)
        {
            var availableAbilities = boss.Template?.Abilities ?? new List<string>();

            if (availableAbilities.Any())
            {
                var abilityToUse = SelectBossAbility(boss, healthPercentage, availableAbilities);
                if (!string.IsNullOrEmpty(abilityToUse))
                {
                    ProcessMobAbility(boss, abilityToUse, world);
                    boss.LastAbilityTime = DateTime.UtcNow;
                }
            }
        }
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
                        var damage = mob.EnhancedStats.Damage * 1.5f; // Charge does more damage
                        target.Health = Math.Max(0, target.Health - (int)damage);
                        result.Damage = (int)damage;
                        result.AffectedPlayers.Add(target);
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
        var nearbyPlayers = world.Players.Values
            .Where(p => p.IsAlive && Vector2.Distance(p.Position, mob.Position) <= 10.0f)
            .ToList();

        foreach (var player in nearbyPlayers)
        {
            result.AffectedPlayers.Add(player);
            // Could apply fear effect here
        }

        return result;
    }

    private MobAbilityResult ProcessSummonAbility(EnhancedMob mob, GameWorld world)
    {
        var result = new MobAbilityResult { AbilityName = "Summon", Success = true };

        SummonMinions(mob, world);

        return result;
    }

    // =============================================
    // UTILITY METHODS
    // =============================================

    private bool IsValidPosition(Vector2 position, GameWorld world)
    {
        // Simple bounds checking - can be enhanced with collision detection
        foreach (var room in world.Rooms.Values)
        {
            if (position.X >= room.Position.X - room.Size.X / 2 &&
                position.X <= room.Position.X + room.Size.X / 2 &&
                position.Y >= room.Position.Y - room.Size.Y / 2 &&
                position.Y <= room.Position.Y + room.Size.Y / 2)
            {
                return true;
            }
        }
        return false;
    }

    private int GetMobCountInRoom(GameWorld world, string roomId)
    {
        return world.Mobs.Values.Count(m => m.RoomId == roomId && m.Health > 0);
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

    private int CalculateFinalDamage(EnhancedMob attacker, RealTimePlayer target, int baseDamage)
    {
        var damage = baseDamage;

        // Apply critical hit
        if (_random.NextDouble() < attacker.EnhancedStats.CriticalChance)
        {
            damage = (int)(damage * 1.5f);
        }

        // Apply target armor (if implemented)
        // damage = Math.Max(1, damage - target.Armor);

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