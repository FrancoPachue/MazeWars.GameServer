using MazeWars.GameServer.Engine.Movement.Models;
using MazeWars.GameServer.Engine.Movement.Services;
using MazeWars.GameServer.Engine.Movement.Settings;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Movement.Interface;

// =============================================
// COMPLETE MOVEMENT SYSTEM INTERFACE
// =============================================

public interface IMovementSystem : IDisposable
{
    // =============================================
    // CORE MOVEMENT METHODS
    // =============================================

    /// <summary>
    /// Validates if a position is valid within the world bounds
    /// </summary>
    bool IsValidPosition(GameWorld world, Vector2 position);

    /// <summary>
    /// Validates if a move input is legitimate (not cheating)
    /// </summary>
    bool IsValidMovementInput(PlayerInputMessage input, RealTimePlayer player);

    /// <summary>
    /// Updates player movement for a single frame
    /// </summary>
    MovementResult UpdatePlayerMovement(RealTimePlayer player, PlayerInputMessage input, GameWorld world, float deltaTime);

    /// <summary>
    /// Updates all players' movement in a world
    /// </summary>
    void UpdateAllPlayersMovement(GameWorld world, float deltaTime);

    /// <summary>
    /// Calculates the effective movement speed for a player
    /// </summary>
    float CalculateMovementSpeed(RealTimePlayer player, bool isSprinting);

    /// <summary>
    /// Clamps a position to world boundaries
    /// </summary>
    Vector2 ClampToWorldBounds(Vector2 position, GameWorld world);

    // =============================================
    // COLLISION SYSTEM
    // =============================================

    /// <summary>
    /// Checks and resolves collisions between players and world objects
    /// </summary>
    void ProcessCollisions(GameWorld world);

    /// <summary>
    /// Checks if a position would result in a collision
    /// </summary>
    bool WillCollide(RealTimePlayer player, Vector2 targetPosition, GameWorld world);

    /// <summary>
    /// Finds the nearest valid position to a desired position
    /// </summary>
    Vector2 FindNearestValidPosition(Vector2 desiredPosition, GameWorld world, string excludePlayerId);

    // =============================================
    // ROOM SYSTEM
    // =============================================

    /// <summary>
    /// Handles room transitions when players move between rooms
    /// </summary>
    RoomTransitionResult CheckRoomTransition(RealTimePlayer player, GameWorld world);

    /// <summary>
    /// Gets the room ID for a specific position
    /// </summary>
    string GetRoomIdByPosition(GameWorld world, Vector2 position);

    /// <summary>
    /// Gets list of connected rooms for a specific room
    /// </summary>
    List<string> GetConnectedRooms(string roomId, GameWorld world);

    /// <summary>
    /// Checks if two rooms are connected
    /// </summary>
    bool AreRoomsConnected(string roomId1, string roomId2, GameWorld world);

    /// <summary>
    /// Gets distance from position to a specific room
    /// </summary>
    float GetDistanceToRoom(Vector2 position, string targetRoomId, GameWorld world);

    // =============================================
    // TELEPORTATION SYSTEM
    // =============================================

    /// <summary>
    /// Teleports a player to a specific position (for abilities like dash)
    /// </summary>
    TeleportResult TeleportPlayer(RealTimePlayer player, Vector2 targetPosition, GameWorld world);

    // =============================================
    // PREDICTION AND INTERPOLATION
    // =============================================

    /// <summary>
    /// Predicts player position based on current velocity
    /// </summary>
    Vector2 PredictPlayerPosition(RealTimePlayer player, float timeAhead);

    /// <summary>
    /// Interpolates between two positions
    /// </summary>
    Vector2 InterpolatePosition(Vector2 fromPosition, Vector2 toPosition, float interpolationFactor);

    // =============================================
    // ANTI-CHEAT AND MONITORING
    // =============================================

    /// <summary>
    /// Flags a player for movement monitoring
    /// </summary>
    void FlagPlayerForMonitoring(string playerId, string reason);

    /// <summary>
    /// Resets player suspicion level
    /// </summary>
    void ResetPlayerSuspicion(string playerId);

    /// <summary>
    /// Checks if a player is flagged as suspicious
    /// </summary>
    bool IsPlayerSuspicious(string playerId);

    /// <summary>
    /// Gets list of players flagged as suspicious
    /// </summary>
    List<PlayerMovementTracker> GetSuspiciousPlayers();

    // =============================================
    // STATISTICS AND DIAGNOSTICS
    // =============================================

    /// <summary>
    /// Gets movement statistics for a specific world
    /// </summary>
    MovementStats GetMovementStats(string worldId);

    /// <summary>
    /// Gets detailed movement statistics for all worlds
    /// </summary>
    Dictionary<string, object> GetDetailedMovementStats();

    /// <summary>
    /// Gets spatial grid information for diagnostics
    /// </summary>
    Dictionary<string, object> GetSpatialGridInfo(string worldId);

    /// <summary>
    /// Logs movement diagnostics for a specific player
    /// </summary>
    void LogMovementDiagnostics(string playerId);

    // =============================================
    // CLEANUP AND OPTIMIZATION
    // =============================================

    /// <summary>
    /// Cleans up movement data for a world that no longer exists
    /// </summary>
    void CleanupWorldData(string worldId);

    /// <summary>
    /// Cleans up movement tracker for a specific player
    /// </summary>
    void CleanupPlayerTracker(string playerId);

    /// <summary>
    /// Optimizes memory usage by cleaning up old data
    /// </summary>
    void OptimizeMemoryUsage();

    // =============================================
    // CONFIGURATION
    // =============================================

    /// <summary>
    /// Updates movement settings at runtime
    /// </summary>
    void UpdateMovementSettings(MovementSettings newSettings);

    /// <summary>
    /// Gets current movement settings
    /// </summary>
    MovementSettings GetCurrentMovementSettings();

    // =============================================
    // EVENTS
    // =============================================

    /// <summary>
    /// Event fired when a player changes rooms
    /// </summary>
    event Action<RealTimePlayer, string, string>? OnRoomChanged; // player, oldRoom, newRoom

    /// <summary>
    /// Event fired when players enter the same room (potential PvP encounter)
    /// </summary>
    event Action<GameWorld, string, List<RealTimePlayer>>? OnPlayersInRoom; // world, roomId, players
}

// =============================================
// EXTENSION METHODS FOR CONVENIENCE
// =============================================

public static class MovementSystemExtensions
{
    /// <summary>
    /// Validates player movement input
    /// </summary>
    public static bool ValidatePlayerMovement(this IMovementSystem movementSystem, RealTimePlayer player, PlayerInputMessage input)
    {
        return movementSystem.IsValidMovementInput(input, player);
    }

    /// <summary>
    /// Gets a safe spawn position for a team
    /// </summary>
    public static Vector2 GetSafeSpawnPosition(this IMovementSystem movementSystem, GameWorld world, string teamId)
    {
        var basePosition = GetTeamSpawnPositionHelper(teamId, world);
        return movementSystem.FindNearestValidPosition(basePosition, world, "");
    }

    /// <summary>
    /// Checks if a player can move to a specific position
    /// </summary>
    public static bool CanMoveTo(this IMovementSystem movementSystem, RealTimePlayer player, Vector2 targetPosition, GameWorld world)
    {
        return movementSystem.IsValidPosition(world, targetPosition) &&
               !movementSystem.WillCollide(player, targetPosition, world);
    }

    /// <summary>
    /// Gets movement speed including all modifiers
    /// </summary>
    public static float GetEffectiveSpeed(this IMovementSystem movementSystem, RealTimePlayer player)
    {
        return movementSystem.CalculateMovementSpeed(player, player.IsSprinting);
    }

    /// <summary>
    /// Checks if two players are in the same room
    /// </summary>
    public static bool ArePlayersInSameRoom(this IMovementSystem movementSystem, RealTimePlayer player1, RealTimePlayer player2)
    {
        return player1.CurrentRoomId == player2.CurrentRoomId;
    }

    /// <summary>
    /// Gets distance between two players
    /// </summary>
    public static float GetDistanceBetweenPlayers(this IMovementSystem movementSystem, RealTimePlayer player1, RealTimePlayer player2)
    {
        return Vector2.Distance(player1.Position, player2.Position);
    }

    /// <summary>
    /// Checks if a player is within range of another player
    /// </summary>
    public static bool IsPlayerInRange(this IMovementSystem movementSystem, RealTimePlayer player1, RealTimePlayer player2, float range)
    {
        return movementSystem.GetDistanceBetweenPlayers(player1, player2) <= range;
    }

    /// <summary>
    /// Attempts to move a player towards a target position
    /// </summary>
    public static Vector2 MoveTowards(this IMovementSystem movementSystem, RealTimePlayer player, Vector2 targetPosition, float speed, float deltaTime, GameWorld world)
    {
        var direction = (targetPosition - player.Position).GetNormalized();
        var desiredPosition = player.Position + direction * speed * deltaTime;

        if (movementSystem.CanMoveTo(player, desiredPosition, world))
        {
            return desiredPosition;
        }

        return player.Position;
    }

    /// <summary>
    /// Gets all players within a specific radius
    /// </summary>
    public static List<RealTimePlayer> GetPlayersInRadius(this IMovementSystem movementSystem, GameWorld world, Vector2 center, float radius, string? excludePlayerId = null)
    {
        return world.Players.Values
            .Where(p => p.IsAlive)
            .Where(p => p.PlayerId != excludePlayerId)
            .Where(p => Vector2.Distance(p.Position, center) <= radius)
            .ToList();
    }

    /// <summary>
    /// Gets the center position of a room
    /// </summary>
    public static Vector2? GetRoomCenter(this IMovementSystem movementSystem, GameWorld world, string roomId)
    {
        if (world.Rooms.TryGetValue(roomId, out var room))
        {
            return room.Position;
        }
        return null;
    }

    /// <summary>
    /// Helper method for team spawn positions
    /// </summary>
    private static Vector2 GetTeamSpawnPositionHelper(string teamId, GameWorld world)
    {
        // Valores por defecto basados en un mundo de 240x240
        return teamId.ToLower() switch
        {
            "team1" => new Vector2(30, 30),
            "team2" => new Vector2(210, 30),
            "team3" => new Vector2(30, 210),
            "team4" => new Vector2(210, 210),
            _ => new Vector2(120, 120)
        };
    }
}