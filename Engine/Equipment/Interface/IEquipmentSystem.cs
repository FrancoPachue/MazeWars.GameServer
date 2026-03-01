using MazeWars.GameServer.Engine.Equipment.Models;
using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Equipment.Interface;

public interface IEquipmentSystem
{
    EquipResult EquipItem(RealTimePlayer player, string itemId);
    EquipResult UnequipItem(RealTimePlayer player, EquipmentSlot slot);
    List<AbilityDefinition> GetAvailableAbilities(RealTimePlayer player);
    AbilityDefinition? ResolveAbility(RealTimePlayer player, string abilityId);
    (AbilityDefinition ability, ClassAbilityModifier? modifier)? ResolveAbilityWithModifiers(RealTimePlayer player, string abilityId);
    void RecalculateEquipmentStats(RealTimePlayer player);
    void EquipStartingGear(RealTimePlayer player);
    Dictionary<EquipmentSlot, string> GetEquippedItemIds(RealTimePlayer player);
}
