using MazeWars.GameServer.Engine.Movement.Interface;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Movement;

public static class MovementSystemExtensions
{
    public static bool ValidatePlayerMovement(this IMovementSystem movementSystem, RealTimePlayer player, PlayerInputMessage input)
    {
        return movementSystem.IsValidMovementInput(input, player);
    }

    public static Vector2 GetSafeSpawnPosition(this IMovementSystem movementSystem, GameWorld world, string teamId)
    {
        // Método helper para encontrar una posición de spawn segura
        var basePosition = GetTeamSpawnPositionHelper(teamId, world);
        return movementSystem.FindNearestValidPosition(basePosition, world, "");
    }

    private static Vector2 GetTeamSpawnPositionHelper(string teamId, GameWorld world)
    {
        // Lógica similar a GetTeamSpawnPosition pero accesible desde extensión
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