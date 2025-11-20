using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Trade request message from client to server.
/// </summary>
[MessagePackObject]
public class TradeRequestMessage
{
    [Key(0)]
    public string TargetPlayerId { get; set; } = string.Empty;

    [Key(1)]
    public List<string> OfferedItemIds { get; set; } = new();

    [Key(2)]
    public List<string> RequestedItemIds { get; set; } = new();
}
