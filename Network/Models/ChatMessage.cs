using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Chat message from client to server.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ChatMessage
{
    [Key(0)]
    public string Message { get; set; } = string.Empty;

    [Key(1)]
    public string ChatType { get; set; } = "team";
}
