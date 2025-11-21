using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Administrative message broadcast to players.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class AdminMessageData
{
    [Key(0)]
    public string Message { get; set; } = string.Empty;

    [Key(1)]
    public DateTime Timestamp { get; set; }

    [Key(2)]
    public bool IsSystemMessage { get; set; }
}
