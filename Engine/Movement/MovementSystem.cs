using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Movement.Interface;
using MazeWars.GameServer.Engine.Movement.Models;
using MazeWars.GameServer.Engine.Movement.Services;
using MazeWars.GameServer.Engine.Movement.Settings;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MazeWars.GameServer.Services.Movement;

public class MovementSystem : IMovementSystem
{
    private readonly ILogger<MovementSystem> _logger;
    private readonly GameServerSettings _settings;
    private readonly MovementSettings _movementSettings;

    // Spatial optimization
    private readonly Dictionary<string, SpatialGrid> _worldSpatialGrids = new();
    private readonly Dictionary<string, Dictionary<string, RoomBounds>> _worldRoomBounds = new();
    private readonly Dictionary<string, List<CorridorBounds>> _worldCorridorBounds = new();

    // Anti-cheat tracking
    private readonly Dictionary<string, PlayerMovementTracker> _playerTrackers = new();
    private readonly Dictionary<string, MovementStats> _worldStats = new();

    // Events
    public event Action<RealTimePlayer, string, string>? OnRoomChanged;
    public event Action<GameWorld, string, List<RealTimePlayer>>? OnPlayersInRoom;

    public MovementSystem(ILogger<MovementSystem> logger, IOptions<GameServerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;

        // Initialize movement settings from game server settings
        _movementSettings = new MovementSettings
        {
            BaseMovementSpeed = _settings.GameBalance.MovementSpeed,
            SprintMultiplier = _settings.GameBalance.SprintMultiplier,
            ManaCostPerSprintSecond = 1,
            PlayerCollisionRadius = 0.8f,
            MobCollisionRadius = 1.0f,
            MaxInputMagnitude = 1.1f,
            TeleportMaxDistance = 15.0f,
            EnableCollisionPrediction = true
        };
    }

    // =============================================
    // POSITION VALIDATION
    // =============================================

    public bool IsValidPosition(GameWorld world, Vector2 position)
    {
        var roomBounds = GetOrCreateRoomBounds(world);

        // Check if inside any room
        foreach (var bounds in roomBounds.Values)
        {
            if (position.X >= bounds.MinBounds.X && position.X <= bounds.MaxBounds.X &&
                position.Y >= bounds.MinBounds.Y && position.Y <= bounds.MaxBounds.Y)
                return true;
        }

        // Check if inside any corridor between connected rooms
        var corridors = GetOrCreateCorridorBounds(world);
        foreach (var corridor in corridors)
        {
            if (position.X >= corridor.MinBounds.X && position.X <= corridor.MaxBounds.X &&
                position.Y >= corridor.MinBounds.Y && position.Y <= corridor.MaxBounds.Y)
            {
                // Block if corridor has a locked door (entire corridor blocked)
                if (corridor.ConnectionId != null &&
                    world.LockedDoors.TryGetValue(corridor.ConnectionId, out var door) &&
                    door.IsLocked)
                    return false;

                // For closed doors: block a thin strip at the door position
                // Uses room-centers midpoint (matches client door visual position)
                if (corridor.ConnectionId != null &&
                    world.Doors.TryGetValue(corridor.ConnectionId, out var roomDoor) &&
                    !roomDoor.IsOpen)
                {
                    const float doorBlockThickness = 0.5f;

                    if (corridor.IsHorizontal)
                    {
                        if (Math.Abs(position.X - corridor.DoorMidpoint.X) < doorBlockThickness)
                            return false;
                    }
                    else
                    {
                        if (Math.Abs(position.Y - corridor.DoorMidpoint.Y) < doorBlockThickness)
                            return false;
                    }
                }

                return true;
            }
        }

        return false;
    }

    public bool IsValidMovementInput(PlayerInputMessage input, RealTimePlayer player)
    {
        var validation = ValidateMovementInput(input, player);

        if (!validation.IsValid)
        {
            _logger.LogWarning("Invalid movement from {PlayerName}: {Error} (Suspicion: {Suspicion:F2})",
                player.PlayerName, validation.ValidationError, validation.SuspicionLevel);

            // Track suspicious behavior
            var tracker = GetOrCreatePlayerTracker(player.PlayerId);
            tracker.SuspiciousMovements++;

            if (validation.SuspicionLevel > 0.8f)
            {
                tracker.IsBeingMonitored = true;
                _logger.LogWarning("Player {PlayerName} flagged for movement monitoring", player.PlayerName);
            }
        }

        return validation.IsValid;
    }

    private MovementValidation ValidateMovementInput(PlayerInputMessage input, RealTimePlayer player)
    {
        var validation = new MovementValidation { IsValid = true };

        // Click-to-move: validate target position is within world bounds
        if (input.HasMoveTarget)
        {
            var worldSize = _movementSettings.WorldSize;
            if (Math.Abs(input.MoveTarget.X) > worldSize + 10 ||
                Math.Abs(input.MoveTarget.Y) > worldSize + 10)
            {
                validation.IsValid = false;
                validation.ValidationError = "Move target outside world bounds";
                validation.SuspicionLevel = 0.8f;
                validation.ValidationFlags.Add("INVALID_TARGET");
                return validation;
            }
        }

        // Check for impossible speeds based on movement history
        var tracker = GetOrCreatePlayerTracker(player.PlayerId);

        // Skip speed/teleport checks for a grace period after a dash ability
        var timeSinceDash = (DateTime.UtcNow - player.LastDashTime).TotalMilliseconds;
        if (timeSinceDash < 500)
        {
            // Player just dashed — reset tracker to avoid stale history contaminating future checks
            tracker.RecentPositions.Clear();
            tracker.PositionTimestamps.Clear();
            tracker.AddPosition(player.Position);
            return validation;
        }

        tracker.AddPosition(player.Position);

        var averageSpeed = tracker.CalculateAverageSpeed();
        var maxAllowedSpeed = CalculateMovementSpeed(player, true) * _movementSettings.SpeedToleranceMultiplier;

        if (averageSpeed > maxAllowedSpeed)
        {
            validation.SuspicionLevel += 0.3f;
            validation.ValidationFlags.Add("HIGH_SPEED");

            if (averageSpeed > maxAllowedSpeed * 1.5f)
            {
                validation.IsValid = false;
                validation.ValidationError = $"Speed too high: {averageSpeed:F2} (max: {maxAllowedSpeed:F2})";
            }
        }

        // Check for teleportation (sudden position jumps)
        if (tracker.RecentPositions.Count >= 2)
        {
            var lastPos = tracker.RecentPositions[^2];
            var distance = Vector2.Distance(lastPos, player.Position);
            var timeDiff = (DateTime.UtcNow - tracker.PositionTimestamps[^2]).TotalSeconds;

            if (timeDiff > 0 && distance / timeDiff > maxAllowedSpeed * 2)
            {
                validation.SuspicionLevel += _movementSettings.TeleportSuspicionLevel;
                validation.ValidationFlags.Add("TELEPORT_DETECTED");
            }
        }

        return validation;
    }

    // =============================================
    // MOVEMENT PROCESSING
    // =============================================

    public MovementResult UpdatePlayerMovement(RealTimePlayer player, PlayerInputMessage input, GameWorld world, float deltaTime)
    {
        var result = new MovementResult
        {
            Success = false,
            NewPosition = player.Position,
            NewVelocity = player.Velocity
        };

        if (!player.IsAlive || player.IsCasting)
        {
            // No error message: casting/dead is a normal state, not suspicious
            return result;
        }

        // Validate input
        if (!IsValidMovementInput(input, player))
        {
            result.ErrorMessage = "Invalid movement input";
            return result;
        }

        // Click-to-move: process new target if client sent a click
        if (input.HasMoveTarget)
        {
            var target = input.MoveTarget;
            if (IsValidPosition(world, target))
            {
                player.MoveTarget = target;
                player.IsPathing = true;
            }
        }

        // Stop command (S key or click on self)
        if (input.StopMovement)
        {
            player.MoveTarget = null;
            player.IsPathing = false;
            player.Velocity = Vector2.Zero;
            player.IsMoving = false;
        }

        // Set sprint state from input
        player.IsSprinting = input.IsSprinting && player.Mana > 0;

        // Aim direction is independent from movement (like LoL)
        player.Direction = input.AimDirection;

        // Position is NOT updated here — UpdateAllPlayersMovement handles movement
        result.Success = true;
        return result;
    }

    public void UpdateAllPlayersMovement(GameWorld world, float deltaTime)
    {
        var stats = GetOrCreateWorldStats(world.WorldId);

        foreach (var player in world.Players.Values)
        {
            if (!player.IsAlive || player.IsCasting) continue;

            // Click-to-move: continuous steering toward target
            if (player.IsPathing && player.MoveTarget.HasValue)
            {
                var toTarget = player.MoveTarget.Value - player.Position;
                var distance = toTarget.Magnitude;

                if (distance <= _movementSettings.ArrivalThreshold)
                {
                    player.MoveTarget = null;
                    player.IsPathing = false;
                    player.Velocity = Vector2.Zero;
                    player.IsMoving = false;
                    player.IsSprinting = false;
                }
                else
                {
                    // Sprint mana cost
                    if (player.IsSprinting && player.Mana > 0)
                    {
                        var manaCost = (int)(_movementSettings.ManaCostPerSprintSecond * deltaTime);
                        player.Mana = Math.Max(0, player.Mana - manaCost);
                    }
                    else
                    {
                        player.IsSprinting = false;
                    }

                    var speed = CalculateMovementSpeed(player, player.IsSprinting);
                    var direction = toTarget.GetNormalized();
                    var desiredVelocity = direction * speed;

                    // Smooth steering to avoid jerky direction changes
                    if (_movementSettings.SteeringSmoothing > 0 && player.Velocity.Magnitude > 0.1f)
                    {
                        var t = Math.Min(1f, deltaTime / _movementSettings.SteeringSmoothing);
                        player.Velocity = Vector2.Lerp(player.Velocity, desiredVelocity, t);
                    }
                    else
                    {
                        player.Velocity = desiredVelocity;
                    }
                    player.IsMoving = true;
                }
            }

            // Apply velocity
            if (player.Velocity.Magnitude > 0.01f)
            {
                var newPosition = player.Position + player.Velocity * deltaTime;

                if (IsValidPosition(world, newPosition))
                {
                    player.Position = newPosition;
                }
                else
                {
                    // Wall collision: try sliding along each axis separately
                    var slideX = new Vector2(newPosition.X, player.Position.Y);
                    var slideY = new Vector2(player.Position.X, newPosition.Y);

                    if (IsValidPosition(world, slideX))
                    {
                        player.Position = slideX;
                        player.Velocity = new Vector2(player.Velocity.X, 0);
                    }
                    else if (IsValidPosition(world, slideY))
                    {
                        player.Position = slideY;
                        player.Velocity = new Vector2(0, player.Velocity.Y);
                    }
                    else
                    {
                        // Completely blocked by wall
                        player.Velocity = Vector2.Zero;
                        player.IsMoving = false;
                        player.MoveTarget = null;
                        player.IsPathing = false;
                        stats.CollisionsDetected++;
                    }
                }
            }

            stats.TotalMovementUpdates++;
        }

        // Update spatial grid for collision optimization
        UpdateSpatialGrid(world);
    }

    public float CalculateMovementSpeed(RealTimePlayer player, bool isSprinting)
    {
        var baseSpeed = _movementSettings.BaseMovementSpeed * player.MovementSpeedModifier;

        if (isSprinting && player.Mana > 0)
        {
            baseSpeed *= _movementSettings.SprintMultiplier;
        }

        // Apply class-based speed modifiers
        var classModifier = player.PlayerClass.ToLower() switch
        {
            "scout" => 1.1f,
            "tank" => 0.9f,
            "support" => 1.0f,
            "assassin" => 1.15f,
            "warlock" => 0.95f,
            _ => 1.0f
        };

        var speed = baseSpeed * classModifier;

        // Apply weight penalty: linear from 1.0x at capacity to MinPenalty at 2x capacity
        var capacity = _settings.GameBalance.PlayerCarryCapacity;
        if (capacity > 0 && player.CurrentWeight > capacity)
        {
            var overweightRatio = Math.Min((player.CurrentWeight - capacity) / capacity, 1.0f);
            var minMultiplier = _settings.GameBalance.OverweightSpeedPenalty;
            var weightMultiplier = 1.0f - overweightRatio * (1.0f - minMultiplier);
            speed *= weightMultiplier;
        }

        return speed;
    }

    // =============================================
    // COLLISION SYSTEM
    // =============================================

    public void ProcessCollisions(GameWorld world)
    {
        var stats = GetOrCreateWorldStats(world.WorldId);
        var spatialGrid = GetOrCreateSpatialGrid(world.WorldId);

        var alivePlayers = world.Players.Values.Where(p => p.IsAlive).ToList();

        foreach (var player in alivePlayers)
        {
            var collisions = new List<CollisionInfo>();

            // Check collision with mobs in nearby cells
            var mobCollisions = CheckMobCollisions(player, world, spatialGrid);
            collisions.AddRange(mobCollisions);

            // Check collision with other players in nearby cells
            var playerCollisions = CheckPlayerCollisions(player, alivePlayers, spatialGrid);
            collisions.AddRange(playerCollisions);

            // Resolve collisions
            foreach (var collision in collisions)
            {
                ResolveCollision(player, collision, world);
                stats.CollisionsDetected++;
            }
        }
    }

    private List<CollisionInfo> CheckMobCollisions(RealTimePlayer player, GameWorld world, SpatialGrid spatialGrid)
    {
        var collisions = new List<CollisionInfo>();
        var playerCell = spatialGrid.GetCellCoords(player.Position);

        // Check current cell and adjacent cells
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                var cellCoord = (playerCell.Item1 + dx, playerCell.Item2 + dy);
                if (spatialGrid.MobCells.TryGetValue(cellCoord, out var mobIds))
                {
                    foreach (var mobId in mobIds)
                    {
                        if (string.IsNullOrEmpty(mobId)) continue;
                        if (world.Mobs.TryGetValue(mobId, out var mob) &&
                            mob.Health > 0 &&
                            mob.RoomId == player.CurrentRoomId)
                        {
                            var distance = GameMathUtils.Distance(player.Position, mob.Position);
                            if (distance < _movementSettings.MobCollisionRadius)
                            {
                                var collision = CreateCollisionInfo(player.Position, mob.Position,
                                    CollisionType.Mob, mobId, _movementSettings.MobCollisionRadius);
                                collisions.Add(collision);
                            }
                        }
                    }
                }
            }
        }

        return collisions;
    }

    private List<CollisionInfo> CheckPlayerCollisions(RealTimePlayer player, List<RealTimePlayer> allPlayers, SpatialGrid spatialGrid)
    {
        var collisions = new List<CollisionInfo>();
        var playerCell = spatialGrid.GetCellCoords(player.Position);

        // Check current cell and adjacent cells
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                var cellCoord = (playerCell.Item1 + dx, playerCell.Item2 + dy);
                if (spatialGrid.PlayerCells.TryGetValue(cellCoord, out var playerIds))
                {
                    foreach (var playerId in playerIds)
                    {
                        if (playerId == player.PlayerId) continue;

                        var otherPlayer = allPlayers.FirstOrDefault(p => p.PlayerId == playerId);
                        if (otherPlayer != null && otherPlayer.IsAlive)
                        {
                            var distance = GameMathUtils.Distance(player.Position, otherPlayer.Position);
                            if (distance < _movementSettings.PlayerCollisionRadius)
                            {
                                var collision = CreateCollisionInfo(player.Position, otherPlayer.Position,
                                    CollisionType.Player, playerId, _movementSettings.PlayerCollisionRadius);
                                collisions.Add(collision);
                            }
                        }
                    }
                }
            }
        }

        return collisions;
    }

    private CollisionInfo CreateCollisionInfo(Vector2 pos1, Vector2 pos2, CollisionType type, string objectId, float radius)
    {
        var direction = (pos1 - pos2);
        var distance = direction.Magnitude;
        var normal = distance > 0 ? direction.GetNormalized() : Vector2.UnitX;

        return new CollisionInfo
        {
            Type = type,
            ObjectId = objectId,
            CollisionPoint = pos2 + normal * (distance / 2),
            CollisionNormal = normal,
            PenetrationDepth = radius - distance
        };
    }

    private void ResolveCollision(RealTimePlayer player, CollisionInfo collision, GameWorld world)
    {
        // Only affect players who are moving toward the collision
        // A stationary player won't be pushed by someone walking into them
        var dot = Vector2.Dot(player.Velocity, collision.CollisionNormal);
        if (collision.Type == CollisionType.Player && dot >= 0)
            return; // Not moving toward this player, skip

        var pushDistance = Math.Max(0.05f, collision.PenetrationDepth);
        var pushVector = collision.CollisionNormal * pushDistance;
        var pushedPosition = player.Position + pushVector;

        // Validate pushed position against room/corridor bounds
        if (IsValidPosition(world, pushedPosition))
        {
            player.Position = pushedPosition;
        }
        else
        {
            // Try pushing along each axis separately (wall-aware)
            var pushX = new Vector2(pushedPosition.X, player.Position.Y);
            var pushY = new Vector2(player.Position.X, pushedPosition.Y);

            if (IsValidPosition(world, pushX))
                player.Position = pushX;
            else if (IsValidPosition(world, pushY))
                player.Position = pushY;
            // else: can't push without entering a wall, skip push entirely
        }

        // Remove velocity component toward the collision (slide along surface)
        if (dot < 0)
        {
            player.Velocity -= collision.CollisionNormal * dot;
        }
    }

    // =============================================
    // ROOM SYSTEM
    // =============================================

    public RoomTransitionResult CheckRoomTransition(RealTimePlayer player, GameWorld world)
    {
        var result = new RoomTransitionResult();
        var newRoomId = GetRoomIdByPosition(world, player.Position);

        if (newRoomId != player.CurrentRoomId && !string.IsNullOrEmpty(newRoomId))
        {
            result.RoomChanged = true;
            result.OldRoomId = player.CurrentRoomId;
            result.NewRoomId = newRoomId;

            var oldRoomId = player.CurrentRoomId;
            player.CurrentRoomId = newRoomId;

            _logger.LogDebug("Player {PlayerName} moved from room {OldRoom} to {NewRoom}",
                player.PlayerName, oldRoomId, newRoomId);

            // Fire room changed event
            OnRoomChanged?.Invoke(player, oldRoomId, newRoomId);

            // Check for encounters in new room
            var playersInRoom = world.Players.Values
                .Where(p => p.IsAlive && p.CurrentRoomId == newRoomId)
                .ToList();

            result.PlayersInRoom = playersInRoom.Count;
            result.TeamsInRoom = playersInRoom.Select(p => p.TeamId).Distinct().ToList();

            if (result.TeamsInRoom.Count > 1)
            {
                result.EncounterDetected = true;
                _logger.LogDebug("PvP encounter detected in room {RoomId} between teams: {Teams}",
                    newRoomId, string.Join(", ", result.TeamsInRoom));

                // Fire encounter event
                OnPlayersInRoom?.Invoke(world, newRoomId, playersInRoom);
            }

            // Update stats
            var stats = GetOrCreateWorldStats(world.WorldId);
            stats.RoomTransitions++;
            stats.RoomPopulation[newRoomId] = playersInRoom.Count;
        }

        return result;
    }

    public string GetRoomIdByPosition(GameWorld world, Vector2 position)
    {
        var roomBounds = GetOrCreateRoomBounds(world);

        // Check if inside a room
        foreach (var (roomId, bounds) in roomBounds)
        {
            if (position.X >= bounds.MinBounds.X && position.X <= bounds.MaxBounds.X &&
                position.Y >= bounds.MinBounds.Y && position.Y <= bounds.MaxBounds.Y)
            {
                return roomId;
            }
        }

        // Check corridors — return the closest connected room
        // If one room is corrupted and the other isn't, prefer the safe room
        // so players in corridors aren't stuck taking corruption damage
        var corridors = GetOrCreateCorridorBounds(world);
        foreach (var corridor in corridors)
        {
            if (position.X >= corridor.MinBounds.X && position.X <= corridor.MaxBounds.X &&
                position.Y >= corridor.MinBounds.Y && position.Y <= corridor.MaxBounds.Y)
            {
                var aCorrupted = world.CorruptedRooms.Contains(corridor.RoomIdA);
                var bCorrupted = world.CorruptedRooms.Contains(corridor.RoomIdB);

                // If only one side is corrupted, prefer the safe side
                if (aCorrupted && !bCorrupted) return corridor.RoomIdB;
                if (bCorrupted && !aCorrupted) return corridor.RoomIdA;

                // Both safe or both corrupted — use nearest center
                var distA = Vector2.Distance(position, roomBounds[corridor.RoomIdA].Center);
                var distB = Vector2.Distance(position, roomBounds[corridor.RoomIdB].Center);
                return distA <= distB ? corridor.RoomIdA : corridor.RoomIdB;
            }
        }

        return "room_1_1"; // Default fallback room
    }

    // =============================================
    // TELEPORTATION SYSTEM
    // =============================================

    public TeleportResult TeleportPlayer(RealTimePlayer player, Vector2 targetPosition, GameWorld world)
    {
        var result = new TeleportResult
        {
            Success = false,
            FinalPosition = player.Position
        };

        // Check teleport distance limit
        var distance = Vector2.Distance(player.Position, targetPosition);
        if (distance > _movementSettings.TeleportMaxDistance)
        {
            result.ErrorMessage = $"Teleport distance too far: {distance:F2} (max: {_movementSettings.TeleportMaxDistance})";
            return result;
        }

        // Validate target position
        if (!IsValidPosition(world, targetPosition))
        {
            // Try to find nearest valid position
            targetPosition = ClampToWorldBounds(targetPosition, world);
        }

        // Check for collisions at target position
        var wouldCollide = CheckPositionForCollisions(targetPosition, world, player.PlayerId);
        if (wouldCollide)
        {
            result.ErrorMessage = "Target position blocked";
            return result;
        }

        // Perform teleport
        var oldPosition = player.Position;
        player.Position = targetPosition;
        result.FinalPosition = targetPosition;
        result.Success = true;

        // Check if room changed
        var roomTransition = CheckRoomTransition(player, world);
        if (roomTransition.RoomChanged)
        {
            result.RoomChanged = true;
            result.NewRoomId = roomTransition.NewRoomId;
        }

        // Update stats
        var stats = GetOrCreateWorldStats(world.WorldId);
        stats.TeleportsPerformed++;

        _logger.LogDebug("Player {PlayerName} teleported from {OldPos} to {NewPos} (distance: {Distance:F2})",
            player.PlayerName, oldPosition, targetPosition, distance);

        return result;
    }

    private bool CheckPositionForCollisions(Vector2 position, GameWorld world, string excludePlayerId)
    {
        // Check collision with other players
        foreach (var otherPlayer in world.Players.Values)
        {
            if (otherPlayer.PlayerId == excludePlayerId || !otherPlayer.IsAlive) continue;

            var distance = Vector2.Distance(position, otherPlayer.Position);
            if (distance < _movementSettings.PlayerCollisionRadius)
            {
                return true;
            }
        }

        // Check collision with mobs
        foreach (var mob in world.Mobs.Values)
        {
            if (mob.Health <= 0) continue;

            var distance = Vector2.Distance(position, mob.Position);
            if (distance < _movementSettings.MobCollisionRadius)
            {
                return true;
            }
        }

        return false;
    }

    // =============================================
    // UTILITY METHODS
    // =============================================

    public Vector2 ClampToWorldBounds(Vector2 position, GameWorld world)
    {
        var worldSize = CalculateWorldSize(world);

        return new Vector2(
            Math.Clamp(position.X, -worldSize, worldSize),
            Math.Clamp(position.Y, -worldSize, worldSize)
        );
    }

    private float CalculateWorldSize(GameWorld world)
    {
        return _movementSettings.WorldSize;
    }


    private PlayerMovementTracker GetOrCreatePlayerTracker(string playerId)
    {
        if (!_playerTrackers.TryGetValue(playerId, out var tracker))
        {
            tracker = new PlayerMovementTracker
            {
                PlayerId = playerId,
                LastValidation = DateTime.UtcNow
            };
            _playerTrackers[playerId] = tracker;
        }
        return tracker;
    }

    private MovementStats GetOrCreateWorldStats(string worldId)
    {
        if (!_worldStats.TryGetValue(worldId, out var stats))
        {
            stats = new MovementStats
            {
                LastStatsReset = DateTime.UtcNow
            };
            _worldStats[worldId] = stats;
        }
        return stats;
    }

    private SpatialGrid GetOrCreateSpatialGrid(string worldId)
    {
        if (!_worldSpatialGrids.TryGetValue(worldId, out var grid))
        {
            grid = new SpatialGrid
            {
                CellSize = _movementSettings.SpatialGridCellSize
            };
            _worldSpatialGrids[worldId] = grid;
        }
        return grid;
    }

    private Dictionary<string, RoomBounds> GetOrCreateRoomBounds(GameWorld world)
    {
        if (!_worldRoomBounds.TryGetValue(world.WorldId, out var roomBounds))
        {
            roomBounds = new Dictionary<string, RoomBounds>();

            // Build room bounds cache from world data
            foreach (var room in world.Rooms.Values)
            {
                var bounds = new RoomBounds
                {
                    RoomId = room.RoomId,
                    Center = room.Position,
                    Size = room.Size,
                    MinBounds = new Vector2(
                        room.Position.X - room.Size.X / 2,
                        room.Position.Y - room.Size.Y / 2
                    ),
                    MaxBounds = new Vector2(
                        room.Position.X + room.Size.X / 2,
                        room.Position.Y + room.Size.Y / 2
                    ),
                    ConnectedRooms = new List<string>(room.Connections)
                };

                roomBounds[room.RoomId] = bounds;
            }

            _worldRoomBounds[world.WorldId] = roomBounds;
            _logger.LogDebug("Cached room bounds for world {WorldId}: {RoomCount} rooms",
                world.WorldId, roomBounds.Count);
        }

        return roomBounds;
    }

    private List<CorridorBounds> GetOrCreateCorridorBounds(GameWorld world)
    {
        if (!_worldCorridorBounds.TryGetValue(world.WorldId, out var corridors))
        {
            corridors = new List<CorridorBounds>();
            var halfCorridor = _movementSettings.CorridorWidth / 2f;
            var processedPairs = new HashSet<string>();

            foreach (var room in world.Rooms.Values)
            {
                foreach (var connectedRoomId in room.Connections)
                {
                    // Only process each pair once
                    var pairKey = string.Compare(room.RoomId, connectedRoomId, StringComparison.Ordinal) < 0
                        ? $"{room.RoomId}|{connectedRoomId}"
                        : $"{connectedRoomId}|{room.RoomId}";

                    if (!processedPairs.Add(pairKey)) continue;
                    if (!world.Rooms.TryGetValue(connectedRoomId, out var otherRoom)) continue;

                    var dx = otherRoom.Position.X - room.Position.X;
                    var dy = otherRoom.Position.Y - room.Position.Y;

                    CorridorBounds corridor;

                    if (Math.Abs(dx) > Math.Abs(dy))
                    {
                        // Horizontal connection (rooms differ in X)
                        var leftRoom = dx > 0 ? room : otherRoom;
                        var rightRoom = dx > 0 ? otherRoom : room;
                        var centerY = (leftRoom.Position.Y + rightRoom.Position.Y) / 2f;
                        var leftWall = leftRoom.Position.X + leftRoom.Size.X / 2f;
                        var rightWall = rightRoom.Position.X - rightRoom.Size.X / 2f;

                        // Door at CENTER of corridor (midpoint between both room walls)
                        var doorMidpoint = new Vector2(
                            (leftWall + rightWall) / 2f,
                            centerY);

                        corridor = new CorridorBounds
                        {
                            MinBounds = new Vector2(leftWall, centerY - halfCorridor),
                            MaxBounds = new Vector2(rightWall, centerY + halfCorridor),
                            RoomIdA = leftRoom.RoomId,
                            RoomIdB = rightRoom.RoomId,
                            DoorMidpoint = doorMidpoint,
                            IsHorizontal = true
                        };
                    }
                    else
                    {
                        // Vertical connection (rooms differ in Y)
                        var topRoom = dy > 0 ? room : otherRoom;
                        var bottomRoom = dy > 0 ? otherRoom : room;
                        var centerX = (topRoom.Position.X + bottomRoom.Position.X) / 2f;
                        var topWall = topRoom.Position.Y + topRoom.Size.Y / 2f;
                        var bottomWall = bottomRoom.Position.Y - bottomRoom.Size.Y / 2f;

                        // Door at CENTER of corridor (midpoint between both room walls)
                        var doorMidpoint = new Vector2(
                            centerX,
                            (topWall + bottomWall) / 2f);

                        corridor = new CorridorBounds
                        {
                            MinBounds = new Vector2(centerX - halfCorridor, topWall),
                            MaxBounds = new Vector2(centerX + halfCorridor, bottomWall),
                            RoomIdA = topRoom.RoomId,
                            RoomIdB = bottomRoom.RoomId,
                            DoorMidpoint = doorMidpoint,
                            IsHorizontal = false
                        };
                    }

                    // Set connection ID for locked door checks
                    var connId = string.Compare(room.RoomId, connectedRoomId, StringComparison.Ordinal) < 0
                        ? $"{room.RoomId}_{connectedRoomId}" : $"{connectedRoomId}_{room.RoomId}";
                    corridor.ConnectionId = connId;

                    corridors.Add(corridor);
                }
            }

            _worldCorridorBounds[world.WorldId] = corridors;
            _logger.LogDebug("Cached {CorridorCount} corridor bounds for world {WorldId}",
                corridors.Count, world.WorldId);
        }

        return corridors;
    }

    private void UpdateSpatialGrid(GameWorld world)
    {
        var spatialGrid = GetOrCreateSpatialGrid(world.WorldId);

        // Clear previous frame data
        spatialGrid.PlayerCells.Clear();
        spatialGrid.MobCells.Clear();

        // Add players to spatial grid
        foreach (var player in world.Players.Values.Where(p => p.IsAlive))
        {
            var cellCoord = spatialGrid.GetCellCoords(player.Position);
            var list = spatialGrid.PlayerCells.GetOrAdd(cellCoord, _ => new List<string>());
            list.Add(player.PlayerId);
        }

        // Add mobs to spatial grid
        foreach (var mob in world.Mobs.Values.Where(m => m.Health > 0))
        {
            var cellCoord = spatialGrid.GetCellCoords(mob.Position);
            var list = spatialGrid.MobCells.GetOrAdd(cellCoord, _ => new List<string>());
            list.Add(mob.MobId);
        }
    }

    // =============================================
    // CLEANUP AND OPTIMIZATION
    // =============================================

    public void CleanupWorldData(string worldId)
    {
        _worldSpatialGrids.Remove(worldId);
        _worldRoomBounds.Remove(worldId);
        _worldCorridorBounds.Remove(worldId);
        _worldStats.Remove(worldId);

        // Clean up player trackers for players no longer in any world
        // This would need world context to determine which players to remove
        _logger.LogDebug("Cleaned up movement data for world {WorldId}", worldId);
    }

    public void CleanupPlayerTracker(string playerId)
    {
        if (_playerTrackers.Remove(playerId))
        {
            _logger.LogDebug("Cleaned up movement tracker for player {PlayerId}", playerId);
        }
    }

    public void OptimizeMemoryUsage()
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-5);

        // Clean up old player trackers
        var expiredTrackers = _playerTrackers
            .Where(kvp => kvp.Value.LastValidation < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var playerId in expiredTrackers)
        {
            _playerTrackers.Remove(playerId);
        }

        if (expiredTrackers.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired player movement trackers", expiredTrackers.Count);
        }
    }

    // =============================================
    // STATISTICS AND MONITORING
    // =============================================

    public MovementStats GetMovementStats(string worldId)
    {
        return GetOrCreateWorldStats(worldId);
    }

    public Dictionary<string, object> GetDetailedMovementStats()
    {
        var totalUpdates = _worldStats.Values.Sum(s => s.TotalMovementUpdates);
        var totalCollisions = _worldStats.Values.Sum(s => s.CollisionsDetected);
        var totalTransitions = _worldStats.Values.Sum(s => s.RoomTransitions);
        var totalInvalidMovements = _worldStats.Values.Sum(s => s.InvalidMovements);
        var totalTeleports = _worldStats.Values.Sum(s => s.TeleportsPerformed);

        var suspiciousPlayers = _playerTrackers.Values
            .Where(t => t.SuspiciousMovements > 5 || t.IsBeingMonitored)
            .Count();

        var averagePlayerSpeed = _playerTrackers.Values
            .Where(t => t.RecentPositions.Count >= 2)
            .Select(t => t.CalculateAverageSpeed())
            .DefaultIfEmpty(0)
            .Average();

        return new Dictionary<string, object>
        {
            ["TotalMovementUpdates"] = totalUpdates,
            ["TotalCollisions"] = totalCollisions,
            ["TotalRoomTransitions"] = totalTransitions,
            ["TotalInvalidMovements"] = totalInvalidMovements,
            ["TotalTeleports"] = totalTeleports,
            ["SuspiciousPlayers"] = suspiciousPlayers,
            ["AveragePlayerSpeed"] = averagePlayerSpeed,
            ["TrackedPlayers"] = _playerTrackers.Count,
            ["ActiveWorlds"] = _worldStats.Count,
            ["CollisionRate"] = totalUpdates > 0 ? (double)totalCollisions / totalUpdates : 0,
            ["InvalidMovementRate"] = totalUpdates > 0 ? (double)totalInvalidMovements / totalUpdates : 0,
            ["WorldStats"] = _worldStats.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    kvp.Value.TotalMovementUpdates,
                    kvp.Value.CollisionsDetected,
                    kvp.Value.RoomTransitions,
                    kvp.Value.RoomPopulation
                }
            )
        };
    }

    public List<PlayerMovementTracker> GetSuspiciousPlayers()
    {
        return _playerTrackers.Values
            .Where(t => t.SuspiciousMovements > 3 || t.IsBeingMonitored)
            .OrderByDescending(t => t.SuspiciousMovements)
            .ToList();
    }

    // =============================================
    // ANTI-CHEAT METHODS
    // =============================================

    public void FlagPlayerForMonitoring(string playerId, string reason)
    {
        var tracker = GetOrCreatePlayerTracker(playerId);
        tracker.IsBeingMonitored = true;
        tracker.SuspiciousMovements++;

        _logger.LogWarning("Player {PlayerId} flagged for movement monitoring: {Reason}",
            playerId, reason);
    }

    public void ResetPlayerSuspicion(string playerId)
    {
        if (_playerTrackers.TryGetValue(playerId, out var tracker))
        {
            tracker.SuspiciousMovements = 0;
            tracker.IsBeingMonitored = false;
            _logger.LogInformation("Reset movement suspicion for player {PlayerId}", playerId);
        }
    }

    public bool IsPlayerSuspicious(string playerId)
    {
        if (_playerTrackers.TryGetValue(playerId, out var tracker))
        {
            return tracker.SuspiciousMovements > 5 || tracker.IsBeingMonitored;
        }
        return false;
    }

    // =============================================
    // PREDICTION AND INTERPOLATION
    // =============================================

    public Vector2 PredictPlayerPosition(RealTimePlayer player, float timeAhead)
    {
        if (!_movementSettings.EnableCollisionPrediction || timeAhead <= 0)
        {
            return player.Position;
        }

        var predictedPosition = player.Position + player.Velocity * timeAhead;

        // Add some randomness based on player movement patterns
        var tracker = GetOrCreatePlayerTracker(player.PlayerId);
        if (tracker.RecentPositions.Count >= 3)
        {
            // Calculate movement trend
            var recentMovement = tracker.RecentPositions[^1] - tracker.RecentPositions[^3];
            var trend = recentMovement * 0.1f; // Small adjustment based on trend
            predictedPosition += trend;
        }

        return predictedPosition;
    }

    public Vector2 InterpolatePosition(Vector2 fromPosition, Vector2 toPosition, float interpolationFactor)
    {
        interpolationFactor = Math.Clamp(interpolationFactor, 0f, 1f);
        return fromPosition + (toPosition - fromPosition) * interpolationFactor;
    }

    // =============================================
    // ADVANCED COLLISION DETECTION
    // =============================================

    public bool WillCollide(RealTimePlayer player, Vector2 targetPosition, GameWorld world)
    {
        // Simple collision prediction
        var steps = 10;
        var direction = (targetPosition - player.Position);
        var stepSize = direction.Magnitude / steps;
        var normalizedDirection = direction.GetNormalized();

        for (int i = 1; i <= steps; i++)
        {
            var checkPosition = player.Position + normalizedDirection * (stepSize * i);

            if (!IsValidPosition(world, checkPosition) ||
                CheckPositionForCollisions(checkPosition, world, player.PlayerId))
            {
                return true;
            }
        }

        return false;
    }

    public Vector2 FindNearestValidPosition(Vector2 desiredPosition, GameWorld world, string excludePlayerId)
    {
        if (IsValidPosition(world, desiredPosition) &&
            !CheckPositionForCollisions(desiredPosition, world, excludePlayerId))
        {
            return desiredPosition;
        }

        // Spiral search for valid position
        var searchRadius = 1f;
        var maxSearchRadius = 10f;
        var angleStep = 30f; // degrees

        while (searchRadius <= maxSearchRadius)
        {
            for (float angle = 0; angle < 360; angle += angleStep)
            {
                var radians = angle * Math.PI / 180f;
                var testPosition = desiredPosition + new Vector2(
                    (float)Math.Cos(radians) * searchRadius,
                    (float)Math.Sin(radians) * searchRadius
                );

                if (IsValidPosition(world, testPosition) &&
                    !CheckPositionForCollisions(testPosition, world, excludePlayerId))
                {
                    return testPosition;
                }
            }

            searchRadius += 1f;
        }

        // Fallback to clamped position
        return ClampToWorldBounds(desiredPosition, world);
    }

    // =============================================
    // ROOM TRANSITION HELPERS
    // =============================================

    public List<string> GetConnectedRooms(string roomId, GameWorld world)
    {
        var roomBounds = GetOrCreateRoomBounds(world);

        if (roomBounds.TryGetValue(roomId, out var bounds))
        {
            return bounds.ConnectedRooms;
        }

        return new List<string>();
    }

    public bool AreRoomsConnected(string roomId1, string roomId2, GameWorld world)
    {
        var connectedRooms = GetConnectedRooms(roomId1, world);
        return connectedRooms.Contains(roomId2);
    }

    public float GetDistanceToRoom(Vector2 position, string targetRoomId, GameWorld world)
    {
        var roomBounds = GetOrCreateRoomBounds(world);

        if (roomBounds.TryGetValue(targetRoomId, out var bounds))
        {
            return Vector2.Distance(position, bounds.Center);
        }

        return float.MaxValue;
    }

    // =============================================
    // PERFORMANCE OPTIMIZATION
    // =============================================

    public void UpdateMovementSettings(MovementSettings newSettings)
    {
        // Allow runtime updates to movement settings
        var oldSettings = _movementSettings;

        _movementSettings.BaseMovementSpeed = newSettings.BaseMovementSpeed;
        _movementSettings.SprintMultiplier = newSettings.SprintMultiplier;
        _movementSettings.PlayerCollisionRadius = newSettings.PlayerCollisionRadius;
        _movementSettings.MobCollisionRadius = newSettings.MobCollisionRadius;
        _movementSettings.MaxInputMagnitude = newSettings.MaxInputMagnitude;
        _movementSettings.TeleportMaxDistance = newSettings.TeleportMaxDistance;
        _movementSettings.EnableCollisionPrediction = newSettings.EnableCollisionPrediction;

        _logger.LogInformation("Updated movement settings - Speed: {Speed}, Sprint: {Sprint}, Collision: {Collision}",
            newSettings.BaseMovementSpeed, newSettings.SprintMultiplier, newSettings.EnableCollisionPrediction);
    }

    public MovementSettings GetCurrentMovementSettings()
    {
        return new MovementSettings
        {
            BaseMovementSpeed = _movementSettings.BaseMovementSpeed,
            SprintMultiplier = _movementSettings.SprintMultiplier,
            MaxInputMagnitude = _movementSettings.MaxInputMagnitude,
            PlayerCollisionRadius = _movementSettings.PlayerCollisionRadius,
            MobCollisionRadius = _movementSettings.MobCollisionRadius,
            WallPushbackForce = _movementSettings.WallPushbackForce,
            MinMovementThreshold = _movementSettings.MinMovementThreshold,
            ManaCostPerSprintSecond = _movementSettings.ManaCostPerSprintSecond,
            TeleportMaxDistance = _movementSettings.TeleportMaxDistance,
            EnableCollisionPrediction = _movementSettings.EnableCollisionPrediction,
            CollisionPredictionTime = _movementSettings.CollisionPredictionTime
        };
    }

    // =============================================
    // DEBUGGING AND DIAGNOSTICS
    // =============================================

    public void LogMovementDiagnostics(string playerId)
    {
        if (_playerTrackers.TryGetValue(playerId, out var tracker))
        {
            _logger.LogInformation("Movement Diagnostics for {PlayerId}:", playerId);
            _logger.LogInformation("  Recent Positions: {Count}", tracker.RecentPositions.Count);
            _logger.LogInformation("  Average Speed: {Speed:F2}", tracker.CalculateAverageSpeed());
            _logger.LogInformation("  Max Recorded Speed: {MaxSpeed:F2}", tracker.MaxRecordedSpeed);
            _logger.LogInformation("  Suspicious Movements: {Count}", tracker.SuspiciousMovements);
            _logger.LogInformation("  Being Monitored: {Monitored}", tracker.IsBeingMonitored);

            if (tracker.RecentPositions.Count >= 2)
            {
                var lastPosition = tracker.RecentPositions[^1];
                var secondLastPosition = tracker.RecentPositions[^2];
                var distance = Vector2.Distance(lastPosition, secondLastPosition);
                _logger.LogInformation("  Last Movement Distance: {Distance:F2}", distance);
            }
        }
        else
        {
            _logger.LogWarning("No movement tracker found for player {PlayerId}", playerId);
        }
    }

    public Dictionary<string, object> GetSpatialGridInfo(string worldId)
    {
        if (_worldSpatialGrids.TryGetValue(worldId, out var grid))
        {
            return new Dictionary<string, object>
            {
                ["CellSize"] = grid.CellSize,
                ["PlayerCells"] = grid.PlayerCells.Count,
                ["MobCells"] = grid.MobCells.Count,
                ["TotalPlayerEntries"] = grid.PlayerCells.Values.Sum(list => list.Count),
                ["TotalMobEntries"] = grid.MobCells.Values.Sum(list => list.Count),
                ["AveragePlayersPerCell"] = grid.PlayerCells.Count > 0 ?
                    (double)grid.PlayerCells.Values.Sum(list => list.Count) / grid.PlayerCells.Count : 0,
                ["AverageMobsPerCell"] = grid.MobCells.Count > 0 ?
                    (double)grid.MobCells.Values.Sum(list => list.Count) / grid.MobCells.Count : 0
            };
        }

        return new Dictionary<string, object>();
    }

    // =============================================
    // DISPOSAL
    // =============================================

    public void Dispose()
    {
        _worldSpatialGrids.Clear();
        _worldRoomBounds.Clear();
        _worldCorridorBounds.Clear();
        _playerTrackers.Clear();
        _worldStats.Clear();

        _logger.LogInformation("MovementSystem disposed");
    }
}