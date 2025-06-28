using MazeWars.GameServer.Engine.Items.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Items.Interfaces;

public interface IItemSystem
{
    Task<UseItemResult> UseItem(RealTimePlayer player, string itemId, GameWorld world);
    bool CanUseItem(RealTimePlayer player, LootItem item);
    ItemInfo GetItemInfo(string itemId);
}
