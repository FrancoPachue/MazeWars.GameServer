using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Broadcast when a player disconnects from the world.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PlayerDisconnectedData
{
    [Key(0)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(1)]
    public string Reason { get; set; } = string.Empty;

    [Key(2)]
    public DateTime Timestamp { get; set; }

    [Key(3)]
    public bool CanReconnect { get; set; }
}
