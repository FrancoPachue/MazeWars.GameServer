namespace MazeWars.GameServer.Network.Models;

// Connection messages
public class ClientConnectData
{
    public string PlayerName { get; set; } = string.Empty;
    public string PlayerClass { get; set; } = "scout";
    public string TeamId { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}
