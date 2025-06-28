using MazeWars.GameServer.Engine.Items.Models;
using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Items.Interfaces;

public interface IWorldInteractionSystem
{
    Task<UseItemResult> UseKey(RealTimePlayer player, LootItem key, GameWorld world);
    Task<UseItemResult> UseLockpick(RealTimePlayer player, LootItem lockpick, GameWorld world);
    Task<UseItemResult> UseRope(RealTimePlayer player, LootItem rope, GameWorld world);
    bool CanInteractWith(RealTimePlayer player, string objectId, GameWorld world);
}
