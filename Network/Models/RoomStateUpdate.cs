namespace MazeWars.GameServer.Network.Models;

public class RoomStateUpdate
{
    public string RoomId { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public string CompletedByTeam { get; set; } = string.Empty;
    public int MobCount { get; set; }
    public int LootCount { get; set; }
}
