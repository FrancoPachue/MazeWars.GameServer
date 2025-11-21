using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Chat message broadcast from server to clients.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ChatReceivedData
{
    [Key(0)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public string ChatType { get; set; } = "all";

    [Key(3)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
