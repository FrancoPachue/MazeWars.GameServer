using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Combat.Models;
public class AbilityResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public List<CombatEvent> Events { get; set; } = new();
}