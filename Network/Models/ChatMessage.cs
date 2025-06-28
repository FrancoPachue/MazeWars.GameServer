namespace MazeWars.GameServer.Network.Models;

public class ChatMessage
{
    public string Message { get; set; } = string.Empty;
    public string ChatType { get; set; } = "team";
}
