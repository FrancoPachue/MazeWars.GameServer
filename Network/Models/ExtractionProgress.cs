namespace MazeWars.GameServer.Network.Models;

public class ExtractionProgress
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public float Progress { get; set; }
    public int SecondsRemaining { get; set; }
}
