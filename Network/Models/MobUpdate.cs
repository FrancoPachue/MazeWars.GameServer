using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class MobUpdate
{
    public string MobId { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public string State { get; set; } = string.Empty;
    public int Health { get; set; }
}
