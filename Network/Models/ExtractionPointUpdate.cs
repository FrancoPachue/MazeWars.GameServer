using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

public class ExtractionPointUpdate
{
    public string ExtractionId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Vector2 Position { get; set; }
    public List<ExtractionProgress> PlayersExtracting { get; set; } = new();
}
