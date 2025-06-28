namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public enum AIProcessingPriority
{
    Low,      // Far from players
    Medium,   // Medium distance
    High,     // Close to players
    Critical  // Bosses, combat situations
}
