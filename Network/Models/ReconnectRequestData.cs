using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Client request to reconnect using session token.
/// </summary>
[MessagePackObject]
public class ReconnectRequestData
{
    [Key(0)]
    public string SessionToken { get; set; } = string.Empty;

    [Key(1)]
    public string PlayerName { get; set; } = string.Empty; // For verification

    [Key(2)]
    public float ClientTimestamp { get; set; } // Client's current time
}
