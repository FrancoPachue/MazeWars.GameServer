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

    // ⭐ NEW: Rate limiting tracking
    public int InputsThisSecond { get; set; }
    public DateTime LastInputSecondStart { get; set; } = DateTime.UtcNow;
    public const int MAX_INPUTS_PER_SECOND = 35; // Allow some tolerance over 30 expected

    // ⭐ NEW: Suspicion decay tracking
    public float SuspicionLevel { get; set; }
    public int ConsecutiveValidMovements { get; set; }
    public const float SUSPICION_DECAY_RATE = 0.1f; // Decay per valid movement
    public const int VALID_MOVEMENTS_TO_CLEAR_MONITORING = 100;

    public void AddPosition(Vector2 position)
    {
        RecentPositions.Add(position);
        PositionTimestamps.Add(DateTime.UtcNow);
        LastValidation = DateTime.UtcNow;

        // Keep only last 10 positions for analysis
        while (RecentPositions.Count > 10)
        {
            RecentPositions.RemoveAt(0);
            PositionTimestamps.RemoveAt(0);
        }
    }

    /// <summary>
    /// Check and update rate limiting. Returns true if rate limit exceeded.
    /// </summary>
    public bool CheckRateLimit()
    {
        var now = DateTime.UtcNow;
        var secondsDiff = (now - LastInputSecondStart).TotalSeconds;

        if (secondsDiff >= 1.0)
        {
            // Reset counter for new second
            InputsThisSecond = 1;
            LastInputSecondStart = now;
            return false;
        }

        InputsThisSecond++;
        return InputsThisSecond > MAX_INPUTS_PER_SECOND;
    }

    /// <summary>
    /// Record a valid movement and apply suspicion decay
    /// </summary>
    public void RecordValidMovement()
    {
        ConsecutiveValidMovements++;

        // Apply suspicion decay
        if (SuspicionLevel > 0)
        {
            SuspicionLevel = Math.Max(0, SuspicionLevel - SUSPICION_DECAY_RATE);
        }

        // Clear monitoring after enough valid movements
        if (IsBeingMonitored && ConsecutiveValidMovements >= VALID_MOVEMENTS_TO_CLEAR_MONITORING)
        {
            IsBeingMonitored = false;
            SuspiciousMovements = Math.Max(0, SuspiciousMovements - 1);
        }
    }

    /// <summary>
    /// Record a suspicious movement
    /// </summary>
    public void RecordSuspiciousMovement(float addedSuspicion)
    {
        SuspiciousMovements++;
        ConsecutiveValidMovements = 0;
        SuspicionLevel = Math.Min(1.0f, SuspicionLevel + addedSuspicion);

        if (SuspicionLevel > 0.8f)
        {
            IsBeingMonitored = true;
        }
    }

    public float CalculateAverageSpeed()
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

        return totalTime > 0 ? totalDistance / totalTime : 0f;
    }
}
