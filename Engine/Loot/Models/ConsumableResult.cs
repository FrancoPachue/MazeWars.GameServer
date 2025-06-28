using MazeWars.GameServer.Models;

public class ConsumableResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int HealthRestored { get; set; }
    public int ManaRestored { get; set; }
    public List<StatusEffect> StatusEffects { get; set; } = new();
    public float DurationSeconds { get; set; }
}
