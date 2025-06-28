using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class ClientConnectedData
{
    public string PlayerId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public Vector2 SpawnPosition { get; set; }
    public ServerInfo ServerInfo { get; set; } = new();
}
