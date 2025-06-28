namespace MazeWars.GameServer.Network.Models;

// Reliable messaging
public class ReliableMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
    public object Data { get; set; } = null!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; } = 0;
    public bool RequiresAck { get; set; } = true;
}
