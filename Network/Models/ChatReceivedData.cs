namespace MazeWars.GameServer.Network.Models;

public class ChatReceivedData
{
    public string PlayerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ChatType { get; set; } = "all";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
