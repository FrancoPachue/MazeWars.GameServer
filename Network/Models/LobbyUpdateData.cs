using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Periodic lobby status updates sent to players waiting in lobby.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class LobbyUpdateData
{
    [Key(0)]
    public string WorldId { get; set; } = string.Empty;

    [Key(1)]
    public int CurrentPlayers { get; set; }

    [Key(2)]
    public int MaxPlayers { get; set; }

    [Key(3)]
    public int MinPlayersToStart { get; set; }

    [Key(4)]
    public Dictionary<string, int> TeamCounts { get; set; } = new();

    [Key(5)]
    public DateTime? EstimatedStartTime { get; set; }

    [Key(6)]
    public string Status { get; set; } = "waiting";
}
