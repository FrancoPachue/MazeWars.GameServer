using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Combat.Interface;
public interface ICombatSystem
{
    event Action<string, CombatEvent>? OnCombatEvent;
    event Action<RealTimePlayer, RealTimePlayer?>? OnPlayerDeath;
    Task<CombatResult> ProcessAttack(RealTimePlayer attacker, List<RealTimePlayer> potentialTargets, GameWorld world);
    Task<AbilityResult> ProcessAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world);
    bool CanAttack(RealTimePlayer player);
    bool CanUseAbility(RealTimePlayer player, string abilityType);
    void ApplyStatusEffect(RealTimePlayer player, StatusEffect effect);
    void UpdateStatusEffects(List<RealTimePlayer> players, float deltaTime);
}