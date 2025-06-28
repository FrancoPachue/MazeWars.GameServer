namespace MazeWars.GameServer.Models;

public class Room
{
    public string RoomId { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; }
    public List<string> Connections { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string CompletedByTeam { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public List<string> SpawnedLootIds { get; set; } = new();
}
