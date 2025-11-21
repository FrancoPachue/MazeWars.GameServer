using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Broadcast to all players when the game starts from lobby.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class GameStartedData
{
    [Key(0)]
    public string WorldId { get; set; } = string.Empty;

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public DateTime Timestamp { get; set; }

    [Key(3)]
    public string GameMode { get; set; } = string.Empty;

    [Key(4)]
    public List<string> Instructions { get; set; } = new();
}
