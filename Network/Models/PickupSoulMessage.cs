using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Request from client to pick up a dead teammate's soul.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PickupSoulMessage
{
    [Key(0)]
    public string TargetPlayerId { get; set; } = string.Empty;
}
