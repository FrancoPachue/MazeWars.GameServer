namespace MazeWars.GameServer.Engine.Movement.Models;

public class MovementStats
{
    public int TotalMovementUpdates { get; set; }
    public int CollisionsDetected { get; set; }
    public int RoomTransitions { get; set; }
    public int InvalidMovements { get; set; }
    public int TeleportsPerformed { get; set; }
    public float AveragePlayerSpeed { get; set; }
    public Dictionary<string, int> RoomPopulation { get; set; } = new();
    public DateTime LastStatsReset { get; set; } = DateTime.UtcNow;
}
