using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Message acknowledgement from client to server.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class MessageAcknowledgement
{
    [Key(0)]
    public string MessageId { get; set; } = string.Empty;

    [Key(1)]
    public bool Success { get; set; } = true;

    [Key(2)]
    public string ErrorMessage { get; set; } = string.Empty;
}
