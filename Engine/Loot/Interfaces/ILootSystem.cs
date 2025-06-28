using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Loot.Interface;

// =============================================
// LOOT SYSTEM INTERFACE
// =============================================

public interface ILootSystem : IDisposable
{
    // =============================================
    // CORE LOOT MANAGEMENT
    // =============================================

    /// <summary>
    /// Initializes loot tables for the system
    /// </summary>
    void InitializeLootTables(Dictionary<string, LootTable> baseLootTables);

    /// <summary>
    /// Spawns loot from a specific loot table
    /// </summary>
    List<LootItem> SpawnLootFromTable(GameWorld world, Room room, LootTable lootTable, Vector2? specificPosition = null);

    /// <summary>
    /// Creates a loot item from a loot drop definition
    /// </summary>
    LootItem CreateLootItem(LootDrop drop, Room room, Vector2? overridePosition = null);

    /// <summary>
    /// Spawns random loot in available rooms
    /// </summary>
    void SpawnRandomLoot(GameWorld world);

    /// <summary>
    /// Processes loot spawning for a world (called each frame)
    /// </summary>
    void ProcessLootSpawning(GameWorld world, float deltaTime);

    // =============================================
    // PLAYER LOOT INTERACTIONS
    // =============================================

    /// <summary>
    /// Attempts to grab loot for a player
    /// </summary>
    LootGrabResult ProcessLootGrab(RealTimePlayer player, string lootId, GameWorld world);

    /// <summary>
    /// Drops loot when a player dies
    /// </summary>
    List<LootItem> DropPlayerLoot(RealTimePlayer deadPlayer, GameWorld world, int maxItemsToDrop = 3);

    /// <summary>
    /// Uses an item from player's inventory
    /// </summary>
    ItemUseResult UseItem(RealTimePlayer player, LootItem item, GameWorld world);

    /// <summary>
    /// Validates if a player can grab specific loot
    /// </summary>
    bool CanPlayerGrabLoot(RealTimePlayer player, LootItem loot, GameWorld world);

    // =============================================
    // LOOT DISTRIBUTION AND RARITY
    // =============================================

    /// <summary>
    /// Calculates loot rarity based on world conditions
    /// </summary>
    int CalculateDynamicRarity(GameWorld world, Room room, string triggerCondition);

    /// <summary>
    /// Gets loot drop chance modifiers based on player stats
    /// </summary>
    float GetLuckModifier(RealTimePlayer player);

    /// <summary>
    /// Determines if a drop should occur based on chance and modifiers
    /// </summary>
    bool ShouldDropOccur(LootDrop drop, RealTimePlayer? player = null, float baseMultiplier = 1.0f);

    // =============================================
    // DYNAMIC LOOT SPAWNING
    // =============================================

    /// <summary>
    /// Handles loot spawning when mobs die
    /// </summary>
    void ProcessMobDeathLoot(Mob deadMob, GameWorld world, RealTimePlayer? killer = null);

    /// <summary>
    /// Handles loot spawning when rooms are completed
    /// </summary>
    void ProcessRoomCompletionLoot(Room completedRoom, GameWorld world, string completingTeam);

    /// <summary>
    /// Handles loot spawning when bosses die
    /// </summary>
    void ProcessBossDeathLoot(Mob bossMob, GameWorld world, RealTimePlayer killer);

    // =============================================
    // LOOT CLEANUP AND OPTIMIZATION
    // =============================================

    /// <summary>
    /// Removes expired loot from the world
    /// </summary>
    void CleanupExpiredLoot(GameWorld world);

    /// <summary>
    /// Manages loot density per room
    /// </summary>
    void ManageLootDensity(GameWorld world);

    /// <summary>
    /// Cleans up loot data for a world
    /// </summary>
    void CleanupWorldLoot(string worldId);

    // =============================================
    // ITEM EFFECTS AND CONSUMABLES
    // =============================================

    /// <summary>
    /// Applies effects of a consumable item
    /// </summary>
    ConsumableResult UseConsumable(RealTimePlayer player, LootItem consumable);

    /// <summary>
    /// Uses a key item
    /// </summary>
    KeyUseResult UseKey(RealTimePlayer player, LootItem key, GameWorld world);

    /// <summary>
    /// Equips or unequips a weapon/armor
    /// </summary>
    EquipResult EquipItem(RealTimePlayer player, LootItem item);

    // =============================================
    // LOOT STATISTICS AND ANALYTICS
    // =============================================

    /// <summary>
    /// Gets loot statistics for a world
    /// </summary>
    LootStats GetLootStats(string worldId);

    /// <summary>
    /// Gets detailed loot analytics across all worlds
    /// </summary>
    Dictionary<string, object> GetDetailedLootAnalytics();

    /// <summary>
    /// Gets loot distribution by rarity
    /// </summary>
    Dictionary<int, int> GetLootDistributionByRarity(string worldId);

    // =============================================
    // CONFIGURATION AND SETTINGS
    // =============================================

    /// <summary>
    /// Updates loot settings at runtime
    /// </summary>
    void UpdateLootSettings(LootSettings newSettings);

    /// <summary>
    /// Gets current loot settings
    /// </summary>
    LootSettings GetCurrentLootSettings();

    /// <summary>
    /// Adds or updates a loot table
    /// </summary>
    void UpdateLootTable(string tableId, LootTable lootTable);

    // =============================================
    // EVENTS
    // =============================================

    /// <summary>
    /// Event fired when loot is spawned
    /// </summary>
    event Action<string, LootUpdate>? OnLootSpawned; // worldId, lootUpdate

    /// <summary>
    /// Event fired when loot is taken by a player
    /// </summary>
    event Action<string, LootUpdate>? OnLootTaken; // worldId, lootUpdate

    /// <summary>
    /// Event fired when loot expires or is removed
    /// </summary>
    event Action<string, LootUpdate>? OnLootRemoved; // worldId, lootUpdate

    /// <summary>
    /// Event fired when a player uses an item
    /// </summary>
    event Action<RealTimePlayer, LootItem, ItemUseResult>? OnItemUsed; // player, item, result
}