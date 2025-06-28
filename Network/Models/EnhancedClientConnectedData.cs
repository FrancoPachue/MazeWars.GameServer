namespace MazeWars.GameServer.Network.Models;

public class EnhancedClientConnectedData : ClientConnectedData
{
    public PlayerStats PlayerStats { get; set; } = new();
    public List<string> AvailableCommands { get; set; } = new();
    public TeamInfo TeamInfo { get; set; } = new();
}
