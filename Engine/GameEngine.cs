using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Engine.Movement.Interface;
using MazeWars.GameServer.Engine.Movement.Settings;
using MazeWars.GameServer.Engine.AI.Interface;
using MazeWars.GameServer.Engine.Equipment;
using MazeWars.GameServer.Engine.Equipment.Interface;
using MazeWars.GameServer.Engine.Loot.Interface;
using MazeWars.GameServer.Engine.Network;
using MazeWars.GameServer.Engine.Memory;
using MazeWars.GameServer.Engine.Managers;
using MazeWars.GameServer.Engine.Stash;
using MazeWars.GameServer.Data.Repositories;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Utils;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using MazeWars.GameServer.Engine.MobIASystem.Models;

namespace MazeWars.GameServer.Engine;

public class RealTimeGameEngine
{
    private readonly ILogger<RealTimeGameEngine> _logger;
    private readonly GameServerSettings _settings;
    private readonly ICombatSystem _combatSystem;
    private readonly IMovementSystem _movementSystem;
    private readonly ILootSystem _lootSystem;
    private readonly IEquipmentSystem _equipmentSystem;
    private readonly IMobAISystem _mobAISystem;
    private readonly LobbyManager _lobbyManager;
    private readonly WorldManager _worldManager;
    private readonly InputProcessor _inputProcessor;
    private readonly StashService _stashService;
    private readonly IPlayerRepository _playerRepository;

    private readonly Timer _gameLoopTimer;

    private readonly ConcurrentDictionary<string, ConcurrentBag<CombatEvent>> _recentCombatEvents = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<LootUpdate>> _recentLootUpdates = new();

    private readonly Queue<PlayerStateUpdate> _playerUpdatePool = new();
    private readonly Queue<CombatEvent> _combatEventPool = new();
    private readonly Random _random = new();

    private int _frameNumber = 0;
    private DateTime _lastFrameTime = DateTime.UtcNow;

    public event Action<string, List<string>>? OnGameStarted;
    public event Action<string, RealTimePlayer>? OnInventoryChanged;
    public event Action<string, RealTimePlayer, string>? OnPlayerError;
    public event Action<string, RealTimePlayer, MatchSummaryData>? OnPlayerExtracted;

    public RealTimeGameEngine(
        ILogger<RealTimeGameEngine> logger,
        IOptions<GameServerSettings> settings,
        ICombatSystem combatSystem,
        IMovementSystem movementSystem,
        ILootSystem lootSystem,
        IEquipmentSystem equipmentSystem,
        IMobAISystem mobAISystem,
        LobbyManager lobbyManager,
        WorldManager worldManager,
        InputProcessor inputProcessor,
        StashService stashService,
        IPlayerRepository playerRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _combatSystem = combatSystem;
        _movementSystem = movementSystem;
        _lootSystem = lootSystem;
        _equipmentSystem = equipmentSystem;
        _mobAISystem = mobAISystem;
        _lobbyManager = lobbyManager;
        _worldManager = worldManager;
        _inputProcessor = inputProcessor;
        _stashService = stashService;
        _playerRepository = playerRepository;

        // ⭐ REFACTORED: Configure InputProcessor with player lookup
        _inputProcessor.PlayerLookup = FindPlayer;

        // ⭐ REFACTORED: Subscribe to LobbyManager events
        _lobbyManager.OnLobbyReadyToStart += HandleLobbyReadyToStart;

        // ⭐ REFACTORED: Subscribe to InputProcessor events
        _inputProcessor.OnPlayerInput += ProcessPlayerInput;
        _inputProcessor.OnLootGrab += ProcessLootGrab;
        _inputProcessor.OnChat += ProcessChat;
        _inputProcessor.OnPingMarker += ProcessPingMarker;
        _inputProcessor.OnUseItem += ProcessUseItem;
        _inputProcessor.OnExtraction += ProcessExtraction;
        _inputProcessor.OnTradeRequest += ProcessTradeRequest;
        _inputProcessor.OnContainerGrab += ProcessContainerGrab;

        // ⭐ NUEVO: Subscribe to MobAISystem events
        _mobAISystem.OnMobSpawned += HandleMobSpawned;
        _mobAISystem.OnMobDeath += HandleMobDeath;
        _mobAISystem.OnMobStateChanged += HandleMobStateChanged;
        _mobAISystem.OnMobAttack += HandleMobAttack;
        _mobAISystem.OnBossSpawned += HandleBossSpawned;
        _mobAISystem.OnPlayerKilledByMob += HandlePlayerKilledByMob;
        _mobAISystem.OnMobAbilityUsed += HandleCombatEvent;

        // Subscribe to LootSystem events
        _lootSystem.OnLootSpawned += HandleLootSpawned;
        _lootSystem.OnLootTaken += HandleLootTaken;
        _lootSystem.OnLootRemoved += HandleLootRemoved;
        _lootSystem.OnItemUsed += HandleItemUsed;
        _lootSystem.OnContainerSpawned += HandleContainerSpawned;
        _lootSystem.OnContainerUpdated += HandleContainerUpdated;
        _lootSystem.OnContainerRemoved += HandleContainerRemoved;

        // Subscribe to CombatSystem events
        _combatSystem.OnCombatEvent += HandleCombatEvent;
        _combatSystem.OnPlayerDeath += HandlePlayerDeath;

        // Subscribe to ProjectileSystem events (uses same handlers)
        _combatSystem.ProjectileSystem.OnCombatEvent += HandleCombatEvent;
        _combatSystem.ProjectileSystem.OnPlayerDeath += HandlePlayerDeath;

        // ⭐ NUEVO: Initialize mob templates
        InitializeMobTemplates();

        // Start game loop at target FPS
        var frameTimeMs = 1000.0 / _settings.TargetFPS;
        _gameLoopTimer = new Timer(GameLoop, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(frameTimeMs));

        _logger.LogInformation("⭐ REFACTORED: Real-Time Game Engine started at {FPS} FPS with LobbyManager, WorldManager, InputProcessor, Combat, Movement, Loot, and AI systems", _settings.TargetFPS);

    }

    // =============================================
    // ⭐ NUEVOS: EVENT HANDLERS DEL MOB AI SYSTEM
    // =============================================

    private void HandleMobSpawned(string worldId, Mob mob)
    {
        _logger.LogDebug("Mob spawned in world {WorldId}: {MobType} {MobId} at {Position}",
            worldId, mob.MobType, mob.MobId, mob.Position);
    }

    private void HandleMobDeath(string worldId, Mob mob, RealTimePlayer? killer)
    {
        var world = FindWorldById(worldId);
        if (world != null)
        {
            _lootSystem.ProcessMobDeathLoot(mob, world, killer);
        }

        // XP for killer + track mob kills
        if (killer != null)
        {
            var xpReward = (mob is MazeWars.GameServer.Engine.MobIASystem.Models.EnhancedMob enhMob)
                ? enhMob.EnhancedStats.ExperienceReward : 0;
            var xpAmount = xpReward > 0 ? xpReward : (_random.Next(50, 81));
            killer.ExperiencePoints += xpAmount;
            killer.MobKills++;
            CheckLevelUp(killer);
        }

        AddCombatEvent(worldId, new CombatEvent
        {
            EventType = "mob_death",
            SourceId = killer?.PlayerId ?? "",
            TargetId = mob.MobId,
            Value = 0,
            Position = mob.Position,
            RoomId = mob.RoomId
        });

        _logger.LogInformation("Mob died in world {WorldId}: {MobType} {MobId}, killed by {KillerName}",
            worldId, mob.MobType, mob.MobId, killer?.PlayerName ?? "unknown");
    }

    private void HandlePlayerKilledByMob(RealTimePlayer deadPlayer, Mob killerMob)
    {
        // Reuse the same player death logic (loot drop, death event)
        HandlePlayerDeath(deadPlayer, null);

        _logger.LogInformation("Player {PlayerName} killed by mob {MobType} {MobId}",
            deadPlayer.PlayerName, killerMob.MobType, killerMob.MobId);
    }

    private void HandleMobStateChanged(Mob mob, MobState oldState, MobState newState)
    {
        _logger.LogDebug("Mob {MobId} changed state from {OldState} to {NewState}",
            mob.MobId, oldState, newState);

        // Marcar mob como dirty para envío de updates
        mob.IsDirty = true;
    }

    private void HandleMobAttack(Mob mob, RealTimePlayer target, int damage, bool isCrit)
    {
        var worldId = FindWorldIdByPlayer(target.PlayerId);
        if (!string.IsNullOrEmpty(worldId))
        {
            AddCombatEvent(worldId, new CombatEvent
            {
                EventType = isCrit ? "damage_crit" : "mob_attack",
                SourceId = mob.MobId,
                TargetId = target.PlayerId,
                Value = damage,
                Position = target.Position,
                RoomId = mob.RoomId
            });
        }

        _logger.LogDebug("Mob {MobId} attacked {PlayerName} for {Damage} damage{Crit}",
            mob.MobId, target.PlayerName, damage, isCrit ? " (CRIT)" : "");
    }

    private void HandleBossSpawned(string worldId, Mob boss)
    {
        AddCombatEvent(worldId, new CombatEvent
        {
            EventType = "boss_spawned",
            SourceId = boss.MobId,
            TargetId = "",
            Value = 0,
            Position = boss.Position,
            RoomId = boss.RoomId
        });

        _logger.LogInformation("Boss spawned in world {WorldId}: {MobType} {MobId} at {Position}",
            worldId, boss.MobType, boss.MobId, boss.Position);
    }

    // =============================================
    // EVENT HANDLERS DEL LOOT SYSTEM (sin cambios)
    // =============================================

    private void HandleLootSpawned(string worldId, LootUpdate lootUpdate)
    {
        AddLootUpdate(worldId, lootUpdate);

        _logger.LogDebug("Loot spawned in world {WorldId}: {ItemName} at {Position}",
            worldId, lootUpdate.ItemName, lootUpdate.Position);
    }

    private void HandleLootTaken(string worldId, LootUpdate lootUpdate)
    {
        AddLootUpdate(worldId, lootUpdate);

        _logger.LogDebug("Loot taken in world {WorldId}: {ItemName} by {PlayerId}",
            worldId, lootUpdate.ItemName, lootUpdate.TakenBy);
    }

    private void HandleLootRemoved(string worldId, LootUpdate lootUpdate)
    {
        AddLootUpdate(worldId, lootUpdate);

        _logger.LogDebug("Loot removed in world {WorldId}: {ItemName} ({UpdateType})",
            worldId, lootUpdate.ItemName, lootUpdate.UpdateType);
    }

    // =============================================
    // EVENT HANDLERS DEL CONTAINER SYSTEM
    // =============================================

    private readonly ConcurrentDictionary<string, ConcurrentBag<ContainerUpdate>> _recentContainerUpdates = new();

    private void HandleContainerSpawned(string worldId, LootContainer container)
    {
        var update = new ContainerUpdate
        {
            UpdateType = "spawned",
            ContainerId = container.ContainerId,
            ContainerType = container.ContainerType,
            PositionX = container.Position.X,
            PositionY = container.Position.Y,
            RoomId = container.RoomId,
            DisplayName = container.DisplayName,
            Items = container.Contents.Select(i => new ContainerItemInfo
            {
                LootId = i.LootId, ItemName = i.ItemName, ItemType = i.ItemType, Rarity = i.Rarity,
                Quality = EquipmentSystem.GetItemQualityTier(i) is var qt && qt >= 0 ? qt : 0
            }).ToList()
        };
        AddContainerUpdate(worldId, update);
    }

    private void HandleContainerUpdated(string worldId, LootContainer container)
    {
        var update = new ContainerUpdate
        {
            UpdateType = "updated",
            ContainerId = container.ContainerId,
            ContainerType = container.ContainerType,
            PositionX = container.Position.X,
            PositionY = container.Position.Y,
            RoomId = container.RoomId,
            DisplayName = container.DisplayName,
            Items = container.Contents.Select(i => new ContainerItemInfo
            {
                LootId = i.LootId, ItemName = i.ItemName, ItemType = i.ItemType, Rarity = i.Rarity,
                Quality = EquipmentSystem.GetItemQualityTier(i) is var qt && qt >= 0 ? qt : 0
            }).ToList()
        };
        AddContainerUpdate(worldId, update);
    }

    private void HandleContainerRemoved(string worldId, string containerId)
    {
        AddContainerUpdate(worldId, new ContainerUpdate
        {
            UpdateType = "removed",
            ContainerId = containerId
        });
    }

    private void AddContainerUpdate(string worldId, ContainerUpdate update)
    {
        var bag = _recentContainerUpdates.GetOrAdd(worldId, _ => new ConcurrentBag<ContainerUpdate>());
        if (bag.Count < MaxEventsPerBag)
            bag.Add(update);
    }

    private List<ContainerUpdate> GetAndClearRecentContainerUpdates(string worldId)
    {
        if (_recentContainerUpdates.TryRemove(worldId, out var bag))
            return bag.ToList();
        return new List<ContainerUpdate>();
    }

    private void HandleItemUsed(RealTimePlayer player, LootItem item, ItemUseResult result)
    {
        if (result.Success)
        {
            _logger.LogInformation("Player {PlayerName} successfully used {ItemName}: {Message}",
                player.PlayerName, item.ItemName, result.Message);

            // Aplicar efectos de estado si los hay
            if (result.AppliedEffects.Any())
            {
                foreach (var effect in result.AppliedEffects)
                {
                    _combatSystem.ApplyStatusEffect(player, effect);
                }
            }
        }
        else
        {
            _logger.LogWarning("Player {PlayerName} failed to use {ItemName}: {Error}",
                player.PlayerName, item.ItemName, result.ErrorMessage);
        }
    }

    // =============================================
    // EVENT HANDLERS DEL MOVEMENT SYSTEM (sin cambios)
    // =============================================

    private void HandleRoomChanged(RealTimePlayer player, string oldRoomId, string newRoomId)
    {
        _logger.LogDebug("Player {PlayerName} transitioned from room {OldRoom} to {NewRoom}",
            player.PlayerName, oldRoomId, newRoomId);
    }

    private void HandlePlayersInRoom(GameWorld world, string roomId, List<RealTimePlayer> playersInRoom)
    {
        var teams = playersInRoom.Select(p => p.TeamId).Distinct().ToList();

        if (teams.Count > 1)
        {
            _logger.LogInformation("PvP encounter in world {WorldId}, room {RoomId}: {PlayerCount} players from {TeamCount} teams",
                world.WorldId, roomId, playersInRoom.Count, teams.Count);
        }
    }

    // =============================================
    // EVENT HANDLERS DEL COMBAT SYSTEM (sin cambios)
    // =============================================

    private void HandleCombatEvent(string worldId, CombatEvent combatEvent)
    {
        AddCombatEvent(worldId, combatEvent);

        // Track DamageDealt / HealingDone on the source player
        if (!string.IsNullOrEmpty(combatEvent.SourceId) && combatEvent.Value > 0)
        {
            var world = FindWorldById(worldId);
            if (world != null && world.Players.TryGetValue(combatEvent.SourceId, out var source))
            {
                switch (combatEvent.EventType)
                {
                    case "damage":
                    case "damage_crit":
                    case "ability_damage":
                    case "ability_damage_crit":
                    case "projectile_hit":
                        source.DamageDealt += combatEvent.Value;
                        break;
                    case "heal":
                        source.HealingDone += combatEvent.Value;
                        break;
                }
            }
        }

        _logger.LogDebug("Combat event in world {WorldId}: {EventType} from {SourceId} to {TargetId} - Value: {Value}",
            worldId, combatEvent.EventType, combatEvent.SourceId, combatEvent.TargetId, combatEvent.Value);
    }

    private void HandlePlayerDeath(RealTimePlayer deadPlayer, RealTimePlayer? killer)
    {
        var world = FindWorldByPlayer(deadPlayer.PlayerId);
        if (world == null) return;

        // Track stats
        deadPlayer.Deaths++;
        if (killer != null)
            killer.Kills++;

        // Usar LootSystem para drop de items del jugador
        _lootSystem.DropPlayerLoot(deadPlayer, world);

        // XP for killer
        if (killer != null)
        {
            killer.ExperiencePoints += 150;
            CheckLevelUp(killer);

            // Assist XP for teammates within 15 units
            foreach (var ally in world.Players.Values)
            {
                if (ally.PlayerId == killer.PlayerId || !ally.IsAlive) continue;
                if (ally.TeamId != killer.TeamId) continue;
                if (GameMathUtils.Distance(ally.Position, deadPlayer.Position) <= 15f)
                {
                    ally.ExperiencePoints += 75;
                    CheckLevelUp(ally);
                }
            }
        }

        // Add death combat event
        AddCombatEvent(world.WorldId, new CombatEvent
        {
            EventType = "player_death",
            SourceId = killer?.PlayerId ?? "",
            TargetId = deadPlayer.PlayerId,
            Value = 0,
            Position = deadPlayer.Position,
            RoomId = deadPlayer.CurrentRoomId
        });

        // Check team elimination
        CheckTeamElimination(world, deadPlayer.TeamId);
    }

    // =============================================
    // ⭐ REFACTORIZADO: INICIALIZACIÓN CON MOB AI SYSTEM
    // =============================================

    private void InitializeMobTemplates()
    {
        // Crear templates básicos o cargar desde configuración
        var mobTemplates = new Dictionary<string, MobTemplate>();

        // Los templates se pueden cargar desde la configuración o crear aquí
        // El MobAISystem ya tiene templates por defecto en InitializeDefaultTemplates()

        //_mobAISystem.InitializeMobTemplates(mobTemplates);

        _logger.LogInformation("Initialized mob templates for AI system");
    }

    // =============================================
    // ⭐ REMOVED: InitializeLootTables() moved to WorldManager
    // =============================================
    // Loot tables are now initialized in WorldManager
    // and used directly by LootSystem

    // =============================================
    // PUBLIC API METHODS (sin cambios)
    // =============================================

    /// <summary>
    /// ⭐ REFACTORED: Queue input for processing (delegated to InputProcessor).
    /// </summary>
    public void QueueInput(NetworkMessage input)
    {
        if (input != null)
        {
            _inputProcessor.QueueInput(input);
        }
    }

    // =============================================
    // ⭐ REMOVED: CreateWorld() moved to WorldManager
    // =============================================
    // This method has been replaced by WorldManager.CreateWorld()
    // which is called from HandleLobbyReadyToStart()

    public void RemovePlayerFromWorld(string worldId, string playerId)
    {
        var world = _worldManager.GetWorld(worldId);

        // Handle lobby arena world: remove from both GameWorld and WorldLobby
        if (world != null && world.IsLobbyWorld)
        {
            world.Players.TryRemove(playerId, out _);
            _lobbyManager.RemovePlayerFromLobby(worldId, playerId);
            _movementSystem.CleanupPlayerTracker(playerId);
            _inputProcessor.ClearPlayerInputBuffer(playerId);

            if (world.Players.Count == 0)
            {
                _movementSystem.CleanupWorldData(worldId);
                _worldManager.RemoveWorld(worldId);
                _logger.LogInformation("Removed empty lobby world {WorldId}", worldId);
            }
            return;
        }

        // Regular game world
        if (world != null)
        {
            if (_worldManager.RemovePlayerFromWorld(worldId, playerId))
            {
                _movementSystem.CleanupPlayerTracker(playerId);
                _inputProcessor.ClearPlayerInputBuffer(playerId);

                var updatedWorld = _worldManager.GetWorld(worldId);
                if (updatedWorld != null && updatedWorld.Players.Count == 0)
                {
                    _mobAISystem.CleanupWorldAI(worldId);
                    _lootSystem.CleanupWorldLoot(worldId);
                    _movementSystem.CleanupWorldData(worldId);

                    _worldManager.RemoveWorld(worldId);
                    _logger.LogInformation("Removed empty world {WorldId}", worldId);
                }
            }
            return;
        }

        // Fallback: lobby without arena world
        if (_lobbyManager.IsLobby(worldId))
        {
            if (_lobbyManager.RemovePlayerFromLobby(worldId, playerId))
            {
                _movementSystem.CleanupPlayerTracker(playerId);
                _inputProcessor.ClearPlayerInputBuffer(playerId);
            }
        }
    }

    public List<string> GetAvailableWorlds(int maxPlayersPerWorld)
    {
        // ⭐ REFACTORED: Delegate to LobbyManager
        return _lobbyManager.GetAvailableLobbies(maxPlayersPerWorld);
    }

    /// <summary>
    /// Get world updates for all active worlds.
    /// ⚠️ IMPORTANT: Caller MUST call ReturnWorldUpdateToPool() after serialization to prevent memory leaks!
    /// </summary>
    /// <returns>Dictionary of world updates (uses object pooling)</returns>
    public Dictionary<string, WorldUpdateMessage> GetWorldUpdates()
    {
        var updates = new Dictionary<string, WorldUpdateMessage>();

        // ⭐ REFACTORED: Use WorldManager to get all worlds
        foreach (var world in _worldManager.GetAllWorlds())
        {
            var update = CreateWorldUpdate(world);
            updates[world.WorldId] = update;
        }

        return updates;
    }

    public Dictionary<string, WorldStateMessage> GetWorldStates()
    {
        var worldStates = new Dictionary<string, WorldStateMessage>();

        // ⭐ REFACTORED: Use WorldManager to get all worlds
        foreach (var world in _worldManager.GetAllWorlds())
        {
            worldStates[world.WorldId] = CreateWorldStateMessage(world);
        }

        return worldStates;
    }

    public void ForceCompleteWorld(string worldId)
    {
        // ⭐ REFACTORED: Use WorldManager to get world
        var world = _worldManager.GetWorld(worldId);
        if (world != null)
        {
            world.IsCompleted = true;

            foreach (var extraction in world.ExtractionPoints.Values)
            {
                extraction.IsActive = true;
            }

            _logger.LogWarning("World {WorldId} forcibly completed by admin", worldId);
        }
        else
        {
            throw new InvalidOperationException($"World {worldId} not found");
        }
    }

    public Dictionary<string, object> GetServerStats()
    {
        // ⭐ REFACTORED: Use managers to get stats
        var worlds = _worldManager.GetAllWorlds();
        var totalPlayers = worlds.Sum(w => w.Players.Count);
        var alivePlayers = worlds.Sum(w => w.Players.Values.Count(p => p.IsAlive));
        var totalMobs = worlds.Sum(w => w.Mobs.Count);
        var totalLoot = worlds.Sum(w => w.AvailableLoot.Count);

        // Incluir estadísticas de sistemas especializados
        var movementStats = _movementSystem.GetDetailedMovementStats();
        var lootStats = _lootSystem.GetDetailedLootAnalytics();
        var aiStats = _mobAISystem.GetDetailedAIAnalytics(); // ⭐ NUEVO

        var stats = new Dictionary<string, object>
        {
            ["FrameNumber"] = _frameNumber,
            ["WorldCount"] = worlds.Count,
            ["TotalPlayers"] = totalPlayers,
            ["AlivePlayers"] = alivePlayers,
            ["TotalMobs"] = totalMobs,
            ["TotalLoot"] = totalLoot,
            ["InputQueueSize"] = _inputProcessor.GetQueueSize(),
            ["TargetFPS"] = _settings.TargetFPS,
            ["AveragePlayersPerWorld"] = worlds.Count > 0 ? (double)totalPlayers / worlds.Count : 0,
            ["CompletedWorlds"] = worlds.Count(w => w.IsCompleted),
            ["RecentCombatEvents"] = _recentCombatEvents.Values.Sum(list => list.Count),
            ["RecentLootUpdates"] = _recentLootUpdates.Values.Sum(list => list.Count),
            ["MovementStats"] = movementStats,
            ["LootStats"] = lootStats,
            ["AIStats"] = aiStats // ⭐ NUEVO
        };

        return stats;
    }

    // =============================================
    // ⭐ OPTIMIZED: GAME LOOP WITH PARALLEL WORLD PROCESSING
    // =============================================

    private void GameLoop(object? state)
    {
        try
        {
            var frameStart = DateTime.UtcNow;
            Interlocked.Increment(ref _frameNumber);

            var deltaTime = (frameStart - _lastFrameTime).TotalSeconds;
            _lastFrameTime = frameStart;

            deltaTime = Math.Min(deltaTime, 1.0 / 30.0);

            ProcessInputQueue();

            // ⭐ PERF: Get snapshot of worlds to process (minimal lock time)
            // ⭐ REFACTORED: Use WorldManager to get worlds
            var worldsSnapshot = _worldManager.GetAllWorlds().ToArray();

            // ⭐ PERF: Process worlds in PARALLEL for massive speedup
            // With 8 worlds @ 5ms each: Sequential=40ms, Parallel=5ms (8x faster!)
            if (worldsSnapshot.Length > 0)
            {
                Parallel.ForEach(worldsSnapshot, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount // Use all CPU cores
                }, world =>
                {
                    try
                    {
                        UpdateWorld(world, (float)deltaTime);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating world {WorldId}", world.WorldId);
                    }
                });
            }

            // Optimización periódica de sistemas
            if (_frameNumber % (_settings.TargetFPS * 60) == 0) // Cada minuto
            {
                _movementSystem.OptimizeMemoryUsage();
                _mobAISystem.OptimizeMemoryUsage(); // ⭐ NUEVO
            }

            var frameTime = (DateTime.UtcNow - frameStart).TotalMilliseconds;
            var targetFrameTime = 1000.0 / _settings.TargetFPS;

            if (frameTime > targetFrameTime * 1.5)
            {
                _logger.LogWarning("Frame {FrameNumber} took {FrameTime:F2}ms (target: {TargetTime:F2}ms)",
                    _frameNumber, frameTime, targetFrameTime);
            }

            if (_frameNumber % (_settings.TargetFPS * 30) == 0)
            {
                LogPerformanceStats();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in game loop frame {FrameNumber}", _frameNumber);
        }
    }

    private void UpdateWorld(GameWorld world, float deltaTime)
    {
        // Skip update for ended matches
        if (world.MatchEnded) return;

        // Movement + collision for all worlds (including lobby)
        _movementSystem.UpdateAllPlayersMovement(world, deltaTime);

        var currentServerTime = (float)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        foreach (var player in world.Players.Values)
        {
            if (player.IsAlive) player.RecordPosition(currentServerTime);
        }

        _movementSystem.ProcessCollisions(world);

        // Lobby arena: only movement/collision + auto-respawn, skip game systems
        if (world.IsLobbyWorld)
        {
            ProcessLobbyRespawn(world);
            return;
        }

        // Full game systems (non-lobby only)
        _mobAISystem.UpdateMobs(world, deltaTime);

        _combatSystem.UpdateStatusEffects(world.Players.Values, deltaTime);
        _combatSystem.UpdateChanneling(world, deltaTime);
        _combatSystem.ProjectileSystem.UpdateProjectiles(world, deltaTime);

        UpdateCombatStates(world, deltaTime);
        CheckRoomTransitions(world);
        UpdateDoors(world);

        _lootSystem.ProcessLootSpawning(world, deltaTime);
        _lootSystem.CleanupExpiredContainers(world);

        UpdateCorruption(world);

        ProcessExtractionSystem(world, deltaTime);
        CheckRoomCompletion(world);
        CheckWinConditions(world);
        CheckMatchEnd(world);
    }

    /// <summary>
    /// Auto-respawn dead players in lobby after 3 seconds.
    /// </summary>
    private void ProcessLobbyRespawn(GameWorld world)
    {
        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive)
            {
                var timeDead = (DateTime.UtcNow - player.DeathTime).TotalSeconds;
                if (timeDead >= 3.0)
                {
                    player.IsAlive = true;
                    player.Health = player.MaxHealth;
                    player.Mana = player.MaxMana;
                    player.Position = _worldManager.GetLobbySpawnPosition(player.TeamId);
                    player.Velocity = Vector2.Zero;
                    player.MoveTarget = null;
                    player.IsPathing = false;
                    player.IsMoving = false;

                    // Equip free T1 starting gear if player has no equipment (died and lost everything)
                    if (player.Equipment.Count == 0)
                    {
                        _equipmentSystem.EquipStartingGear(player);
                    }

                    player.ForceNextUpdate();
                }
            }
        }
    }

    // =============================================
    // CORRUPTION ZONE SYSTEM
    // =============================================

    private void UpdateCorruption(GameWorld world)
    {
        var balance = _settings.GameBalance;
        var elapsed = (DateTime.UtcNow - world.CreatedAt).TotalSeconds;

        // Don't start corruption before delay
        if (elapsed < balance.CorruptionDelaySeconds) return;

        var now = DateTime.UtcNow;

        // Initialize corruption on first call past delay
        if (world.CorruptionWave == 0)
        {
            world.CorruptionWave = 1;
            world.CorruptionStartTime = now;
            world.NextWaveTime = now.AddSeconds(balance.CorruptionWaveIntervalSeconds);
            CorruptRoomsByDistance(world, 2); // Edge rooms (Chebyshev dist 2)
            return;
        }

        // Advance wave when timer expires (max 3 waves)
        if (world.CorruptionWave < 3 && now >= world.NextWaveTime)
        {
            world.CorruptionWave++;
            world.NextWaveTime = now.AddSeconds(balance.CorruptionWaveIntervalSeconds);
            world.WarningRooms.Clear();

            int targetDistance = 3 - world.CorruptionWave; // Wave 2→dist 1, Wave 3→dist 0
            CorruptRoomsByDistance(world, targetDistance);
        }

        // Warning: mark rooms about to corrupt 30s before next wave
        if (world.CorruptionWave < 3 && world.WarningRooms.Count == 0)
        {
            var timeUntilWave = (world.NextWaveTime - now).TotalSeconds;
            if (timeUntilWave <= balance.CorruptionWarningSeconds)
            {
                int nextTargetDist = 3 - (world.CorruptionWave + 1);
                foreach (var room in world.Rooms.Values)
                {
                    if (world.CorruptedRooms.Contains(room.RoomId)) continue;
                    if (GetRoomChebyshevDistance(room.RoomId) == nextTargetDist)
                    {
                        world.WarningRooms.Add(room.RoomId);
                        AddCombatEvent(world.WorldId, new CombatEvent
                        {
                            EventType = "corruption_warning",
                            RoomId = room.RoomId
                        });
                    }
                }
            }
        }

        // Damage tick: once per second
        if ((now - world.LastCorruptionDamageTick).TotalSeconds >= 1.0)
        {
            world.LastCorruptionDamageTick = now;
            int damage = (int)(balance.CorruptionDamageBase * Math.Pow(balance.CorruptionDamageScale, world.CorruptionWave - 1));

            foreach (var player in world.Players.Values)
            {
                if (!player.IsAlive) continue;
                if (!world.CorruptedRooms.Contains(player.CurrentRoomId)) continue;

                player.Health = Math.Max(0, player.Health - damage);
                if (player.Health <= 0)
                {
                    player.IsAlive = false;
                    player.DeathTime = now;
                }

                AddCombatEvent(world.WorldId, new CombatEvent
                {
                    EventType = "corruption_damage",
                    TargetId = player.PlayerId,
                    Value = damage,
                    Position = player.Position,
                    RoomId = player.CurrentRoomId
                });
            }
        }
    }

    private void CorruptRoomsByDistance(GameWorld world, int targetDistance)
    {
        // Collect rooms that will be corrupted this wave
        var newlyCorrupted = new HashSet<string>();
        foreach (var room in world.Rooms.Values)
        {
            if (world.CorruptedRooms.Contains(room.RoomId)) continue;
            if (room.RoomType is "spawn" or "extraction_zone") continue;

            if (GetRoomChebyshevDistance(room.RoomId) >= targetDistance)
                newlyCorrupted.Add(room.RoomId);
        }

        // Auto-unlock doors BEFORE corrupting, so players aren't trapped
        AutoUnlockDoorsForCorruption(world, newlyCorrupted);

        // Now apply corruption
        foreach (var roomId in newlyCorrupted)
        {
            world.CorruptedRooms.Add(roomId);
            world.WarningRooms.Remove(roomId);

            AddCombatEvent(world.WorldId, new CombatEvent
            {
                EventType = "room_corrupted",
                RoomId = roomId
            });

            _logger.LogInformation("Room {RoomId} corrupted (wave {Wave})",
                roomId, world.CorruptionWave);
        }
    }

    /// <summary>
    /// Auto-unlock doors when corruption would trap a player (all exits either corrupted or locked).
    /// </summary>
    private void AutoUnlockDoorsForCorruption(GameWorld world, HashSet<string> newlyCorrupted)
    {
        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive) continue;
            var playerRoom = player.CurrentRoomId;

            // Skip players already in corrupted rooms — they need to move regardless
            if (world.CorruptedRooms.Contains(playerRoom) || newlyCorrupted.Contains(playerRoom))
                continue;

            if (!world.Rooms.TryGetValue(playerRoom, out var room)) continue;

            // Collect locked doors blocking non-corrupted exits
            var lockedExitDoors = new List<LockedDoor>();
            bool hasOpenSafeExit = false;

            foreach (var connectedRoomId in room.Connections)
            {
                // Is this exit safe (not corrupted/about-to-be-corrupted)?
                bool isSafe = !world.CorruptedRooms.Contains(connectedRoomId)
                              && !newlyCorrupted.Contains(connectedRoomId);
                if (!isSafe) continue;

                // Check if a locked door blocks this exit
                var connectionId = string.Compare(playerRoom, connectedRoomId, StringComparison.Ordinal) < 0
                    ? $"{playerRoom}_{connectedRoomId}" : $"{connectedRoomId}_{playerRoom}";

                if (world.LockedDoors.TryGetValue(connectionId, out var door) && door.IsLocked)
                {
                    lockedExitDoors.Add(door);
                }
                else
                {
                    hasOpenSafeExit = true;
                    break; // At least one safe unlocked exit exists
                }
            }

            // If ALL safe exits are locked → auto-unlock them
            if (!hasOpenSafeExit && lockedExitDoors.Count > 0)
            {
                foreach (var door in lockedExitDoors)
                {
                    door.IsLocked = false;
                    door.UnlockedByPlayerId = "corruption_system";
                    door.UnlockedAt = DateTime.UtcNow;

                    AddCombatEvent(world.WorldId, new CombatEvent
                    {
                        EventType = "door_unlocked",
                        SourceId = "corruption_system",
                        TargetId = door.ConnectionId,
                        RoomId = playerRoom,
                        AbilityId = door.ConnectionId
                    });

                    _logger.LogInformation("Auto-unlocked door {ConnectionId} to prevent trapping player {PlayerName}",
                        door.ConnectionId, player.PlayerName);
                }
            }
        }
    }

    /// <summary>
    /// Extracts grid coordinates from a room ID (e.g., "room_2_3" → x=2, y=3)
    /// and returns the Chebyshev distance from the grid center (2,2 for a 5x5 grid).
    /// </summary>
    private static int GetRoomChebyshevDistance(string roomId)
    {
        var parts = roomId.Split('_');
        if (parts.Length >= 3 && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var y))
        {
            return Math.Max(Math.Abs(x - 2), Math.Abs(y - 2));
        }
        return 0; // Unknown room, treat as center
    }

    // =============================================
    // INPUT PROCESSING (sin cambios grandes)
    // =============================================

    /// <summary>
    /// ⭐ REFACTORED: Process input queue (delegated to InputProcessor).
    /// InputProcessor will trigger events that call our process methods.
    /// </summary>
    private void ProcessInputQueue()
    {
        _inputProcessor.ProcessInputQueue();
    }

    // =============================================
    // PLAYER INPUT PROCESSING (sin cambios)
    // =============================================

    private void ProcessPlayerInput(RealTimePlayer player, PlayerInputMessage input)
    {
        if (!player.IsAlive) return;

        var world = FindWorldByPlayer(player.PlayerId);

        // Fallback: player not in any world (shouldn't happen with lobby worlds)
        if (world == null)
        {
            ProcessLobbyMovement(player, input);
            return;
        }

        // Process movement (works for both lobby and game worlds — includes wall collision)
        var movementResult = _movementSystem.UpdatePlayerMovement(player, input, world, 1.0f / _settings.TargetFPS);

        if (!movementResult.Success && !string.IsNullOrEmpty(movementResult.ErrorMessage))
        {
            _logger.LogWarning("Movement failed for {PlayerName}: {Error}",
                player.PlayerName, movementResult.ErrorMessage);

            _movementSystem.FlagPlayerForMonitoring(player.PlayerId, movementResult.ErrorMessage);
        }

        player.Direction = input.AimDirection;

        // Combat only in game worlds (not lobby)
        if (!world.IsLobbyWorld)
        {
            if (input.IsAttacking && _combatSystem.CanAttack(player))
            {
                ProcessAttack(player, input.TargetEntityId);
            }

            if (!string.IsNullOrEmpty(input.AbilityType))
            {
                ProcessAbility(player, input.AbilityType, input.AbilityTarget);
            }
        }

        player.LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Process movement for players in lobby (tavern area).
    /// Click-to-move steering without world collision - no combat allowed.
    /// </summary>
    private void ProcessLobbyMovement(RealTimePlayer player, PlayerInputMessage input)
    {
        var deltaTime = 1.0f / _settings.TargetFPS;

        // Click-to-move: process new target
        if (input.HasMoveTarget)
        {
            var target = input.MoveTarget;
            target.X = Math.Clamp(target.X, 0f, 240f);
            target.Y = Math.Clamp(target.Y, 0f, 240f);
            player.MoveTarget = target;
            player.IsPathing = true;
        }

        if (input.StopMovement)
        {
            player.MoveTarget = null;
            player.IsPathing = false;
        }

        // Steering toward target
        if (player.IsPathing && player.MoveTarget.HasValue)
        {
            var toTarget = player.MoveTarget.Value - player.Position;
            var distance = toTarget.Magnitude;

            if (distance <= 0.3f)
            {
                player.MoveTarget = null;
                player.IsPathing = false;
                player.Velocity = new Vector2 { X = 0, Y = 0 };
                player.IsMoving = false;
                player.IsSprinting = false;
            }
            else
            {
                var baseSpeed = _settings.GameBalance.MovementSpeed;
                var speed = input.IsSprinting ? baseSpeed * _settings.GameBalance.SprintMultiplier : baseSpeed;
                var direction = toTarget.GetNormalized();
                var velocity = direction * speed;
                var newPosition = player.Position + velocity * deltaTime;

                newPosition.X = Math.Clamp(newPosition.X, 0f, 240f);
                newPosition.Y = Math.Clamp(newPosition.Y, 0f, 240f);

                player.Position = newPosition;
                player.Velocity = velocity;
                player.IsMoving = true;
                player.IsSprinting = input.IsSprinting;
            }
        }
        else
        {
            player.Velocity = new Vector2 { X = 0, Y = 0 };
            player.IsMoving = false;
            player.IsSprinting = false;
        }

        player.Direction = input.AimDirection;
        player.LastActivity = DateTime.UtcNow;
    }

    private void ProcessLootGrab(RealTimePlayer player, LootGrabMessage lootGrab)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null || !player.IsAlive) return;

        try
        {
            var result = _lootSystem.ProcessLootGrab(player, lootGrab.LootId, world);

            if (result.Success)
            {
                _logger.LogInformation("Player {PlayerName} grabbed {ItemName}",
                    player.PlayerName, result.GrabbedItem!.ItemName);

                // Notify network layer so it sends updated inventory to the player
                OnInventoryChanged?.Invoke(world.WorldId, player);
            }
            else
            {
                _logger.LogDebug("Loot grab failed for {PlayerName}: {Error}",
                    player.PlayerName, result.ErrorMessage);
            }

            player.LastActivity = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing loot grab for player {PlayerId}", player.PlayerId);
        }
    }

    private void ProcessContainerGrab(RealTimePlayer player, ContainerGrabMessage containerGrab)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null || !player.IsAlive) return;

        _logger.LogInformation("🎯 Container grab request: Player={PlayerName}, Container={ContainerId}, LootId={LootId}",
            player.PlayerName, containerGrab.ContainerId, containerGrab.LootId);

        try
        {
            var result = _lootSystem.GrabFromContainer(player, containerGrab.ContainerId, containerGrab.LootId, world);

            if (result.Success)
            {
                player.ContainersLooted++;

                _logger.LogInformation("✅ Player {PlayerName} grabbed {ItemName} from container {ContainerId}",
                    player.PlayerName, result.GrabbedItem!.ItemName, containerGrab.ContainerId);

                OnInventoryChanged?.Invoke(world.WorldId, player);
            }
            else
            {
                _logger.LogWarning("❌ Container grab failed for {PlayerName}: {Error} (Container={ContainerId}, LootId={LootId})",
                    player.PlayerName, result.ErrorMessage, containerGrab.ContainerId, containerGrab.LootId);

                // "Item not in container" is a harmless race condition (double-click) — don't show to player
                if (result.ErrorMessage != "Item not in container")
                    OnPlayerError?.Invoke(world.WorldId, player, result.ErrorMessage ?? "Cannot grab item");
            }

            player.LastActivity = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing container grab for player {PlayerId}", player.PlayerId);
        }
    }

    private void ProcessChat(RealTimePlayer player, ChatMessage chat)
    {
        _logger.LogInformation("Chat from {PlayerName} [{ChatType}]: {Message}",
            player.PlayerName, chat.ChatType, chat.Message);
        player.LastActivity = DateTime.UtcNow;
    }

    private void ProcessPingMarker(RealTimePlayer player, PingMarkerMessage ping)
    {
        player.LastActivity = DateTime.UtcNow;
    }

    private void ProcessUseItem(RealTimePlayer player, UseItemMessage useItem)
    {
        if (!player.IsAlive) return;

        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null) return;

        var item = player.Inventory.FirstOrDefault(i => i.LootId == useItem.ItemId);
        if (item == null)
        {
            // Normal race condition: client sent use_item before receiving inventory update
            _logger.LogDebug("Player {PlayerName} tried to use already-consumed item {ItemId}",
                player.PlayerName, useItem.ItemId);
            // Resync inventory so client removes the stale item
            OnInventoryChanged?.Invoke(world.WorldId, player);
            return;
        }

        var result = _lootSystem.UseItem(player, item, world);

        if (result.Success && result.ItemConsumed)
        {
            if (item.StackCount > 1)
            {
                item.StackCount--;
            }
            else
            {
                player.Inventory.Remove(item);
            }
            RecalculatePlayerWeight(player);
            OnInventoryChanged?.Invoke(world.WorldId, player);
        }
        else if (!result.Success)
        {
            OnPlayerError?.Invoke(world.WorldId, player, result.ErrorMessage ?? "Cannot use item");
        }

        // Emit door_unlocked event so clients can update visuals
        if (result.Success && !string.IsNullOrEmpty(result.UnlockedConnectionId))
        {
            AddCombatEvent(world.WorldId, new CombatEvent
            {
                EventType = "door_unlocked",
                SourceId = player.PlayerId,
                TargetId = result.UnlockedConnectionId,
                Position = player.Position,
                RoomId = player.CurrentRoomId,
                AbilityId = result.UnlockedConnectionId
            });
        }

        _logger.LogInformation("Player {PlayerName} used item {ItemName}: {Success}",
            player.PlayerName, item.ItemName, result.Success ? "Success" : $"Failed - {result.ErrorMessage}");
    }

    private static void RecalculatePlayerWeight(RealTimePlayer player)
    {
        player.CurrentWeight = MazeWars.GameServer.Services.Loot.LootSystem.CalculatePlayerWeight(player);
    }

    private async void ProcessAttack(RealTimePlayer attacker, string targetEntityId = "")
    {
        try
        {
            var world = FindWorldByPlayer(attacker.PlayerId);
            if (world == null) return;

            // If a specific target is selected, try to attack only that target
            bool hitSpecificTarget = false;

            if (!string.IsNullOrEmpty(targetEntityId))
            {
                // Check if target is a player
                if (world.Players.TryGetValue(targetEntityId, out var targetPlayer) &&
                    targetPlayer.IsAlive && targetPlayer.TeamId != attacker.TeamId)
                {
                    var singleResult = await _combatSystem.ProcessAttack(attacker, new List<RealTimePlayer> { targetPlayer }, world);
                    if (singleResult.Success || singleResult.IsRangedProjectile) return;
                }

                // Check if target is a mob
                if (world.Mobs.TryGetValue(targetEntityId, out var targetMob) && targetMob.Health > 0)
                {
                    // Ranged: fire projectile toward target (ProcessAttack handles it)
                    var singleResult = await _combatSystem.ProcessAttack(attacker, new List<RealTimePlayer>(), world);
                    if (singleResult.IsRangedProjectile) return;

                    // Melee: attack the specific mob only
                    var mobResult = _combatSystem.ProcessAttackAgainstMobs(attacker, new List<Mob> { targetMob }, world);
                    if (mobResult.Success) return;
                }
            }

            // Fallback: no specific target or target not found — attack all in range
            var potentialTargets = world.Players.Values
                .Where(p => p.IsAlive && p.TeamId != attacker.TeamId)
                .ToList();

            var fallbackResult = await _combatSystem.ProcessAttack(attacker, potentialTargets, world);

            // For ranged projectile attacks, the projectile handles mob targeting too
            if (!fallbackResult.IsRangedProjectile)
            {
                // Melee: also attack mobs (uses same attack frame, no extra CanAttack check)
                var potentialMobs = world.Mobs.Values
                    .Where(m => m.Health > 0)
                    .ToList();

                if (potentialMobs.Any())
                {
                    var mobResult = _combatSystem.ProcessAttackAgainstMobs(attacker, potentialMobs, world);
                    if (mobResult.Success)
                        fallbackResult.Success = true;
                }
            }

            if (!fallbackResult.Success && !string.IsNullOrEmpty(fallbackResult.ErrorMessage))
            {
                _logger.LogDebug("Attack failed for {PlayerName}: {Error}",
                    attacker.PlayerName, fallbackResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing attack for {PlayerName}", attacker.PlayerName);
        }
    }

    private async void ProcessAbility(RealTimePlayer player, string abilityType, Vector2 target)
    {
        try
        {
            // Pre-validation: player must be alive and not stunned
            if (!player.IsAlive) return;
            if (player.StatusEffects.Any(e => e.EffectType == "stun")) return;

            var world = FindWorldByPlayer(player.PlayerId);
            if (world == null) return;

            // Validate target position is within reasonable distance (max ability range + generous margin)
            var distToTarget = GameMathUtils.Distance(player.Position, target);
            if (distToTarget > 50f) return; // Reject absurd targets

            var result = await _combatSystem.ProcessAbility(player, abilityType, target, world);

            if (result.Success)
            {
                _logger.LogInformation("Player {PlayerName} used ability {AbilityType}: {Message}",
                    player.PlayerName, abilityType, result.Message);
            }
            else
            {
                _logger.LogDebug("Ability {AbilityType} failed for {PlayerName}: {Error}",
                    abilityType, player.PlayerName, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ability {AbilityType} for {PlayerName}", abilityType, player.PlayerName);
        }
    }

    private void ProcessExtraction(RealTimePlayer player, ExtractionMessage extraction)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null || !player.IsAlive) return;

        if (!world.ExtractionPoints.TryGetValue(extraction.ExtractionId, out var extractionPoint)) return;

        switch (extraction.Action.ToLower())
        {
            case "start":
                var distance = GameMathUtils.Distance(player.Position, extractionPoint.Position);
                if (distance <= 5.0f && extractionPoint.IsActive)
                {
                    StartPlayerExtraction(world, player, extractionPoint);
                }
                break;

            case "cancel":
                CancelPlayerExtraction(world, player.PlayerId, extractionPoint);
                break;
        }
    }

    private void ProcessTradeRequest(RealTimePlayer player, TradeRequestMessage tradeRequest)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null || !player.IsAlive) return;

        var targetPlayer = world.Players.Values
            .FirstOrDefault(p => p.PlayerId == tradeRequest.TargetPlayerId);

        if (targetPlayer == null || !targetPlayer.IsAlive) return;

        var distance = GameMathUtils.Distance(player.Position, targetPlayer.Position);
        if (distance > 5.0f) return;

        _logger.LogInformation("Trade request from {PlayerName} to {TargetName}: {OfferedCount} items",
            player.PlayerName, targetPlayer.PlayerName, tradeRequest.OfferedItemIds.Count);
    }

    // =============================================
    // ⭐ REFACTORIZADO: ROOM SYSTEM SIN LÓGICA DE MOBS
    // =============================================

    private void CheckRoomTransitions(GameWorld world)
    {
        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive) continue;

            var transitionResult = _movementSystem.CheckRoomTransition(player, world);

            if (transitionResult.RoomChanged)
            {
                CheckRoomForSpecialEvents(world, transitionResult.NewRoomId!, player);
            }
        }
    }

    private void CheckRoomForSpecialEvents(GameWorld world, string roomId, RealTimePlayer player)
    {
        if (world.Rooms.TryGetValue(roomId, out var room))
        {
            if (room.RoomId.Contains("boss"))
            {
                _logger.LogInformation("Player {PlayerName} entered boss room {RoomId}!",
                    player.PlayerName, roomId);
                
                // ⭐ NUEVO: Notificar al AI system sobre entrada a sala de jefe
                _mobAISystem.ProcessBossAI(world.Mobs.Values.FirstOrDefault(m => m.RoomId == roomId && m.MobType.Contains("boss")), world, 0.1f);
            }
        }
    }

    // =============================================
    // REVIVAL ALTAR SYSTEM - Soul pickup and altar channeling
    // =============================================

    /// <summary>
    /// Process a player picking up a dead teammate's soul.
    /// Player must be within 2.0 range of the dead ally's body.
    /// Carrying a soul applies a 20% movement speed penalty.
    /// </summary>
    public void ProcessPickupSoul(RealTimePlayer player, string targetPlayerId)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null || !player.IsAlive)
        {
            OnPlayerError?.Invoke(FindWorldIdByPlayer(player.PlayerId), player, "Cannot pick up soul");
            return;
        }

        // Check player isn't already carrying a soul
        if (player.CarryingSoulOfPlayerId != null)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Already carrying a soul");
            return;
        }

        // Find the dead ally
        if (!world.Players.TryGetValue(targetPlayerId, out var deadAlly))
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Player not found");
            return;
        }

        // Must be dead
        if (deadAlly.IsAlive)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Player is not dead");
            return;
        }

        // Must be on the same team
        if (deadAlly.TeamId != player.TeamId)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Cannot pick up enemy soul");
            return;
        }

        // Check range (2.0 units)
        var distance = GameMathUtils.Distance(player.Position, deadAlly.Position);
        if (distance > 2.0f)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Too far from body");
            return;
        }

        // Check nobody else is already carrying this soul
        var alreadyCarried = world.Players.Values.Any(p =>
            p.PlayerId != player.PlayerId && p.CarryingSoulOfPlayerId == targetPlayerId);
        if (alreadyCarried)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Soul already being carried");
            return;
        }

        // Pick up the soul
        player.CarryingSoulOfPlayerId = targetPlayerId;

        // Apply 20% speed penalty
        player.MovementSpeedModifier *= 0.8f;

        _logger.LogInformation("Player {Carrier} picked up soul of {Dead}",
            player.PlayerName, deadAlly.PlayerName);

        AddCombatEvent(world.WorldId, new CombatEvent
        {
            EventType = "soul_picked_up",
            SourceId = player.PlayerId,
            TargetId = targetPlayerId,
            Value = 0,
            Position = player.Position,
            RoomId = player.CurrentRoomId
        });
    }

    /// <summary>
    /// Process a player starting the altar revive channel.
    /// Player must be carrying a soul and within 2.0 range of the altar.
    /// Channeling takes 2 seconds.
    /// </summary>
    public void ProcessAltarRevive(RealTimePlayer player, string altarId)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null || !player.IsAlive)
        {
            OnPlayerError?.Invoke(FindWorldIdByPlayer(player.PlayerId), player, "Cannot use altar");
            return;
        }

        // Must be carrying a soul
        if (player.CarryingSoulOfPlayerId == null)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Not carrying a soul");
            return;
        }

        // Already channeling?
        if (player.ChannelingAbility != null)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Already channeling");
            return;
        }

        if (player.IsCasting)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Cannot use altar while casting");
            return;
        }

        // Find the altar
        if (!world.RevivalAltars.TryGetValue(altarId, out var altar))
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Altar not found");
            return;
        }

        if (!altar.IsActive)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Altar is inactive");
            return;
        }

        // Check range (2.0 units)
        var distance = GameMathUtils.Distance(player.Position, altar.Position);
        if (distance > 2.0f)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Too far from altar");
            return;
        }

        // Start channeling
        player.ChannelingAbility = "altar_revive";
        player.ChannelingTargetId = altarId;
        player.ChannelingStartTime = DateTime.UtcNow;
        player.ChannelingDuration = 2.0f;
        player.IsMoving = false;
        player.MoveTarget = null;
        player.IsPathing = false;

        _logger.LogInformation("Player {PlayerName} started altar revive channel at {AltarId} (carrying soul of {SoulId})",
            player.PlayerName, altarId, player.CarryingSoulOfPlayerId);

        AddCombatEvent(world.WorldId, new CombatEvent
        {
            EventType = "channeling_started",
            SourceId = player.PlayerId,
            TargetId = altarId,
            Value = 20, // 2.0s * 10
            AbilityId = "Altar Revive",
            Position = player.Position,
            RoomId = player.CurrentRoomId
        });
    }

    // =============================================
    // DOOR SYSTEM - Channeling doors (Dark and Darker style)
    // =============================================

    /// <summary>
    /// Process a door interaction request from a player. Starts channeling to open the door.
    /// Called directly from NetworkService (not queued through InputProcessor).
    /// </summary>
    public void ProcessDoorInteract(RealTimePlayer player, string doorId)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null || !player.IsAlive)
        {
            OnPlayerError?.Invoke(FindWorldIdByPlayer(player.PlayerId), player, "Cannot interact with door");
            return;
        }

        if (!world.Doors.TryGetValue(doorId, out var door))
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Door not found");
            return;
        }

        if (door.IsLocked)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Door is locked and requires a key");
            return;
        }

        if (player.ChannelingAbility != null)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Already channeling");
            return;
        }

        if (player.IsCasting)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Cannot interact while casting");
            return;
        }

        // Calculate door positions at both room entrances
        if (!world.Rooms.TryGetValue(door.RoomIdA, out var roomA) ||
            !world.Rooms.TryGetValue(door.RoomIdB, out var roomB))
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Door rooms not found");
            return;
        }

        // Check distance to door at corridor center
        var doorCenter = CalculateDoorCenter(roomA, roomB);
        var distance = GameMathUtils.Distance(player.Position, doorCenter);

        if (distance > 4.0f)
        {
            OnPlayerError?.Invoke(world.WorldId, player, "Too far from door");
            return;
        }

        // Determine action: open or close
        var action = door.IsOpen ? "door_close" : "door_open";
        var actionLabel = door.IsOpen ? "Closing Door" : "Opening Door";

        // Start channeling (closing is faster — defensive action)
        var channelingTime = door.IsOpen ? door.ClosingDuration : door.ChannelingDuration;
        player.ChannelingAbility = action;
        player.ChannelingTargetId = doorId;
        player.ChannelingStartTime = DateTime.UtcNow;
        player.ChannelingDuration = channelingTime;
        player.IsMoving = false;
        player.MoveTarget = null;
        player.IsPathing = false;

        // Also track on the door itself
        door.ChannelingPlayerId = player.PlayerId;
        door.ChannelingStartTime = DateTime.UtcNow;

        _logger.LogInformation("Player {PlayerName} started channeling to {Action} door {DoorId} ({Duration}s)",
            player.PlayerName, action, doorId, door.ChannelingDuration);

        AddCombatEvent(world.WorldId, new CombatEvent
        {
            EventType = "channeling_started",
            SourceId = player.PlayerId,
            TargetId = doorId,
            Value = (int)channelingTime,
            AbilityId = actionLabel,
            Position = player.Position,
            RoomId = player.CurrentRoomId
        });
    }

    /// <summary>
    /// Calculate door position at the CENTER of the corridor (midpoint between both room walls).
    /// </summary>
    private static Vector2 CalculateDoorCenter(Room roomA, Room roomB)
    {
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

    /// <summary>
    /// Server cleanup: silently close abandoned doors after 90s when no players are in adjacent rooms.
    /// This is NOT gameplay auto-close — open doors are information in PvP.
    /// </summary>
    private void UpdateDoors(GameWorld world)
    {
        foreach (var door in world.Doors.Values)
        {
            if (!door.IsOpen) continue;

            var timeSinceOpened = (DateTime.UtcNow - door.OpenedAt).TotalSeconds;
            if (timeSinceOpened < door.AutoCloseSeconds) continue;

            // Only cleanup if no player is in either connected room or their adjacent rooms
            var doorRooms = new HashSet<string> { door.RoomIdA, door.RoomIdB };

            // Expand to adjacent rooms of door rooms (AOI range)
            if (world.Rooms.TryGetValue(door.RoomIdA, out var rA))
                foreach (var adj in rA.Connections) doorRooms.Add(adj);
            if (world.Rooms.TryGetValue(door.RoomIdB, out var rB))
                foreach (var adj in rB.Connections) doorRooms.Add(adj);

            var anyPlayerInArea = world.Players.Values
                .Any(p => p.IsAlive && doorRooms.Contains(p.CurrentRoomId));

            if (anyPlayerInArea) continue;

            // Silent cleanup — no combat event (no one can see it anyway)
            door.IsOpen = false;
            _logger.LogDebug("Door {DoorId} cleanup-closed (abandoned for {Seconds}s)", door.DoorId, (int)timeSinceOpened);
        }
    }

    private void CheckRoomCompletion(GameWorld world)
    {
        foreach (var room in world.Rooms.Values)
        {
            if (room.IsCompleted) continue;

            // Extraction/spawn rooms have no mobs by design — skip to avoid free XP on enter
            if (room.RoomType is "empty" or "extraction_zone" or "spawn") continue;

            // Check if any alive player is in this room
            string? firstTeamId = null;
            bool hasPlayer = false;
            foreach (var p in world.Players.Values)
            {
                if (p.IsAlive && p.CurrentRoomId == room.RoomId)
                {
                    hasPlayer = true;
                    firstTeamId ??= p.TeamId;
                    break;
                }
            }
            if (!hasPlayer) continue;

            // Check if any alive mob is in this room
            bool hasMob = false;
            foreach (var m in world.Mobs.Values)
            {
                if (m.RoomId == room.RoomId && m.Health > 0)
                {
                    hasMob = true;
                    break;
                }
            }

            if (!hasMob)
            {
                CompleteRoom(world, room, firstTeamId!);
            }
        }
    }

    private void CompleteRoom(GameWorld world, Room room, string completingTeamId)
    {
        room.IsCompleted = true;
        room.CompletedByTeam = completingTeamId;
        room.CompletedAt = DateTime.UtcNow;

        _logger.LogInformation("Room {RoomId} completed by team {TeamId}", room.RoomId, completingTeamId);

        // Only grant XP to team members actually in or adjacent to the room (prevents trail exploit)
        var teamMembers = world.Players.Values
            .Where(p => p.TeamId == completingTeamId && p.IsAlive)
            .Where(p => p.CurrentRoomId == room.RoomId || room.Connections.Contains(p.CurrentRoomId))
            .ToList();

        foreach (var member in teamMembers)
        {
            member.ExperiencePoints += 200;
            CheckLevelUp(member);
        }

        // Usar LootSystem para spawn de loot por completar sala
        _lootSystem.ProcessRoomCompletionLoot(room, world, completingTeamId);

        CheckWorldCompletion(world);
    }

    private void CheckLevelUp(RealTimePlayer player)
    {
        if (player.Level >= 5) return;
        var requiredXP = GameMathUtils.CalculateExperienceRequired(player.Level);

        while (player.ExperiencePoints >= requiredXP && player.Level < 5)
        {
            player.Level++;
            player.ExperiencePoints -= requiredXP;

            // Per-run level bonuses
            player.LevelBonusHealth += 10;
            player.LevelBonusMana += 5;
            player.LevelBonusDamagePercent += 0.02f;
            player.LevelBonusHealingPercent += 0.02f;

            player.MaxHealth += 10;
            player.Health = player.MaxHealth;
            player.MaxMana += 5;
            player.Mana = player.MaxMana;

            player.ForceNextUpdate();

            _logger.LogInformation("Player {PlayerName} leveled up to level {Level}!",
                player.PlayerName, player.Level);

            requiredXP = GameMathUtils.CalculateExperienceRequired(player.Level);
        }
    }

    private void CheckWorldCompletion(GameWorld world)
    {
        var completedRooms = world.Rooms.Values.Count(r => r.IsCompleted);
        var totalRooms = world.Rooms.Count;
        var worldAgeMinutes = (DateTime.UtcNow - world.CreatedAt).TotalMinutes;

        // Escalation phase 1: At 50% rooms cleared, reduce extraction time by 25%
        if (completedRooms >= totalRooms * 0.5 && !world.IsCompleted)
        {
            var reducedTime = (int)(_settings.GameBalance.ExtractionTimeSeconds * 0.75f);
            foreach (var ext in world.ExtractionPoints.Values)
                ext.ExtractionTimeSeconds = Math.Min(ext.ExtractionTimeSeconds, reducedTime);
        }

        // Escalation phase 2: At 80% rooms cleared OR after 10 minutes, close corner extractions
        // Only center extraction remains — forces players toward center
        if ((completedRooms >= totalRooms * 0.8 || worldAgeMinutes >= 10) && !world.IsCompleted)
        {
            world.IsCompleted = true;

            var teamCompletions = world.Rooms.Values
                .Where(r => r.IsCompleted && !string.IsNullOrEmpty(r.CompletedByTeam))
                .GroupBy(r => r.CompletedByTeam)
                .ToDictionary(g => g.Key, g => g.Count());

            if (teamCompletions.Any())
            {
                world.WinningTeam = teamCompletions
                    .OrderByDescending(kvp => kvp.Value)
                    .First().Key;
            }

            // Close corner extraction points, only center remains
            foreach (var ext in world.ExtractionPoints.Values)
            {
                if (ext.ExtractionId != "extract_center")
                    ext.IsActive = false;
            }

            // Center gets faster extraction (reward for surviving to endgame)
            if (world.ExtractionPoints.TryGetValue("extract_center", out var center))
                center.ExtractionTimeSeconds = (int)(_settings.GameBalance.ExtractionTimeSeconds * 0.5f);

            _logger.LogInformation("World {WorldId} entered endgame! Only center extraction active. Winning team: {WinningTeam}",
                world.WorldId, world.WinningTeam);
        }
    }

    private void CheckWinConditions(GameWorld world)
    {
        if (world.IsCompleted) return;

        // Grace period: don't check elimination for first 15 seconds
        if ((DateTime.UtcNow - world.GameStartedAt).TotalSeconds < 15) return;

        // Only check elimination if the game started with multiple teams (PvP)
        // Single-team games (PvE / co-op) end via extraction or match end timer
        if (world.InitialTeamCount < 2) return;

        var aliveTeams = world.Players.Values
            .Where(p => p.IsAlive)
            .Select(p => p.TeamId)
            .Distinct()
            .ToList();

        if (aliveTeams.Count <= 1)
        {
            var winningTeam = aliveTeams.FirstOrDefault() ?? "";
            world.WinningTeam = winningTeam;
            world.IsCompleted = true;

            // On elimination, all extraction points stay active (winner can extract freely)
            _logger.LogInformation("World {WorldId} completed by elimination! Winning team: {WinningTeam}",
                world.WorldId, winningTeam);
        }
    }

    private void CheckTeamElimination(GameWorld world, string teamId)
    {
        var aliveTeamMembers = world.Players.Values
            .Where(p => p.TeamId == teamId && p.IsAlive)
            .Count();

        if (aliveTeamMembers == 0)
        {
            _logger.LogInformation("Team {TeamId} has been eliminated in world {WorldId}",
                teamId, world.WorldId);
        }
    }

    // =============================================
    // MATCH END SYSTEM (Item 5)
    // =============================================

    private void CheckMatchEnd(GameWorld world)
    {
        if (world.MatchEnded || world.IsLobbyWorld) return;

        // Condition 1: No players left (all extracted or disconnected)
        if (world.Players.Count == 0)
        {
            EndMatch(world, "all_extracted");
            return;
        }

        // Condition 2: All remaining players are dead
        var anyAlive = world.Players.Values.Any(p => p.IsAlive);
        if (!anyAlive && world.Players.Count > 0)
        {
            EndMatch(world, "all_dead");
            return;
        }

        // Condition 3: Corruption wave 3 + 60s elapsed → force extract alive players
        if (world.CorruptionWave >= 3)
        {
            var timeSinceLastWave = DateTime.UtcNow - world.NextWaveTime;
            if (timeSinceLastWave.TotalSeconds >= 60)
            {
                // Force-extract all alive players
                var alivePlayers = world.Players.Values.Where(p => p.IsAlive).ToList();
                foreach (var player in alivePlayers)
                {
                    ForceExtractPlayer(world, player);
                }
                EndMatch(world, "time_limit");
            }
        }
    }

    private void EndMatch(GameWorld world, string reason)
    {
        world.MatchEnded = true;
        world.MatchEndTime = DateTime.UtcNow;
        world.MatchEndReason = reason;

        _logger.LogInformation("Match ended in world {WorldId}: {Reason} ({PlayerCount} remaining players)",
            world.WorldId, reason, world.Players.Count);

        // Send match_summary to ALL remaining players (dead players get partial XP)
        foreach (var player in world.Players.Values.ToList())
        {
            var partialXp = player.IsAlive ? 0 : (player.Kills * 50 + player.MobKills * 25);
            var goldEarned = player.IsAlive ? player.Inventory.Sum(i => i.Value) : 0;

            var summary = new MatchSummaryData
            {
                PlayerName = player.PlayerName,
                TeamId = player.TeamId,
                WinningTeam = world.WinningTeam,
                Extracted = false,
                Kills = player.Kills,
                Deaths = player.Deaths,
                DamageDealt = player.DamageDealt,
                HealingDone = player.HealingDone,
                ItemsExtracted = 0,
                ExtractionValue = 0,
                XpGained = partialXp,
                FinalLevel = player.Level,
                GameDurationSeconds = (DateTime.UtcNow - world.CreatedAt).TotalSeconds,
                MobKills = player.MobKills,
                ContainersLooted = player.ContainersLooted,
                GoldEarned = goldEarned
            };

            OnPlayerExtracted?.Invoke(world.WorldId, player, summary);

            // Persist career stats for dead players
            var xp = partialXp;
            var gold = goldEarned;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _playerRepository.UpdateCareerStats(player.PlayerName,
                        player.Kills, player.Deaths, player.DamageDealt, player.HealingDone,
                        extracted: false, xpGained: xp, goldEarned: gold);
                    await _playerRepository.RecordMatch(player.PlayerName, new Data.Models.MatchRecord
                    {
                        MatchStartTime = world.CreatedAt,
                        MatchEndTime = DateTime.UtcNow,
                        Extracted = false,
                        Kills = player.Kills,
                        Deaths = player.Deaths,
                        DamageDealt = player.DamageDealt,
                        HealingDone = player.HealingDone,
                        ItemsExtracted = 0,
                        ExtractionValue = 0,
                        XpGained = xp,
                        GoldEarned = gold
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist end-match data for {Player}", player.PlayerName);
                }
            });
        }

        // Schedule world cleanup after 5 seconds
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            CleanupEndedWorld(world.WorldId);
        });
    }

    private void ForceExtractPlayer(GameWorld world, RealTimePlayer player)
    {
        // Find any extraction point (use first available)
        var extraction = world.ExtractionPoints.Values.FirstOrDefault(e => e.IsActive);
        if (extraction != null)
        {
            CompletePlayerExtraction(world, player.PlayerId, extraction);
        }
    }

    private void CleanupEndedWorld(string worldId)
    {
        _mobAISystem.CleanupWorldAI(worldId);
        _lootSystem.CleanupWorldLoot(worldId);
        _movementSystem.CleanupWorldData(worldId);
        _worldManager.RemoveWorld(worldId);
        _logger.LogInformation("Cleaned up ended world {WorldId}", worldId);
    }

    // =============================================
    // EQUIP FROM STASH (Item 4)
    // =============================================

    public bool EquipFromStash(RealTimePlayer player, string lootId)
    {
        var stash = _stashService.GetStash(player.PlayerName);
        if (stash == null) return false;

        var item = stash.FirstOrDefault(i => i.LootId == lootId);
        if (item == null) return false;

        // Remove from stash
        _stashService.RemoveItem(player.PlayerName, lootId);

        // Add to player inventory
        lock (player.InventoryLock)
        {
            player.Inventory.Add(item);
        }

        // Equip the item
        _equipmentSystem.EquipItem(player, lootId);

        _logger.LogInformation("Player {PlayerName} equipped {ItemName} from stash", player.PlayerName, item.ItemName);
        return true;
    }

    // =============================================
    // COMBAT STATE UPDATES (sin cambios)
    // =============================================

    private void UpdateCombatStates(GameWorld world, float deltaTime)
    {
        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive) continue;

            // HP regen: equipment always active + base OOC regen
            if (player.Health < player.MaxHealth)
            {
                var hpRegen = player.HealthRegenPerSecond; // Equipment regen = always active
                var isOutOfCombat = (DateTime.UtcNow - player.LastDamageTime).TotalSeconds > 5.0;
                if (isOutOfCombat) hpRegen += 2f; // Base OOC regen
                if (hpRegen > 0)
                {
                    player.HealthRegenAccumulator += hpRegen * deltaTime;
                    if (player.HealthRegenAccumulator >= 1f)
                    {
                        var points = (int)player.HealthRegenAccumulator;
                        player.Health = Math.Min(player.MaxHealth, player.Health + points);
                        player.HealthRegenAccumulator -= points;
                    }
                }
            }
            else
            {
                player.HealthRegenAccumulator = 0f;
            }

            // Mana regen: base class rate + equipment
            if (player.Mana < player.MaxMana)
            {
                var totalManaRegen = player.BaseManaRegenPerSecond + player.EquipmentManaRegen;
                if (totalManaRegen > 0)
                {
                    player.ManaRegenAccumulator += totalManaRegen * deltaTime;
                    if (player.ManaRegenAccumulator >= 1f)
                    {
                        var points = (int)player.ManaRegenAccumulator;
                        player.Mana = Math.Min(player.MaxMana, player.Mana + points);
                        player.ManaRegenAccumulator -= points;
                    }
                }
            }
            else
            {
                player.ManaRegenAccumulator = 0f;
            }

            if (player.IsCasting && DateTime.UtcNow >= player.CastingUntil)
            {
                player.IsCasting = false;
            }
        }
    }

    // =============================================
    // EXTRACTION SYSTEM (sin cambios)
    // =============================================

    private void ProcessExtractionSystem(GameWorld world, float deltaTime)
    {
        foreach (var extraction in world.ExtractionPoints.Values)
        {
            if (!extraction.IsActive) continue;

            var playersToRemove = new List<string>();

            foreach (var (playerId, startTime) in extraction.ExtractionStartTimes)
            {
                var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;

                if (elapsedTime >= extraction.ExtractionTimeSeconds)
                {
                    CompletePlayerExtraction(world, playerId, extraction);
                    playersToRemove.Add(playerId);
                }
                else
                {
                    world.Players.TryGetValue(playerId, out var player);
                    if (player == null || !player.IsAlive ||
                        GameMathUtils.Distance(player.Position, extraction.Position) > 3.0f ||
                        (player.LastDamageTime > startTime))
                    {
                        CancelPlayerExtraction(world, playerId, extraction);
                        playersToRemove.Add(playerId);
                    }
                    else
                    {
                        // Emit progress event for client UI
                        var progress = (float)(elapsedTime / extraction.ExtractionTimeSeconds);
                        var remaining = (int)(extraction.ExtractionTimeSeconds - elapsedTime);
                        AddCombatEvent(world.WorldId, new CombatEvent
                        {
                            EventType = "extraction_progress",
                            SourceId = playerId,
                            TargetId = extraction.ExtractionId,
                            RoomId = player.CurrentRoomId,
                            Value = (int)(progress * 100),
                            Speed = remaining
                        });
                    }
                }
            }

            foreach (var playerId in playersToRemove)
            {
                extraction.ExtractionStartTimes.Remove(playerId);
                extraction.PlayersExtracting.Remove(playerId);
            }
        }
    }

    private void StartPlayerExtraction(GameWorld world, RealTimePlayer player, ExtractionPoint extraction)
    {
        if (!extraction.PlayersExtracting.Contains(player.PlayerId))
        {
            extraction.PlayersExtracting.Add(player.PlayerId);
            extraction.ExtractionStartTimes[player.PlayerId] = DateTime.UtcNow;

            _logger.LogInformation("Player {PlayerName} started extraction at {ExtractionId}",
                player.PlayerName, extraction.ExtractionId);

            AddCombatEvent(world.WorldId, new CombatEvent
            {
                EventType = "extraction_started",
                SourceId = player.PlayerId,
                TargetId = extraction.ExtractionId,
                RoomId = player.CurrentRoomId,
                Value = (int)extraction.ExtractionTimeSeconds
            });
        }
    }

    private void CompletePlayerExtraction(GameWorld world, string playerId, ExtractionPoint extraction)
    {
        if (!world.Players.TryGetValue(playerId, out var player)) return;

        var extractedValue = CalculateExtractionValue(player);

        // ── Calculate comprehensive XP (Item 2) ──
        var mobKillsXp = player.ExperiencePoints; // Already accumulated in HandleMobDeath
        var playerKillsXp = player.Kills * 200;
        var containersXp = player.ContainersLooted * 50;
        var extractionBonusXp = player.Inventory.Sum(item => item.Rarity * 100);
        var extractionValueXp = extractedValue / 10;
        var totalMatchXp = playerKillsXp + containersXp + extractionBonusXp + extractionValueXp;
        // Note: mobKillsXp was already added to ExperiencePoints during the match

        // ── Separate valuables from equipment (Item 3) ──
        var valuables = player.Inventory.Where(i => i.ItemType == "valuable").ToList();
        var nonValuables = player.Inventory.Where(i => i.ItemType != "valuable").ToList();
        var goldEarned = player.Inventory.Sum(i => i.Value); // All items contribute to gold

        // Stash non-valuable items for cross-match persistence
        var stashedCount = _stashService.StashItems(player.PlayerName, nonValuables);

        _logger.LogInformation("Player {PlayerName} extracted: {ItemCount} items, {Value} value, {Gold} gold, {Valuables} valuables sold ({Stashed} stashed)",
            player.PlayerName, player.Inventory.Count, extractedValue, goldEarned, valuables.Count, stashedCount);

        // Send match summary before removing player
        var summary = new MatchSummaryData
        {
            PlayerName = player.PlayerName,
            TeamId = player.TeamId,
            WinningTeam = world.WinningTeam,
            Extracted = true,
            Kills = player.Kills,
            Deaths = player.Deaths,
            DamageDealt = player.DamageDealt,
            HealingDone = player.HealingDone,
            ItemsExtracted = player.Inventory.Count,
            ExtractionValue = extractedValue,
            XpGained = totalMatchXp,
            FinalLevel = player.Level,
            GameDurationSeconds = (DateTime.UtcNow - world.CreatedAt).TotalSeconds,
            MobKills = player.MobKills,
            ContainersLooted = player.ContainersLooted,
            GoldEarned = goldEarned
        };
        OnPlayerExtracted?.Invoke(world.WorldId, player, summary);

        // Persist career stats, XP, gold, and match record to database
        _ = Task.Run(async () =>
        {
            try
            {
                await _playerRepository.UpdateCareerStats(player.PlayerName,
                    player.Kills, player.Deaths, player.DamageDealt, player.HealingDone,
                    extracted: true, xpGained: totalMatchXp, goldEarned: goldEarned);
                await _playerRepository.RecordMatch(player.PlayerName, new Data.Models.MatchRecord
                {
                    MatchStartTime = world.CreatedAt,
                    MatchEndTime = DateTime.UtcNow,
                    Extracted = true,
                    Kills = player.Kills,
                    Deaths = player.Deaths,
                    DamageDealt = player.DamageDealt,
                    HealingDone = player.HealingDone,
                    ItemsExtracted = player.Inventory.Count,
                    ExtractionValue = extractedValue,
                    XpGained = totalMatchXp,
                    GoldEarned = goldEarned
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist match data for {Player}", player.PlayerName);
            }
        });

        world.Players.TryRemove(playerId, out _);
    }

    private void CancelPlayerExtraction(GameWorld world, string playerId, ExtractionPoint extraction)
    {
        if (!world.Players.TryGetValue(playerId, out var player)) return;

        _logger.LogInformation("Player {PlayerName} extraction cancelled at {ExtractionId}",
            player.PlayerName, extraction.ExtractionId);

        AddCombatEvent(world.WorldId, new CombatEvent
        {
            EventType = "extraction_cancelled",
            SourceId = playerId,
            TargetId = extraction.ExtractionId,
            RoomId = player.CurrentRoomId
        });
    }

    private int CalculateExtractionValue(RealTimePlayer player)
    {
        return player.Inventory.Sum(item => item.Rarity * item.Rarity * 50);
    }

    // =============================================
    // ⭐ REMOVED: World generation methods moved to WorldManager
    // =============================================
    // Methods removed:
    // - GenerateWorldRooms() → WorldManager.GenerateWorldRooms()
    // - GenerateExtractionPoints() → WorldManager.GenerateExtractionPoints()
    // - SpawnInitialLoot() → WorldManager.SpawnInitialLoot()
    // - IsCornerRoom() → WorldManager.IsCornerRoom()
    // - GetTeamSpawnPosition() → WorldManager.GetTeamSpawnPosition()

    // =============================================
    // WORLD UPDATE MESSAGES (⭐ OPTIMIZED WITH OBJECT POOLING)
    // =============================================

    private WorldUpdateMessage CreateWorldUpdate(GameWorld world)
    {
        var pools = NetworkObjectPools.Instance;

        // ⭐ PERF: Rent world update from pool
        var worldUpdate = pools.WorldUpdates.Rent();

        // ⭐ SYNC: Collect acknowledged input sequences for client reconciliation
        // ⭐ REFACTORED: Use InputProcessor
        var acknowledgedInputs = new Dictionary<string, uint>();
        foreach (var player in world.Players.Values)
        {
            acknowledgedInputs[player.PlayerId] = _inputProcessor.GetLastAcknowledgedSequence(player.PlayerId);
        }

        worldUpdate.AcknowledgedInputs = acknowledgedInputs;
        worldUpdate.ServerTime = (float)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        worldUpdate.FrameNumber = _frameNumber;

        // ⭐ PERF: Rent lists from pools (pre-sized for efficiency)
        var playerList = pools.PlayerStateLists.Rent();
        var mobList = pools.MobUpdateLists.Rent();

        // ⭐ DELTA COMPRESSION: Only send players with significant changes (70-90% bandwidth reduction)
        foreach (var p in world.Players.Values)
        {
            // Skip players without significant changes
            if (!p.HasSignificantChange())
                continue;

            var playerUpdate = pools.PlayerStateUpdates.Rent();
            playerUpdate.PlayerId = p.PlayerId;
            playerUpdate.PlayerName = p.PlayerName;
            playerUpdate.PlayerClass = p.PlayerClass;
            playerUpdate.Position = p.Position;
            playerUpdate.Velocity = p.Velocity;
            playerUpdate.Direction = p.Direction;
            playerUpdate.Health = p.Health;
            playerUpdate.MaxHealth = p.MaxHealth;
            playerUpdate.IsAlive = p.IsAlive;
            playerUpdate.IsMoving = p.IsMoving;
            playerUpdate.IsCasting = p.IsCasting;

            playerList.Add(playerUpdate);

            // ⭐ DELTA: Mark as sent to avoid resending unchanged state
            p.MarkAsSent();
        }

        // Create mob updates using pooled objects (only dirty mobs)
        foreach (var m in world.Mobs.Values)
        {
            if (!m.IsDirty) continue;

            var mobUpdate = pools.MobUpdates.Rent();
            mobUpdate.MobId = m.MobId;
            mobUpdate.Position = m.Position;
            mobUpdate.State = m.State;
            mobUpdate.Health = m.Health;
            mobUpdate.RoomId = m.RoomId;
            mobUpdate.MobType = m.MobType;
            mobUpdate.MaxHealth = m.MaxHealth;
            mobUpdate.Phase = m.BossPhase;

            mobList.Add(mobUpdate);
            m.IsDirty = false; // Clear dirty flag
        }

        worldUpdate.Players = playerList;
        worldUpdate.MobUpdates = mobList;
        worldUpdate.CombatEvents = GetAndClearRecentCombatEvents(world.WorldId);
        worldUpdate.LootUpdates = GetAndClearRecentLootUpdates(world.WorldId);
        worldUpdate.ContainerUpdates = GetAndClearRecentContainerUpdates(world.WorldId);

        // ⚠️ IMPORTANT: Objects will be returned to pool after serialization
        // See ReturnWorldUpdateToPool() method

        return worldUpdate;
    }

    /// <summary>
    /// Return world update and all its contents back to pools after serialization
    /// </summary>
    public void ReturnWorldUpdateToPool(WorldUpdateMessage worldUpdate)
    {
        var pools = NetworkObjectPools.Instance;

        // Return player updates to pool
        if (worldUpdate.Players != null)
        {
            pools.PlayerStateUpdates.ReturnRange(worldUpdate.Players);
            pools.PlayerStateLists.Return(worldUpdate.Players);
        }

        // Return mob updates to pool
        if (worldUpdate.MobUpdates != null)
        {
            pools.MobUpdates.ReturnRange(worldUpdate.MobUpdates);
            pools.MobUpdateLists.Return(worldUpdate.MobUpdates);
        }

        // Return combat events to pool
        if (worldUpdate.CombatEvents != null)
        {
            pools.CombatEvents.ReturnRange(worldUpdate.CombatEvents);
            pools.CombatEventLists.Return(worldUpdate.CombatEvents);
        }

        // Return loot updates to pool
        if (worldUpdate.LootUpdates != null)
        {
            pools.LootUpdates.ReturnRange(worldUpdate.LootUpdates);
            pools.LootUpdateLists.Return(worldUpdate.LootUpdates);
        }

        // Return world update itself to pool
        pools.WorldUpdates.Return(worldUpdate);
    }

    private WorldStateMessage CreateWorldStateMessage(GameWorld world)
    {
        return new WorldStateMessage
        {
            Rooms = world.Rooms.Values.Select(r => new RoomStateUpdate
            {
                RoomId = r.RoomId,
                IsCompleted = r.IsCompleted,
                CompletedByTeam = r.CompletedByTeam,
                MobCount = world.Mobs.Values.Count(m => m.RoomId == r.RoomId && m.Health > 0),
                LootCount = world.AvailableLoot.Values.Count(l => l.RoomId == r.RoomId)
            }).ToList(),

            ExtractionPoints = world.ExtractionPoints.Values.Select(e => new ExtractionPointUpdate
            {
                ExtractionId = e.ExtractionId,
                IsActive = e.IsActive,
                Position = e.Position,
                PlayersExtracting = e.ExtractionStartTimes.Select(kvp =>
                {
                    var player = world.Players.Values.FirstOrDefault(p => p.PlayerId == kvp.Key);
                    var elapsed = (DateTime.UtcNow - kvp.Value).TotalSeconds;
                    var progress = Math.Min(1.0f, (float)(elapsed / e.ExtractionTimeSeconds));

                    return new ExtractionProgress
                    {
                        PlayerId = kvp.Key,
                        PlayerName = player?.PlayerName ?? "Unknown",
                        Progress = progress,
                        SecondsRemaining = Math.Max(0, e.ExtractionTimeSeconds - (int)elapsed)
                    };
                }).ToList()
            }).ToList(),

            WorldInfo = new WorldInfo
            {
                WorldId = world.WorldId,
                IsCompleted = world.IsCompleted,
                WinningTeam = world.WinningTeam,
                TotalRooms = world.Rooms.Count,
                CompletedRooms = world.Rooms.Values.Count(r => r.IsCompleted),
                TotalLoot = world.AvailableLoot.Count,
                WorldAge = DateTime.UtcNow - world.CreatedAt
            }
        };
    }

    // =============================================
    // EVENT MANAGEMENT (sin cambios)
    // =============================================

    private const int MaxEventsPerBag = 500;

    private void AddCombatEvent(string worldId, CombatEvent combatEvent)
    {
        var bag = _recentCombatEvents.GetOrAdd(worldId, _ => new ConcurrentBag<CombatEvent>());
        if (bag.Count < MaxEventsPerBag)
            bag.Add(combatEvent);
    }

    private void AddLootUpdate(string worldId, LootUpdate lootUpdate)
    {
        var bag = _recentLootUpdates.GetOrAdd(worldId, _ => new ConcurrentBag<LootUpdate>());
        if (bag.Count < MaxEventsPerBag)
            bag.Add(lootUpdate);
    }

    private List<CombatEvent> GetAndClearRecentCombatEvents(string worldId)
    {
        if (_recentCombatEvents.TryRemove(worldId, out var bag))
        {
            return bag.ToList();
        }
        return new List<CombatEvent>();
    }

    private List<LootUpdate> GetAndClearRecentLootUpdates(string worldId)
    {
        if (_recentLootUpdates.TryRemove(worldId, out var bag))
        {
            return bag.ToList();
        }
        return new List<LootUpdate>();
    }

    // =============================================
    // ⭐ REFACTORED: MATCHMAKING SYSTEM (now using LobbyManager)
    // =============================================

    public string FindOrCreateWorld(string teamId, string gameMode = "trios")
    {
        // ⭐ REFACTORED: Delegate to LobbyManager with game mode
        var lobbyId = _lobbyManager.FindOrCreateLobby(teamId, gameMode);

        // Create lobby arena world if it doesn't exist yet
        if (_worldManager.GetWorld(lobbyId) == null && _lobbyManager.IsLobby(lobbyId))
        {
            _worldManager.CreateLobbyWorld(lobbyId);
        }

        return lobbyId;
    }

    // =============================================
    // ⭐ REFACTORED: PLAYER MANAGEMENT (now using LobbyManager)
    // =============================================

    public bool AddPlayerToWorld(string worldId, RealTimePlayer player)
    {
        // Ensure player account exists in DB and load stash
        _ = Task.Run(async () =>
        {
            try
            {
                await _playerRepository.GetOrCreatePlayer(player.PlayerName);
                await _stashService.LoadFromDatabase(player.PlayerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load player data for {Player}", player.PlayerName);
            }
        });

        var existingWorld = _worldManager.GetWorld(worldId);

        // Handle lobby arena world: add to both WorldLobby and GameWorld
        if (existingWorld != null && existingWorld.IsLobbyWorld)
        {
            if (!_lobbyManager.AddPlayerToLobby(worldId, player))
                return false;

            player.Position = _worldManager.GetLobbySpawnPosition(player.TeamId);
            player.CurrentRoomId = _movementSystem.GetRoomIdByPosition(existingWorld, player.Position);
            existingWorld.Players[player.PlayerId] = player;
            return true;
        }

        // Regular game world
        if (existingWorld != null)
        {
            return AddPlayerToExistingWorld(existingWorld, player);
        }

        // Fallback: lobby without arena world
        if (_lobbyManager.IsLobby(worldId))
        {
            return _lobbyManager.AddPlayerToLobby(worldId, player);
        }

        _logger.LogWarning("Attempted to add player to non-existent world/lobby {WorldId}", worldId);
        return false;
    }

    /// <summary>
    /// ⭐ REFACTORED: Add player to existing world.
    /// </summary>
    private bool AddPlayerToExistingWorld(GameWorld world, RealTimePlayer player)
    {
        if (world.Players.Count >= _settings.MaxPlayersPerWorld)
        {
            return false;
        }

        player.Position = _worldManager.GetTeamSpawnPosition(player.TeamId);
        player.CurrentRoomId = _movementSystem.GetRoomIdByPosition(world, player.Position);
        world.Players[player.PlayerId] = player;

        return true;
    }

    // =============================================
    // ⭐ REFACTORED: GAME START FROM LOBBY (event handler)
    // =============================================

    /// <summary>
    /// ⭐ REFACTORED: Event handler called by LobbyManager when lobby is ready to start.
    /// Creates the game world from lobby data using WorldManager.
    /// </summary>
    private void HandleLobbyReadyToStart(WorldLobby lobby)
    {
        try
        {
            // Resolve game mode config
            var gameMode = lobby.GameMode;
            _settings.GameModes.TryGetValue(gameMode, out var modeConfig);

            _logger.LogInformation("Creating game world from lobby {LobbyId} with {PlayerCount} players (mode: {GameMode})",
                lobby.LobbyId, lobby.TotalPlayers, gameMode);

            // Remove lobby arena world (uses same worldId as lobby)
            var lobbyWorld = _worldManager.GetWorld(lobby.LobbyId);
            if (lobbyWorld != null && lobbyWorld.IsLobbyWorld)
            {
                _movementSystem.CleanupWorldData(lobby.LobbyId);
                _worldManager.RemoveWorld(lobby.LobbyId);
                _logger.LogInformation("Removed lobby arena world {LobbyId}", lobby.LobbyId);
            }

            // Prepare players with spawn positions
            var playersDict = new Dictionary<string, RealTimePlayer>();
            foreach (var player in lobby.Players.Values)
            {
                player.Position = _worldManager.GetTeamSpawnPosition(player.TeamId, modeConfig);
                playersDict[player.PlayerId] = player;
            }

            // ⭐ REFACTORED: Use WorldManager to create world with mode config
            var world = _worldManager.CreateWorld(lobby.LobbyId, playersDict, gameMode, modeConfig);

            // Set player room IDs and reset movement/level state after world creation
            foreach (var player in world.Players.Values)
            {
                player.CurrentRoomId = _movementSystem.GetRoomIdByPosition(world, player.Position);

                // Clear leftover movement state from lobby
                player.Velocity = Vector2.Zero;
                player.MoveTarget = null;
                player.IsPathing = false;
                player.IsMoving = false;
                player.IsSprinting = false;

                // Reset per-run levels (undo MaxHealth/MaxMana from level-ups)
                player.MaxHealth -= player.LevelBonusHealth;
                player.MaxMana -= player.LevelBonusMana;
                player.Level = 1;
                player.ExperiencePoints = 0;
                player.LevelBonusDamagePercent = 0f;
                player.LevelBonusHealingPercent = 0f;
                player.LevelBonusHealth = 0;
                player.LevelBonusMana = 0;

                // Start at full health
                player.Health = player.MaxHealth;
                player.Mana = player.MaxMana;

                // Clear anti-cheat tracker (position jumped from lobby to spawn)
                _movementSystem.CleanupPlayerTracker(player.PlayerId);
            }

            // Track initial team count for win conditions
            world.InitialTeamCount = world.Players.Values.Select(p => p.TeamId).Distinct().Count();
            world.GameStartedAt = DateTime.UtcNow;

            // ⭐ Spawn initial mobs
            var spawnedMobs = _mobAISystem.SpawnInitialMobs(world);

            // Queue initial container updates so clients receive them on first frame
            foreach (var container in world.LootContainers.Values)
            {
                HandleContainerSpawned(world.WorldId, container);
            }

            _logger.LogInformation("✅ Game started! World {WorldId} with {PlayerCount} players from {TeamCount} teams, {MobCount} mobs, {ContainerCount} containers",
                world.WorldId, world.Players.Count, lobby.TeamPlayerCounts.Count, spawnedMobs.Count, world.LootContainers.Count);

            // ⭐ REFACTORED: Notify LobbyManager that transition is complete
            _lobbyManager.CompleteLobbyStart(lobby.LobbyId);

            // Emit locked door events so clients know which corridors are blocked
            foreach (var door in world.LockedDoors.Values)
            {
                AddCombatEvent(world.WorldId, new CombatEvent
                {
                    EventType = "locked_door",
                    SourceId = door.ConnectionId,
                    TargetId = door.RequiredKeyType,
                    RoomId = door.RoomIdA,
                    AbilityId = door.ConnectionId
                });
            }

            // Trigger event for NetworkService to notify clients
            OnGameStarted?.Invoke(world.WorldId, world.Players.Keys.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating world from lobby {LobbyId}", lobby.LobbyId);
            _lobbyManager.MarkLobbyError(lobby.LobbyId, ex.Message);
        }
    }

    // =============================================
    // ⭐ REFACTORED: LOBBY STATS (delegated to LobbyManager)
    // =============================================

    /// <summary>
    /// ⭐ REFACTORED: Get combined lobby and world stats.
    /// </summary>
    public Dictionary<string, object> GetLobbyStats()
    {
        // ⭐ REFACTORED: Get lobby stats from LobbyManager
        var lobbyStats = _lobbyManager.GetLobbyStats();

        // ⭐ REFACTORED: Add world stats from WorldManager
        var worldStats = _worldManager.GetWorldStats();
        foreach (var (key, value) in worldStats)
        {
            lobbyStats[key] = value;
        }

        return lobbyStats;
    }

    /// <summary>
    /// Get object pool statistics for monitoring performance
    /// </summary>
    public Dictionary<string, PoolStats> GetPoolStats()
    {
        return NetworkObjectPools.Instance.GetAllStats();
    }

    /// <summary>
    /// Get total allocations saved by object pooling
    /// </summary>
    public long GetTotalAllocationsSaved()
    {
        return NetworkObjectPools.Instance.GetTotalAllocationsSaved();
    }

    // =============================================
    // =============================================
    // ⭐ REFACTORED: HELPER METHODS (delegated to WorldManager)
    // =============================================

    /// <summary>
    /// ⭐ REFACTORED: Find player across all worlds AND lobbies.
    /// </summary>
    private RealTimePlayer? FindPlayer(string playerId)
    {
        // First check active game worlds
        var player = _worldManager.FindPlayer(playerId);
        if (player != null)
            return player;

        // Then check lobbies (players waiting to start)
        return _lobbyManager.FindPlayer(playerId);
    }

    /// <summary>
    /// ⭐ REFACTORED: Find world containing player (delegated to WorldManager).
    /// </summary>
    private GameWorld? FindWorldByPlayer(string playerId)
    {
        return _worldManager.FindWorldByPlayer(playerId);
    }

    /// <summary>
    /// ⭐ REFACTORED: Find world by ID (delegated to WorldManager).
    /// </summary>
    private GameWorld? FindWorldById(string worldId)
    {
        return _worldManager.GetWorld(worldId);
    }

    /// <summary>
    /// ⭐ REFACTORED: Find world ID containing player (delegated to WorldManager).
    /// </summary>
    private string FindWorldIdByPlayer(string playerId)
    {
        return _worldManager.FindWorldIdByPlayer(playerId) ?? string.Empty;
    }

    private void LogPerformanceStats()
    {
        // ⭐ REFACTORED: Use managers to get stats
        var worlds = _worldManager.GetAllWorlds();
        var totalPlayers = worlds.Sum(w => w.Players.Count);
        var totalMobs = worlds.Sum(w => w.Mobs.Count);
        var totalLoot = worlds.Sum(w => w.AvailableLoot.Count);

        var movementStats = _movementSystem.GetDetailedMovementStats();
        var lootStats = _lootSystem.GetDetailedLootAnalytics();
        var aiStats = _mobAISystem.GetDetailedAIAnalytics(); // ⭐ NUEVO
        var suspiciousPlayers = _movementSystem.GetSuspiciousPlayers().Count;

        _logger.LogInformation("Performance Stats - Frame: {Frame}, Worlds: {Worlds}, Players: {Players}, Mobs: {Mobs}, Loot: {Loot}, Queue: {Queue}, Movement: Collisions={Collisions}, Suspicious={Suspicious}, LootPickupRate={PickupRate:F2}, AIEfficiency={AIEfficiency:F2}",
            _frameNumber, worlds.Count, totalPlayers, totalMobs, totalLoot, _inputProcessor.GetQueueSize(),
            movementStats.GetValueOrDefault("TotalCollisions", 0), suspiciousPlayers,
            lootStats.GetValueOrDefault("PickupRate", 0.0), aiStats.GetValueOrDefault("ProcessingEfficiency", 0.0));
    }

    /// <summary>
    /// ⭐ REFACTORED: Check if a world ID is a lobby (delegated to LobbyManager).
    /// </summary>
    public bool IsWorldLobby(string worldId)
    {
        return _lobbyManager.IsLobby(worldId);
    }

    /// <summary>
    /// ⭐ REFACTORED: Get lobby info (delegated to LobbyManager).
    /// </summary>
    public WorldLobby? GetLobbyInfo(string worldId)
    {
        return _lobbyManager.GetLobby(worldId);
    }

    // =============================================
    // ⭐ NUEVOS MÉTODOS PARA DIAGNOSTICS DEL AI SYSTEM
    // =============================================

    public Dictionary<string, object> GetAIDiagnostics(string? worldId = null)
    {
        var diagnostics = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(worldId))
        {
            var aiStats = _mobAISystem.GetAIStats(worldId);
            var mobDistribution = _mobAISystem.GetMobDistributionStats(worldId);

            diagnostics["WorldAIStats"] = aiStats;
            diagnostics["MobDistributionByType"] = mobDistribution;
        }

        diagnostics["AllWorldsAIStats"] = _mobAISystem.GetDetailedAIAnalytics();
        diagnostics["CurrentAISettings"] = _mobAISystem.GetCurrentAISettings();

        return diagnostics;
    }

    public void UpdateAISettings(AISettings newSettings)
    {
        _mobAISystem.UpdateAISettings(newSettings);
        _logger.LogInformation("Admin updated AI settings");
    }

    public AISettings GetCurrentAISettings()
    {
        return _mobAISystem.GetCurrentAISettings();
    }

    public void UpdateMobTemplate(string mobType, MobTemplate mobTemplate)
    {
        _mobAISystem.UpdateMobTemplate(mobType, mobTemplate);
        _logger.LogInformation("Admin updated mob template {MobType}", mobType);
    }

    public void ScaleMobDifficulty(string worldId)
    {
        var world = FindWorldById(worldId);
        if (world != null)
        {
            _mobAISystem.ScaleMobDifficulty(world);
            _logger.LogInformation("Admin scaled mob difficulty for world {WorldId}", worldId);
        }
    }

    // =============================================
    // MÉTODOS PARA DIAGNOSTICS DEL LOOT SYSTEM (ya existentes)
    // =============================================

    public Dictionary<string, object> GetLootDiagnostics(string? worldId = null)
    {
        var diagnostics = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(worldId))
        {
            var lootStats = _lootSystem.GetLootStats(worldId);
            var lootDistribution = _lootSystem.GetLootDistributionByRarity(worldId);

            diagnostics["WorldLootStats"] = lootStats;
            diagnostics["LootDistributionByRarity"] = lootDistribution;
        }

        diagnostics["AllWorldsLootStats"] = _lootSystem.GetDetailedLootAnalytics();
        diagnostics["CurrentLootSettings"] = _lootSystem.GetCurrentLootSettings();

        return diagnostics;
    }

    public void UpdateLootSettings(LootSettings newSettings)
    {
        _lootSystem.UpdateLootSettings(newSettings);
        _logger.LogInformation("Admin updated loot settings");
    }

    public LootSettings GetCurrentLootSettings()
    {
        return _lootSystem.GetCurrentLootSettings();
    }

    /// <summary>
    /// ⭐ REFACTORED: Update loot table (delegated to LootSystem).
    /// </summary>
    public void UpdateLootTable(string tableId, LootTable lootTable)
    {
        _lootSystem.UpdateLootTable(tableId, lootTable);
        _logger.LogInformation("Admin updated loot table {TableId}", tableId);
    }

    // =============================================
    // MÉTODOS PARA DIAGNOSTICS DEL MOVEMENT SYSTEM (ya existentes)
    // =============================================

    public Dictionary<string, object> GetMovementDiagnostics(string? worldId = null)
    {
        var diagnostics = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(worldId))
        {
            var movementStats = _movementSystem.GetMovementStats(worldId);
            var spatialInfo = _movementSystem.GetSpatialGridInfo(worldId);

            diagnostics["WorldMovementStats"] = movementStats;
            diagnostics["SpatialGridInfo"] = spatialInfo;
        }

        diagnostics["AllWorldsMovementStats"] = _movementSystem.GetDetailedMovementStats();
        diagnostics["SuspiciousPlayers"] = _movementSystem.GetSuspiciousPlayers().Select(p => new
        {
            p.PlayerId,
            p.SuspiciousMovements,
            p.IsBeingMonitored,
            p.MaxRecordedSpeed,
            AverageSpeed = p.CalculateAverageSpeed(),
            RecentPositionsCount = p.RecentPositions.Count
        }).ToList();

        return diagnostics;
    }

    public void ResetPlayerMovementSuspicion(string playerId)
    {
        _movementSystem.ResetPlayerSuspicion(playerId);
        _logger.LogInformation("Admin reset movement suspicion for player {PlayerId}", playerId);
    }

    public void FlagPlayerForMovementMonitoring(string playerId, string reason)
    {
        _movementSystem.FlagPlayerForMonitoring(playerId, reason);
        _logger.LogInformation("Admin flagged player {PlayerId} for movement monitoring: {Reason}", playerId, reason);
    }

    public void UpdateMovementSettings(MovementSettings newSettings)
    {
        _movementSystem.UpdateMovementSettings(newSettings);
        _logger.LogInformation("Admin updated movement settings");
    }

    public MovementSettings GetCurrentMovementSettings()
    {
        return _movementSystem.GetCurrentMovementSettings();
    }

    /// <summary>
    /// Get acknowledged input sequences for a list of players.
    /// Used by NetworkService to send acknowledgments back to clients for input reconciliation.
    /// </summary>
    public Dictionary<string, uint> GetAcknowledgedInputs(IEnumerable<RealTimePlayer> players)
    {
        var acknowledgedInputs = new Dictionary<string, uint>();
        foreach (var player in players)
        {
            var ackSeq = _inputProcessor.GetLastAcknowledgedSequence(player.PlayerId);
            if (ackSeq > 0)
            {
                acknowledgedInputs[player.PlayerId] = ackSeq;
            }
        }

        return acknowledgedInputs;
    }

    public int GetEffectiveAttackSpeedMs(RealTimePlayer player)
    {
        return _combatSystem.GetEffectiveAttackSpeedMs(player);
    }

    public float GetEffectiveAttackRange(RealTimePlayer player)
    {
        return _combatSystem.GetEffectiveAttackRange(player);
    }

    // =============================================
    // NETWORKING HELPERS (RTT, AOI, STATS)
    // =============================================

    public InputStats? GetInputStats(string playerId)
    {
        return _inputProcessor.GetInputStats(playerId);
    }

    public GameWorld? GetWorld(string worldId)
    {
        return _worldManager.GetWorld(worldId);
    }

    public IEnumerable<GameWorld> GetAllWorlds()
    {
        return _worldManager.GetAllWorlds();
    }

    public List<LootItem> GetPlayerStash(string playerName)
    {
        return _stashService.GetStash(playerName);
    }

    public HashSet<string> GetSuspiciousPlayerIds()
    {
        return _movementSystem.GetSuspiciousPlayers()
            .Where(t => t.SuspiciousMovements > 5)
            .Select(t => t.PlayerId)
            .ToHashSet();
    }

    public void SetCombatRttLookup(Func<string, float> rttLookup)
    {
        _combatSystem.RttLookup = rttLookup;
    }

    // =============================================
    // ⭐ NUEVO: MÉTODO INTEGRADO DE ESTADÍSTICAS CON AI
    // =============================================

    public Dictionary<string, object> GetComprehensiveStats()
    {
        var serverStats = GetServerStats();
        var movementStats = _movementSystem.GetDetailedMovementStats();
        var lootStats = _lootSystem.GetDetailedLootAnalytics();
        var aiStats = _mobAISystem.GetDetailedAIAnalytics(); // ⭐ NUEVO
        var lobbyStats = GetLobbyStats();

        return new Dictionary<string, object>
        {
            ["ServerStats"] = serverStats,
            ["MovementStats"] = movementStats,
            ["LootStats"] = lootStats,
            ["AIStats"] = aiStats, // ⭐ NUEVO
            ["LobbyStats"] = lobbyStats,
            ["SystemsIntegrated"] = new[] { "Combat", "Movement", "Loot", "MobAI" }, // ⭐ ACTUALIZADO
            ["LastUpdate"] = DateTime.UtcNow
        };
    }

    // =============================================
    // ⭐ ACTUALIZADO: DISPOSAL CON MOB AI SYSTEM
    // =============================================

    public void Dispose()
    {
        _gameLoopTimer?.Dispose();

        // ⭐ REFACTORED: Dispose LobbyManager (handles its own timers)
        _lobbyManager?.Dispose();

        // ⭐ REFACTORED: Desuscribirse de eventos del LobbyManager
        if (_lobbyManager != null)
        {
            _lobbyManager.OnLobbyReadyToStart -= HandleLobbyReadyToStart;
        }

        // ⭐ NUEVO: Desuscribirse de eventos del MobAISystem
        if (_mobAISystem != null)
        {
            _mobAISystem.OnMobSpawned -= HandleMobSpawned;
            _mobAISystem.OnMobDeath -= HandleMobDeath;
            _mobAISystem.OnMobStateChanged -= HandleMobStateChanged;
            _mobAISystem.OnMobAttack -= HandleMobAttack;
            _mobAISystem.OnBossSpawned -= HandleBossSpawned;
            _mobAISystem.OnPlayerKilledByMob -= HandlePlayerKilledByMob;
            _mobAISystem.OnMobAbilityUsed -= HandleCombatEvent;
        }

        // Desuscribirse de eventos del LootSystem
        if (_lootSystem != null)
        {
            _lootSystem.OnLootSpawned -= HandleLootSpawned;
            _lootSystem.OnLootTaken -= HandleLootTaken;
            _lootSystem.OnLootRemoved -= HandleLootRemoved;
            _lootSystem.OnItemUsed -= HandleItemUsed;
        }

        // Desuscribirse de eventos del MovementSystem
        if (_movementSystem != null)
        {
            _movementSystem.OnRoomChanged -= HandleRoomChanged;
            _movementSystem.OnPlayersInRoom -= HandlePlayersInRoom;
        }

        // Desuscribirse de eventos del CombatSystem
        if (_combatSystem != null)
        {
            _combatSystem.OnCombatEvent -= HandleCombatEvent;
            _combatSystem.OnPlayerDeath -= HandlePlayerDeath;
            _combatSystem.ProjectileSystem.OnCombatEvent -= HandleCombatEvent;
            _combatSystem.ProjectileSystem.OnPlayerDeath -= HandlePlayerDeath;
        }

        // ⭐ REFACTORED: Use WorldManager and LobbyManager for cleanup
        var allWorlds = _worldManager.GetAllWorlds();
        foreach (var world in allWorlds)
        {
            _mobAISystem?.CleanupWorldAI(world.WorldId); // ⭐ NUEVO
            _lootSystem?.CleanupWorldLoot(world.WorldId);
            _movementSystem?.CleanupWorldData(world.WorldId);
        }

        // ⭐ REFACTORED: Dispose managers
        _worldManager?.Dispose();
        _inputProcessor?.Dispose();

        _recentCombatEvents.Clear();
        _recentLootUpdates.Clear();

        _logger.LogInformation("RealTimeGameEngine disposed with Combat, Movement, Loot, and AI systems");
    }
}