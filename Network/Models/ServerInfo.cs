using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class ServerInfo
{
    public int TickRate { get; set; }
    public Vector2 WorldBounds { get; set; }
    public Dictionary<string, object> GameConfig { get; set; } = new();
}
