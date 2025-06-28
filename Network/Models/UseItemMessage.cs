using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class UseItemMessage
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public Vector2 TargetPosition { get; set; }
}
