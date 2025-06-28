namespace MazeWars.GameServer.Models;

public class StatusEffect
{
    public string EffectType { get; set; } = string.Empty;
    public int Value { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string SourcePlayerId { get; set; } = string.Empty;
}
