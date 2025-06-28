using MazeWars.GameServer.Engine.Loot.Interface;
using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Loot;

public static class LootSystemExtensions
{
    public static bool PlayerCanGrabLoot(this ILootSystem lootSystem, RealTimePlayer player, string lootId, GameWorld world)
    {
        if (!world.AvailableLoot.TryGetValue(lootId, out var loot))
            return false;

        return lootSystem.CanPlayerGrabLoot(player, loot, world);
    }

    public static List<LootItem> GetLootInRange(this ILootSystem lootSystem, GameWorld world, Vector2 position, float range)
    {
        return world.AvailableLoot.Values
            .Where(loot => Vector2.Distance(loot.Position, position) <= range)
            .ToList();
    }

    public static Dictionary<string, int> GetPlayerInventoryStats(this ILootSystem lootSystem, RealTimePlayer player)
    {
        return player.Inventory
            .GroupBy(item => item.ItemType)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public static int GetPlayerInventoryValue(this ILootSystem lootSystem, RealTimePlayer player)
    {
        return player.Inventory.Sum(item => item.Rarity * item.Rarity * 10);
    }

    public static LootItem? GetHighestRarityItem(this ILootSystem lootSystem, RealTimePlayer player)
    {
        return player.Inventory.OrderByDescending(item => item.Rarity).FirstOrDefault();
    }
}
