namespace MazeWars.GameServer.Network.Models;

public class TeamInfo
{
    public string TeamId { get; set; } = string.Empty;
    public List<string> TeamMembers { get; set; } = new();
    public int TeamScore { get; set; } = 0;
    public string TeamColor { get; set; } = "#FFFFFF";
}
