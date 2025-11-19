using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Loot grab request from client to server.
/// </summary>
[MessagePackObject]
public class LootGrabMessage
{
    [Key(0)]
    public string LootId { get; set; } = string.Empty;
}
