namespace MazeWars.GameServer.Network.Models;

public class WorldStateMessage
{
    public List<RoomStateUpdate> Rooms { get; set; } = new();
    public List<ExtractionPointUpdate> ExtractionPoints { get; set; } = new();
    public WorldInfo WorldInfo { get; set; } = new();
}
