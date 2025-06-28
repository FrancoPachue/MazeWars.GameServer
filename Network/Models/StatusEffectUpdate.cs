namespace MazeWars.GameServer.Network.Models;

public class StatusEffectUpdate
{
    public string PlayerId { get; set; } = string.Empty;
    public List<ActiveStatusEffect> StatusEffects { get; set; } = new();
}
