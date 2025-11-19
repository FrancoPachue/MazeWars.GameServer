using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Engine.Movement.Interface;
using MazeWars.GameServer.Engine.Movement.Settings;
using MazeWars.GameServer.Engine.AI.Interface;
using MazeWars.GameServer.Engine.Loot.Interface;
using MazeWars.GameServer.Engine.Network;
using MazeWars.GameServer.Engine.Memory;
using MazeWars.GameServer.Engine.Managers;
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
    private readonly IMobAISystem _mobAISystem;
    private readonly LobbyManager _lobbyManager;
    private readonly WorldManager _worldManager;
    private readonly InputProcessor _inputProcessor;

    private readonly Timer _gameLoopTimer;

    private readonly ConcurrentDictionary<string, List<CombatEvent>> _recentCombatEvents = new();
    private readonly ConcurrentDictionary<string, List<LootUpdate>> _recentLootUpdates = new();

    private readonly Queue<PlayerStateUpdate> _playerUpdatePool = new();
    private readonly Queue<CombatEvent> _combatEventPool = new();

    private int _frameNumber = 0;
    private DateTime _lastFrameTime = DateTime.UtcNow;

    public event Action<string, List<string>>? OnGameStarted;

    public RealTimeGameEngine(
        ILogger<RealTimeGameEngine> logger,
        IOptions<GameServerSettings> settings,
        ICombatSystem combatSystem,
        IMovementSystem movementSystem,
        ILootSystem lootSystem,
        IMobAISystem mobAISystem,
        LobbyManager lobbyManager,
        WorldManager worldManager,
        InputProcessor inputProcessor) // ⭐ REFACTORED: Inyección de nuevos managers
    {
        _logger = logger;
        _settings = settings.Value;
        _combatSystem = combatSystem;
        _movementSystem = movementSystem;
        _lootSystem = lootSystem;
        _mobAISystem = mobAISystem;
        _lobbyManager = lobbyManager;
        _worldManager = worldManager;
        _inputProcessor = inputProcessor;

        // ⭐ REFACTORED: Configure InputProcessor with player lookup
        _inputProcessor.PlayerLookup = FindPlayer;

        // ⭐ REFACTORED: Subscribe to LobbyManager events
        _lobbyManager.OnLobbyReadyToStart += HandleLobbyReadyToStart;

        // ⭐ REFACTORED: Subscribe to InputProcessor events
        _inputProcessor.OnPlayerInput += ProcessPlayerInput;
        _inputProcessor.OnLootGrab += ProcessLootGrab;
        _inputProcessor.OnChat += ProcessChat;
        _inputProcessor.OnUseItem += ProcessUseItem;
        _inputProcessor.OnExtraction += ProcessExtraction;
        _inputProcessor.OnTradeRequest += ProcessTradeRequest;

        // ⭐ NUEVO: Subscribe to MobAISystem events
        _mobAISystem.OnMobSpawned += HandleMobSpawned;
        _mobAISystem.OnMobDeath += HandleMobDeath;
        _mobAISystem.OnMobStateChanged += HandleMobStateChanged;
        _mobAISystem.OnMobAttack += HandleMobAttack;
        _mobAISystem.OnBossSpawned += HandleBossSpawned;

        // Subscribe to LootSystem events
        _lootSystem.OnLootSpawned += HandleLootSpawned;
        _lootSystem.OnLootTaken += HandleLootTaken;
        _lootSystem.OnLootRemoved += HandleLootRemoved;
        _lootSystem.OnItemUsed += HandleItemUsed;

        // Subscribe to CombatSystem events
        _combatSystem.OnCombatEvent += HandleCombatEvent;
        _combatSystem.OnPlayerDeath += HandlePlayerDeath;

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

        AddCombatEvent(worldId, new CombatEvent
        {
            EventType = "mob_death",
            SourceId = killer?.PlayerId ?? "",
            TargetId = mob.MobId,
            Value = 0,
            Position = mob.Position
        });

        _logger.LogInformation("Mob died in world {WorldId}: {MobType} {MobId}, killed by {KillerName}",
            worldId, mob.MobType, mob.MobId, killer?.PlayerName ?? "unknown");
    }

    private void HandleMobStateChanged(Mob mob, MobState oldState, MobState newState)
    {
        _logger.LogDebug("Mob {MobId} changed state from {OldState} to {NewState}",
            mob.MobId, oldState, newState);

        // Marcar mob como dirty para envío de updates
        mob.IsDirty = true;
    }

    private void HandleMobAttack(Mob mob, RealTimePlayer target, int damage)
    {
        var worldId = FindWorldIdByPlayer(target.PlayerId);
        if (!string.IsNullOrEmpty(worldId))
        {
            AddCombatEvent(worldId, new CombatEvent
            {
                EventType = "mob_attack",
                SourceId = mob.MobId,
                TargetId = target.PlayerId,
                Value = damage,
                Position = target.Position
            });
        }

        _logger.LogDebug("Mob {MobId} attacked {PlayerName} for {Damage} damage",
            mob.MobId, target.PlayerName, damage);
    }

    private void HandleBossSpawned(string worldId, Mob boss)
    {
        AddCombatEvent(worldId, new CombatEvent
        {
            EventType = "boss_spawned",
            SourceId = boss.MobId,
            TargetId = "",
            Value = 0,
            Position = boss.Position
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

        _logger.LogDebug("Combat event in world {WorldId}: {EventType} from {SourceId} to {TargetId} - Value: {Value}",
            worldId, combatEvent.EventType, combatEvent.SourceId, combatEvent.TargetId, combatEvent.Value);
    }

    private void HandlePlayerDeath(RealTimePlayer deadPlayer, RealTimePlayer? killer)
    {
        var world = FindWorldByPlayer(deadPlayer.PlayerId);
        if (world == null) return;

        // Usar LootSystem para drop de items del jugador
        _lootSystem.DropPlayerLoot(deadPlayer, world);

        // Add death combat event
        AddCombatEvent(world.WorldId, new CombatEvent
        {
            EventType = "player_death",
            SourceId = killer?.PlayerId ?? "",
            TargetId = deadPlayer.PlayerId,
            Value = 0,
            Position = deadPlayer.Position
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
        // ⭐ REFACTORED: Delegate to WorldManager and LobbyManager

        // Try to remove from active world first
        var world = _worldManager.GetWorld(worldId);
        if (world != null)
        {
            if (_worldManager.RemovePlayerFromWorld(worldId, playerId))
            {
                // Limpiar datos de sistemas especializados
                _movementSystem.CleanupPlayerTracker(playerId);

                // ⭐ REFACTORED: Use InputProcessor
                _inputProcessor.ClearPlayerInputBuffer(playerId);

                // Check if world is now empty and cleanup
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

        // Try to remove from lobby
        if (_lobbyManager.IsLobby(worldId))
        {
            if (_lobbyManager.RemovePlayerFromLobby(worldId, playerId))
            {
                // Limpiar datos del MovementSystem también para lobbies
                _movementSystem.CleanupPlayerTracker(playerId);

                // ⭐ REFACTORED: Use InputProcessor
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
            _frameNumber++;

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
        // Usar sistemas especializados para actualizar el mundo
        _movementSystem.UpdateAllPlayersMovement(world, deltaTime);

        // ⭐ REFACTORIZADO: Usar MobAISystem para actualizar mobs
        _mobAISystem.UpdateMobs(world, deltaTime);

        var allPlayers = world.Players.Values.ToList();
        _combatSystem.UpdateStatusEffects(allPlayers, deltaTime);

        _movementSystem.ProcessCollisions(world);
        UpdateCombatStates(world, deltaTime);
        CheckRoomTransitions(world);

        // Usar LootSystem para procesamiento de loot
        _lootSystem.ProcessLootSpawning(world, deltaTime);

        ProcessExtractionSystem(world, deltaTime);
        CheckRoomCompletion(world);
        CheckWinConditions(world);
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
        if (world == null) return;

        // Usar MovementSystem para procesar movimiento
        var movementResult = _movementSystem.UpdatePlayerMovement(player, input, world, 1.0f / _settings.TargetFPS);

        if (movementResult.Success)
        {
            if (movementResult.PositionChanged)
            {
                player.Position = movementResult.NewPosition;
                player.Velocity = movementResult.NewVelocity;
            }
        }
        else if (!string.IsNullOrEmpty(movementResult.ErrorMessage))
        {
            _logger.LogWarning("Movement failed for {PlayerName}: {Error}",
                player.PlayerName, movementResult.ErrorMessage);

            _movementSystem.FlagPlayerForMonitoring(player.PlayerId, movementResult.ErrorMessage);
        }

        player.Direction = input.AimDirection;

        // Usar CombatSystem para ataques y habilidades
        if (input.IsAttacking && _combatSystem.CanAttack(player))
        {
            ProcessAttack(player);
        }

        if (!string.IsNullOrEmpty(input.AbilityType))
        {
            ProcessAbility(player, input.AbilityType, input.AbilityTarget);
        }

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

    private void ProcessChat(RealTimePlayer player, ChatMessage chat)
    {
        _logger.LogInformation("Chat from {PlayerName} [{ChatType}]: {Message}",
            player.PlayerName, chat.ChatType, chat.Message);
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
            _logger.LogWarning("Player {PlayerName} tried to use non-existent item {ItemId}",
                player.PlayerName, useItem.ItemId);
            return;
        }

        var result = _lootSystem.UseItem(player, item, world);

        if (result.Success && result.ItemConsumed)
        {
            player.Inventory.Remove(item);
        }

        _logger.LogInformation("Player {PlayerName} used item {ItemName}: {Success}",
            player.PlayerName, item.ItemName, result.Success ? "Success" : $"Failed - {result.ErrorMessage}");
    }

    private async void ProcessAttack(RealTimePlayer attacker)
    {
        var world = FindWorldByPlayer(attacker.PlayerId);
        if (world == null) return;

        var potentialTargets = world.Players.Values
            .Where(p => p.IsAlive && p.TeamId != attacker.TeamId)
            .ToList();

        var result = await _combatSystem.ProcessAttack(attacker, potentialTargets, world);

        if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
        {
            _logger.LogDebug("Attack failed for {PlayerName}: {Error}",
                attacker.PlayerName, result.ErrorMessage);
        }
    }

    private async void ProcessAbility(RealTimePlayer player, string abilityType, Vector2 target)
    {
        var world = FindWorldByPlayer(player.PlayerId);
        if (world == null) return;

        if (IsMovementAbility(abilityType))
        {
            await ProcessMovementAbility(player, abilityType, target, world);
        }
        else
        {
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
    }

    private async Task ProcessMovementAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world)
    {
        switch (abilityType.ToLower())
        {
            case "dash":
            case "teleport":
                var teleportResult = _movementSystem.TeleportPlayer(player, target, world);
                if (teleportResult.Success)
                {
                    // ⭐ DELTA COMPRESSION: Force update after teleport (significant position change)
                    player.ForceNextUpdate();

                    _logger.LogInformation("Player {PlayerName} used {AbilityType} to {Position}",
                        player.PlayerName, abilityType, teleportResult.FinalPosition);

                    if (teleportResult.RoomChanged)
                    {
                        _logger.LogDebug("Player {PlayerName} changed room via {AbilityType} to {NewRoom}",
                            player.PlayerName, abilityType, teleportResult.NewRoomId);
                    }
                }
                else
                {
                    _logger.LogDebug("{AbilityType} failed for {PlayerName}: {Error}",
                        abilityType, player.PlayerName, teleportResult.ErrorMessage);
                }
                break;
        }
    }

    private bool IsMovementAbility(string abilityType)
    {
        var movementAbilities = new[] { "dash", "teleport", "charge" };
        return movementAbilities.Contains(abilityType.ToLower());
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

    private void CheckRoomCompletion(GameWorld world)
    {
        foreach (var room in world.Rooms.Values.Where(r => !r.IsCompleted))
        {
            var playersInRoom = world.Players.Values
                .Where(p => p.IsAlive && p.CurrentRoomId == room.RoomId)
                .ToList();

            var mobsInRoom = world.Mobs.Values
                .Where(m => m.RoomId == room.RoomId && m.Health > 0)
                .ToList();

            if (playersInRoom.Any() && !mobsInRoom.Any())
            {
                CompleteRoom(world, room, playersInRoom.First().TeamId);
            }
        }
    }

    private void CompleteRoom(GameWorld world, Room room, string completingTeamId)
    {
        room.IsCompleted = true;
        room.CompletedByTeam = completingTeamId;
        room.CompletedAt = DateTime.UtcNow;

        _logger.LogInformation("Room {RoomId} completed by team {TeamId}", room.RoomId, completingTeamId);

        var teamMembers = world.Players.Values
            .Where(p => p.TeamId == completingTeamId && p.IsAlive)
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
        var requiredXP = GameMathUtils.CalculateExperienceRequired(player.Level);

        if (player.ExperiencePoints >= requiredXP)
        {
            player.Level++;
            player.ExperiencePoints -= requiredXP;

            player.MaxHealth += 20;
            player.Health = player.MaxHealth;
            player.MaxMana += 15;
            player.Mana = player.MaxMana;

            foreach (var stat in player.Stats.Keys.ToList())
            {
                player.Stats[stat] += 2;
            }

            _logger.LogInformation("Player {PlayerName} leveled up to level {Level}!",
                player.PlayerName, player.Level);
        }
    }

    private void CheckWorldCompletion(GameWorld world)
    {
        var completedRooms = world.Rooms.Values.Count(r => r.IsCompleted);
        var totalRooms = world.Rooms.Count;

        if (completedRooms >= totalRooms * 0.8 && !world.IsCompleted)
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

            foreach (var extraction in world.ExtractionPoints.Values)
            {
                extraction.IsActive = true;
            }

            _logger.LogInformation("World {WorldId} completed! Winning team: {WinningTeam}. Extraction points activated.",
                world.WorldId, world.WinningTeam);
        }
    }

    private void CheckWinConditions(GameWorld world)
    {
        if (world.IsCompleted) return;

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

            foreach (var extraction in world.ExtractionPoints.Values)
            {
                extraction.IsActive = true;
            }

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
    // COMBAT STATE UPDATES (sin cambios)
    // =============================================

    private void UpdateCombatStates(GameWorld world, float deltaTime)
    {
        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive) continue;

            // Regeneración natural de salud
            if ((DateTime.UtcNow - player.LastDamageTime).TotalSeconds > 5.0)
            {
                if (player.Health < player.MaxHealth)
                {
                    var regenAmount = (int)(10 * deltaTime);
                    player.Health = Math.Min(player.MaxHealth, player.Health + regenAmount);
                }
            }

            // Regeneración de mana
            if (player.Mana < player.MaxMana)
            {
                var manaRegen = player.PlayerClass switch
                {
                    "support" => 25,
                    "scout" => 20,
                    "tank" => 15,
                    _ => 20
                };

                var regenAmount = (int)(manaRegen * deltaTime);
                player.Mana = Math.Min(player.MaxMana, player.Mana + regenAmount);
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
                    var player = world.Players.Values.FirstOrDefault(p => p.PlayerId == playerId);
                    if (player == null || !player.IsAlive ||
                        GameMathUtils.Distance(player.Position, extraction.Position) > 3.0f)
                    {
                        CancelPlayerExtraction(world, playerId, extraction);
                        playersToRemove.Add(playerId);
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
        }
    }

    private void CompletePlayerExtraction(GameWorld world, string playerId, ExtractionPoint extraction)
    {
        var player = world.Players.Values.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return;

        var extractedValue = CalculateExtractionValue(player);
        var bonusXP = player.Inventory.Sum(item => item.Rarity * 100);

        player.ExperiencePoints += bonusXP;

        _logger.LogInformation("Player {PlayerName} successfully extracted with {ItemCount} items worth {Value} points",
            player.PlayerName, player.Inventory.Count, extractedValue);

        world.Players.TryRemove(playerId, out _);
    }

    private void CancelPlayerExtraction(GameWorld world, string playerId, ExtractionPoint extraction)
    {
        var player = world.Players.Values.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return;

        _logger.LogInformation("Player {PlayerName} extraction cancelled at {ExtractionId}",
            player.PlayerName, extraction.ExtractionId);
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

            mobList.Add(mobUpdate);
            m.IsDirty = false; // Clear dirty flag
        }

        worldUpdate.Players = playerList;
        worldUpdate.MobUpdates = mobList;
        worldUpdate.CombatEvents = GetAndClearRecentCombatEvents(world.WorldId);
        worldUpdate.LootUpdates = GetAndClearRecentLootUpdates(world.WorldId);

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

    private void AddCombatEvent(string worldId, CombatEvent combatEvent)
    {
        if (!_recentCombatEvents.ContainsKey(worldId))
        {
            _recentCombatEvents[worldId] = new List<CombatEvent>();
        }

        _recentCombatEvents[worldId].Add(combatEvent);
    }

    private void AddLootUpdate(string worldId, LootUpdate lootUpdate)
    {
        if (!_recentLootUpdates.ContainsKey(worldId))
        {
            _recentLootUpdates[worldId] = new List<LootUpdate>();
        }

        _recentLootUpdates[worldId].Add(lootUpdate);
    }

    private List<CombatEvent> GetAndClearRecentCombatEvents(string worldId)
    {
        if (_recentCombatEvents.TryGetValue(worldId, out var events))
        {
            var result = new List<CombatEvent>(events);
            events.Clear();
            return result;
        }

        return new List<CombatEvent>();
    }

    private List<LootUpdate> GetAndClearRecentLootUpdates(string worldId)
    {
        if (_recentLootUpdates.TryGetValue(worldId, out var updates))
        {
            var result = new List<LootUpdate>(updates);
            updates.Clear();
            return result;
        }

        return new List<LootUpdate>();
    }

    // =============================================
    // ⭐ REFACTORED: MATCHMAKING SYSTEM (now using LobbyManager)
    // =============================================

    public string FindOrCreateWorld(string teamId)
    {
        // ⭐ REFACTORED: Delegate to LobbyManager
        return _lobbyManager.FindOrCreateLobby(teamId);
    }

    // =============================================
    // ⭐ REFACTORED: PLAYER MANAGEMENT (now using LobbyManager)
    // =============================================

    public bool AddPlayerToWorld(string worldId, RealTimePlayer player)
    {
        // ⭐ REFACTORED: Use WorldManager to check for active world
        var existingWorld = _worldManager.GetWorld(worldId);
        if (existingWorld != null)
        {
            return AddPlayerToExistingWorld(existingWorld, player);
        }

        // ⭐ REFACTORED: Delegate to LobbyManager for lobby joins
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
            _logger.LogInformation("Creating game world from lobby {LobbyId} with {PlayerCount} players",
                lobby.LobbyId, lobby.TotalPlayers);

            // Prepare players with spawn positions
            var playersDict = new Dictionary<string, RealTimePlayer>();
            foreach (var player in lobby.Players.Values)
            {
                player.Position = _worldManager.GetTeamSpawnPosition(player.TeamId);
                playersDict[player.PlayerId] = player;
            }

            // ⭐ REFACTORED: Use WorldManager to create world (handles rooms, extraction, loot)
            var world = _worldManager.CreateWorld(lobby.LobbyId, playersDict);

            // Set player room IDs after world creation
            foreach (var player in world.Players.Values)
            {
                player.CurrentRoomId = _movementSystem.GetRoomIdByPosition(world, player.Position);
            }

            // ⭐ Spawn initial mobs
            var spawnedMobs = _mobAISystem.SpawnInitialMobs(world);

            _logger.LogInformation("✅ Game started! World {WorldId} with {PlayerCount} players from {TeamCount} teams and {MobCount} mobs",
                world.WorldId, world.Players.Count, lobby.TeamPlayerCounts.Count, spawnedMobs.Count);

            // ⭐ REFACTORED: Notify LobbyManager that transition is complete
            _lobbyManager.CompleteLobbyStart(lobby.LobbyId);

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
    /// ⭐ REFACTORED: Find player across all worlds (delegated to WorldManager).
    /// </summary>
    private RealTimePlayer? FindPlayer(string playerId)
    {
        return _worldManager.FindPlayer(playerId);
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