using System.Collections.Concurrent;

namespace MazeWars.GameServer.Models;

public class WorldLobby
{
    public string LobbyId { get; set; } = string.Empty;
    public LobbyStatus Status { get; set; } = LobbyStatus.WaitingForPlayers;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastPlayerJoined { get; set; } = DateTime.UtcNow;
    public int MinPlayersToStart { get; set; } = 2;
    public int MaxPlayers { get; set; } = 8;
    public int TotalPlayers { get; set; } = 0;
    public Dictionary<string, int> TeamPlayerCounts { get; set; } = new();
    public ConcurrentDictionary<string, RealTimePlayer> Players { get; set; } = new();
}
