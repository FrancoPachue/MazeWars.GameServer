using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Lobby information sent on connection (if player connects to lobby).
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ConnectedLobbyInfoData
{
    [Key(0)]
    public string Status { get; set; } = string.Empty;

    [Key(1)]
    public int CurrentPlayers { get; set; }

    [Key(2)]
    public int MaxPlayers { get; set; }

    [Key(3)]
    public int MinPlayersToStart { get; set; }

    [Key(4)]
    public DateTime? EstimatedStartTime { get; set; }
}
