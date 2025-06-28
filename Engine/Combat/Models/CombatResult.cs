using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Combat.Models;
public class CombatResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<CombatEvent> Events { get; set; } = new();
    public int TargetsHit { get; set; }
}