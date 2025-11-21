using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Broadcast to all players when a new player joins the world/lobby.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PlayerJoinedData
{
    [Key(0)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(1)]
    public string PlayerClass { get; set; } = string.Empty;

    [Key(2)]
    public string TeamId { get; set; } = string.Empty;

    [Key(3)]
    public bool IsLobby { get; set; }
}
