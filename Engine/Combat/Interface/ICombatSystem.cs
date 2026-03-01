using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Services.Combat;

namespace MazeWars.GameServer.Engine.Combat.Interface;
public interface ICombatSystem
{
    event Action<string, CombatEvent>? OnCombatEvent;
    event Action<RealTimePlayer, RealTimePlayer?>? OnPlayerDeath;
    void ProcessPlayerDeath(RealTimePlayer deadPlayer, RealTimePlayer? killer);
    ProjectileSystem ProjectileSystem { get; }
    Task<CombatResult> ProcessAttack(RealTimePlayer attacker, List<RealTimePlayer> potentialTargets, GameWorld world);
    CombatResult ProcessAttackAgainstMobs(RealTimePlayer attacker, List<Mob> potentialMobs, GameWorld world);
    Task<AbilityResult> ProcessAbility(RealTimePlayer player, string abilityType, Vector2 target, GameWorld world);
    bool CanAttack(RealTimePlayer player);
    bool CanUseAbility(RealTimePlayer player, string abilityType);
    int GetEffectiveAttackSpeedMs(RealTimePlayer player);
    float GetEffectiveAttackRange(RealTimePlayer player);
    void ApplyStatusEffect(RealTimePlayer player, StatusEffect effect);
    void UpdateStatusEffects(IEnumerable<RealTimePlayer> players, float deltaTime);
    void UpdateChanneling(GameWorld world, float deltaTime);
    Func<string, float>? RttLookup { get; set; }
}