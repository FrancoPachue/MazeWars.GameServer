using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

public class MobCombatResult
{
    public bool AttackPerformed { get; set; }
    public int DamageDealt { get; set; }
    public bool TargetKilled { get; set; }
    public bool AbilityUsed { get; set; }
    public string? AbilityType { get; set; }
    public List<StatusEffect> AppliedEffects { get; set; } = new();
}
