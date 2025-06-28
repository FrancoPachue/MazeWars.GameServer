namespace MazeWars.GameServer.Engine.MobIASystem.Models;

// =============================================
// AI STATISTICS
// =============================================

public class AIStats
{
    public int TotalMobs { get; set; }
    public int AliveMobs { get; set; }
    public int DeadMobs { get; set; }
    public Dictionary<string, int> MobsByType { get; set; } = new();
    public Dictionary<MobState, int> MobsByState { get; set; } = new();
    public Dictionary<string, int> MobsByRoom { get; set; } = new();
    public float AverageAggressionLevel { get; set; }
    public int MobKills { get; set; }
    public int PlayerKills { get; set; }
    public int AbilitiesUsed { get; set; }
    public int StateChanges { get; set; }
    public float AverageLifetime { get; set; }
    public string MostDangerousRoom { get; set; } = string.Empty;
    public string MostActiveAI { get; set; } = string.Empty;
    public DateTime LastStatsReset { get; set; } = DateTime.UtcNow;
}
