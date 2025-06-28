namespace MazeWars.GameServer.Engine.Movement.Models;

public class RoomTransitionResult
{
    public bool RoomChanged { get; set; }
    public string? OldRoomId { get; set; }
    public string? NewRoomId { get; set; }
    public bool EncounterDetected { get; set; }
    public List<string> TeamsInRoom { get; set; } = new();
    public int PlayersInRoom { get; set; }
}
