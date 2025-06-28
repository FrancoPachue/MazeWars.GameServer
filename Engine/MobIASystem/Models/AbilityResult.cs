using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public class MobAbilityResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AbilityName { get; set; }
    public List<RealTimePlayer> AffectedPlayers { get; set; } = new();
    public int Damage { get; set; }
    public List<StatusEffect> StatusEffects { get; set; } = new();
}
