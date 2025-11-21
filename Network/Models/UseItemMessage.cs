using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Use item request from client to server.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class UseItemMessage
{
    [Key(0)]
    public string ItemId { get; set; } = string.Empty;

    [Key(1)]
    public string ItemType { get; set; } = string.Empty;

    [Key(2)]
    public Vector2 TargetPosition { get; set; }
}
