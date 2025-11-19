namespace MazeWars.GameServer.Models;

public class StatusEffect
{
    public string EffectType { get; set; } = string.Empty;
    public int Value { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public string SourcePlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Duration in seconds (calculated from AppliedAt and ExpiresAt)
    /// </summary>
    public double Duration => (ExpiresAt - AppliedAt).TotalSeconds;
}
