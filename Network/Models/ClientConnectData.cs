using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Connection messages from client to server.
/// </summary>
[MessagePackObject]
public class ClientConnectData
{
    [Key(0)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(1)]
    public string PlayerClass { get; set; } = "scout";

    [Key(2)]
    public string TeamId { get; set; } = string.Empty;

    [Key(3)]
    public string AuthToken { get; set; } = string.Empty;
}
