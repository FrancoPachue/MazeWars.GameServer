using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Combat.Models;
public class CombatResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<CombatEvent> Events { get; set; } = new();
    public int TargetsHit { get; set; }
    /// <summary>True when the attack spawned a projectile (handles mob targeting automatically).</summary>
    public bool IsRangedProjectile { get; set; }
}