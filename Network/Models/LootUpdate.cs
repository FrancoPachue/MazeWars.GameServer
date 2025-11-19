using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject]
public class LootUpdate
{
    [Key(0)]
    public string UpdateType { get; set; } = string.Empty;

    [Key(1)]
    public string LootId { get; set; } = string.Empty;

    [Key(2)]
    public string ItemName { get; set; } = string.Empty;

    [Key(3)]
    public Vector2 Position { get; set; }

    [Key(4)]
    public string TakenBy { get; set; } = string.Empty;
}
