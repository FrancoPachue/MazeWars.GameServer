namespace MazeWars.GameServer.Network.Models;

public class WorldInfo
{
    public string WorldId { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public string WinningTeam { get; set; } = string.Empty;
    public int TotalRooms { get; set; }
    public int CompletedRooms { get; set; }
    public int TotalLoot { get; set; }
    public TimeSpan WorldAge { get; set; }
}
