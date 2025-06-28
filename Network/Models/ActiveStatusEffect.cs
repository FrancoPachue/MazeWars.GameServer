namespace MazeWars.GameServer.Network.Models;

public class ActiveStatusEffect
{
    public string EffectType { get; set; } = string.Empty;
    public int SecondsRemaining { get; set; }
    public string SourcePlayerName { get; set; } = string.Empty;
}
