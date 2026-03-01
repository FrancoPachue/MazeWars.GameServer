using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Request from client to start channeling a Revival Altar to revive a carried soul.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class AltarReviveMessage
{
    [Key(0)]
    public string AltarId { get; set; } = string.Empty;
}
