namespace MazeWars.GameServer.Network.Models;

public class TradeRequestMessage
{
    public string TargetPlayerId { get; set; } = string.Empty;
    public List<string> OfferedItemIds { get; set; } = new();
    public List<string> RequestedItemIds { get; set; } = new();
}
