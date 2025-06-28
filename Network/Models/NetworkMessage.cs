namespace MazeWars.GameServer.Network.Models;

public class NetworkMessage
{
    public string Type { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public object Data { get; set; } = null!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
