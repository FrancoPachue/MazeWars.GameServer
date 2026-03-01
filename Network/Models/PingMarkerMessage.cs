using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Ping marker from client to server (team communication).
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PingMarkerMessage
{
    [Key(0)]
    public float X { get; set; }

    [Key(1)]
    public float Y { get; set; }
}

/// <summary>
/// Ping marker broadcast from server to team members.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PingMarkerData
{
    [Key(0)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(1)]
    public float X { get; set; }

    [Key(2)]
    public float Y { get; set; }

    [Key(3)]
    public DateTime Timestamp { get; set; }
}
