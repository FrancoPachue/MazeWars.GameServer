using MazeWars.GameServer.Models;

public class ItemUseResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public bool ItemConsumed { get; set; }
    public List<StatusEffect> AppliedEffects { get; set; } = new();
    public int HealthRestored { get; set; }
    public int ManaRestored { get; set; }
}
