namespace MazeWars.GameServer.Network.Models;

public class MessageAcknowledgement
{
    public string MessageId { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
    public string ErrorMessage { get; set; } = string.Empty;
}
