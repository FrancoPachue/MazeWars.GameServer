using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class LootUpdate
{
    public string UpdateType { get; set; } = string.Empty;
    public string LootId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public string TakenBy { get; set; } = string.Empty;
}
