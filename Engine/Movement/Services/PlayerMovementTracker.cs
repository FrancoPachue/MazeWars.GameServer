using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Movement.Services;

public class PlayerMovementTracker
{
    public string PlayerId { get; set; } = string.Empty;
    public List<Vector2> RecentPositions { get; set; } = new();
    public List<DateTime> PositionTimestamps { get; set; } = new();
    public float MaxRecordedSpeed { get; set; }
    public int SuspiciousMovements { get; set; }
    public DateTime LastValidation { get; set; } = DateTime.UtcNow;
    public bool IsBeingMonitored { get; set; }

    private readonly object _lock = new();

    public void AddPosition(Vector2 position)
    {
        lock (_lock)
        {
            RecentPositions.Add(position);
            PositionTimestamps.Add(DateTime.UtcNow);

            // Keep only last 10 positions for analysis
            while (RecentPositions.Count > 10)
            {
                RecentPositions.RemoveAt(0);
                PositionTimestamps.RemoveAt(0);
            }
        }
    }

    public float CalculateAverageSpeed()
    {
        lock (_lock)
        {
            if (RecentPositions.Count < 2) return 0f;

            float totalDistance = 0f;
            float totalTime = 0f;

            for (int i = 1; i < RecentPositions.Count; i++)
            {
                var distance = Vector2.Distance(RecentPositions[i - 1], RecentPositions[i]);
                var time = (float)(PositionTimestamps[i] - PositionTimestamps[i - 1]).TotalSeconds;

                totalDistance += distance;
                totalTime += time;
            }

            // Need at least 50ms of data to calculate meaningful speed
            // (avoids divide-by-near-zero on first frames after connect)
            return totalTime > 0.05f ? totalDistance / totalTime : 0f;
        }
    }
}
