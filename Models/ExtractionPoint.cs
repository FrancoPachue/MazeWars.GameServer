namespace MazeWars.GameServer.Models;

public class ExtractionPoint
{
    public string ExtractionId { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int ExtractionTimeSeconds { get; set; } = 30;
    public List<string> PlayersExtracting { get; set; } = new();
    public Dictionary<string, DateTime> ExtractionStartTimes { get; set; } = new();
}
