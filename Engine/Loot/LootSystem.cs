using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Loot.Interface;
using MazeWars.GameServer.Engine.Loot.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MazeWars.GameServer.Services.Loot;

public class LootSystem : ILootSystem
{
    private readonly ILogger<LootSystem> _logger;
    private readonly GameServerSettings _settings;
    private readonly LootSettings _lootSettings;
    private readonly Random _random;

    // Loot tables and configuration
    private readonly Dictionary<string, LootTable> _lootTables = new();
    private readonly LootDistribution _lootDistribution = new();

    // World-specific loot tracking
    private readonly Dictionary<string, LootStats> _worldLootStats = new();
    private readonly Dictionary<string, Dictionary<string, DateTime>> _roomLastSpawn = new();

    // Reusable buffer to avoid per-frame allocations
    private readonly List<LootItem> _tempLootBuffer = new(32);

    // Events
    public event Action<string, LootUpdate>? OnLootSpawned;
    public event Action<string, LootUpdate>? OnLootTaken;
    public event Action<string, LootUpdate>? OnLootRemoved;
    public event Action<RealTimePlayer, LootItem, ItemUseResult>? OnItemUsed;
    public event Action<string, LootContainer>? OnContainerSpawned;
    public event Action<string, LootContainer>? OnContainerUpdated;
    public event Action<string, string>? OnContainerRemoved;

    public LootSystem(ILogger<LootSystem> logger, IOptions<GameServerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _random = new Random();

        // Initialize loot settings from game server settings
        _lootSettings = new LootSettings
        {
            GlobalDropRateMultiplier = 1.0f,
            MaxLootPerRoom = 5,
            LootExpirationTimeMinutes = 10,
            LootRespawnIntervalSeconds = _settings.WorldGeneration.LootRespawnIntervalSeconds,
            LootGrabRange = 3.0f,
            EnableDynamicRarity = true,
            LuckStatMultiplier = 0.1f,
            MaxDropsPerMob = 2,
            MaxDropsPerRoom = 5
        };

        _logger.LogInformation("LootSystem initialized with settings: MaxLoot={MaxLoot}, RespawnInterval={Interval}s",
            _lootSettings.MaxLootPerRoom, _lootSettings.LootRespawnIntervalSeconds);
    }

    // =============================================
    // CORE LOOT MANAGEMENT
    // =============================================

    public void InitializeLootTables(Dictionary<string, LootTable> baseLootTables)
    {
        _lootTables.Clear();
        foreach (var (tableId, table) in baseLootTables)
        {
            _lootTables[tableId] = table;
        }

        _logger.LogInformation("Initialized {Count} loot tables: {Tables}",
            _lootTables.Count, string.Join(", ", _lootTables.Keys));
    }

    public List<LootItem> SpawnLootFromTable(GameWorld world, Room room, LootTable lootTable, Vector2? specificPosition = null)
    {
        var spawnedLoot = new List<LootItem>();
        var stats = GetOrCreateWorldStats(world.WorldId);

        foreach (var drop in lootTable.PossibleDrops)
        {
            if (ShouldDropOccur(drop))
            {
                var quantity = _random.Next(drop.MinQuantity, drop.MaxQuantity + 1);

                for (int i = 0; i < quantity; i++)
                {
                    var loot = CreateLootItem(drop, room, specificPosition);

                    // Add to world
                    world.AvailableLoot[loot.LootId] = loot;
                    room.SpawnedLootIds.Add(loot.LootId);
                    world.TotalLootSpawned++;

                    // Update stats
                    stats.TotalLootSpawned++;
                    stats.CurrentActiveLoot++;

                    if (!stats.LootByType.ContainsKey(loot.ItemType))
                        stats.LootByType[loot.ItemType] = 0;
                    stats.LootByType[loot.ItemType]++;

                    if (!stats.LootByRarity.ContainsKey(loot.Rarity))
                        stats.LootByRarity[loot.Rarity] = 0;
                    stats.LootByRarity[loot.Rarity]++;

                    spawnedLoot.Add(loot);

                    // Fire event
                    var lootUpdate = new LootUpdate
                    {
                        UpdateType = "spawned",
                        LootId = loot.LootId,
                        ItemName = loot.ItemName,
                        Position = loot.Position,
                        RoomId = loot.RoomId,
                        Rarity = loot.Rarity
                    };

                    OnLootSpawned?.Invoke(world.WorldId, lootUpdate);

                    _logger.LogDebug("Spawned {ItemName} (rarity {Rarity}) in room {RoomId}",
                        loot.ItemName, loot.Rarity, room.RoomId);
                }
            }
        }

        return spawnedLoot;
    }

    public LootItem CreateLootItem(LootDrop drop, Room room, Vector2? overridePosition = null)
    {
        Vector2 position;

        if (overridePosition.HasValue)
        {
            // Add small random offset even with override position
            var randomOffset = new Vector2(
                (float)(_random.NextDouble() - 0.5) * 2,
                (float)(_random.NextDouble() - 0.5) * 2
            );
            position = overridePosition.Value + randomOffset;
        }
        else
        {
            // Random position within room bounds
            var randomOffset = new Vector2(
                (float)(_random.NextDouble() - 0.5) * room.Size.X * 0.8f,
                (float)(_random.NextDouble() - 0.5) * room.Size.Y * 0.8f
            );
            position = room.Position + randomOffset;
        }

        // Determine weight based on item type / equipment definition
        var weight = drop.ItemType switch
        {
            "consumable" or "potion" => 0.3f,
            "key" => 0.1f,
            _ => 0.5f // default for misc items
        };
        if (drop.Properties.TryGetValue("equipment_id", out var eqId) && eqId is string eqIdStr)
        {
            var equipDef = MazeWars.GameServer.Engine.Equipment.Data.EquipmentRegistry.Get(eqIdStr);
            if (equipDef != null) weight = equipDef.Weight;
        }

        var item = new LootItem
        {
            LootId = Guid.NewGuid().ToString(),
            ItemName = drop.ItemName,
            ItemType = drop.ItemType,
            Rarity = drop.Rarity,
            Position = position,
            RoomId = room.RoomId,
            SpawnedAt = DateTime.UtcNow,
            Properties = new Dictionary<string, object>(drop.Properties),
            Weight = weight
        };
        item.Value = CalculateItemValue(item);
        return item;
    }

    /// <summary>
    /// Creates a LootItem with a specific rarity rolled from ItemRaritySystem.
    /// Rolls discrete quality tier for equipment. Builds display name as "[Quality] [Rarity] BaseName".
    /// </summary>
    public LootItem CreateLootItemWithRarity(LootDrop drop, Room room, int rarity, Vector2? overridePosition = null)
    {
        var loot = CreateLootItem(drop, room, overridePosition);
        loot.Rarity = rarity;
        loot.Properties["rarity"] = rarity;

        // Roll discrete quality tier for equipment items (0=Normal → 4=Masterpiece)
        var qualityTier = 0;
        if (drop.ItemType == "weapon" || drop.ItemType == "armor")
        {
            qualityTier = (int)ItemRaritySystem.RollQualityTier(_random);
            loot.Properties["quality"] = qualityTier;
        }

        // Build display name: "[Quality] [Rarity] BaseName"
        var qualityPrefix = qualityTier > 0 ? $"{ItemRaritySystem.GetQualityName(qualityTier)} " : "";
        var rarityPrefix = rarity > 0 ? $"{ItemRaritySystem.GetRarityName(rarity)} " : "";
        loot.ItemName = $"{qualityPrefix}{rarityPrefix}{drop.ItemName}";

        loot.Value = CalculateItemValue(loot);
        return loot;
    }

    /// <summary>
    /// Calculate the gold value of an item based on type, rarity, and quality.
    /// </summary>
    public static int CalculateItemValue(LootItem item)
    {
        var baseValue = item.ItemType switch
        {
            "weapon" => 100,
            "armor" => 80,
            "consumable" or "potion" => 15,
            "key" => 50,
            "valuable" => 200,
            _ => 20
        };

        var rarityMultiplier = 1.0f + item.Rarity * 0.5f;

        var quality = 0;
        if (item.Properties.TryGetValue("quality", out var q))
        {
            if (q is int qi) quality = qi;
            else if (q is long ql) quality = (int)ql;
        }
        var qualityMultiplier = 1.0f + quality * 0.3f;

        return (int)(baseValue * rarityMultiplier * qualityMultiplier);
    }

    /// <summary>
    /// Create a valuable loot item (gem, jewel, artifact) for treasure rooms.
    /// </summary>
    public LootItem CreateValuableItem(Room room, Vector2? position = null)
    {
        var valuables = new[]
        {
            ("Ruby", 0.25f),
            ("Sapphire", 0.20f),
            ("Emerald", 0.20f),
            ("Gold Bar", 0.15f),
            ("Diamond", 0.10f),
            ("Ancient Coin", 0.10f)
        };

        // Select a valuable based on weighted random
        var roll = (float)_random.NextDouble();
        float cumulative = 0;
        string name = "Ruby";
        foreach (var (vName, chance) in valuables)
        {
            cumulative += chance;
            if (roll <= cumulative)
            {
                name = vName;
                break;
            }
        }

        var rarity = (int)ItemRaritySystem.RollRarity(_random, room.RoomType, "patrol");

        var pos = position ?? new Vector2(
            room.Position.X + (float)(_random.NextDouble() - 0.5) * room.Size.X * 0.6f,
            room.Position.Y + (float)(_random.NextDouble() - 0.5) * room.Size.Y * 0.6f
        );

        var item = new LootItem
        {
            LootId = Guid.NewGuid().ToString(),
            ItemName = rarity > 0 ? $"{ItemRaritySystem.GetRarityName(rarity)} {name}" : name,
            ItemType = "valuable",
            Rarity = rarity,
            Position = pos,
            RoomId = room.RoomId,
            SpawnedAt = DateTime.UtcNow,
            Weight = 0.2f,
            Properties = new Dictionary<string, object> { ["rarity"] = rarity }
        };
        item.Value = CalculateItemValue(item);
        return item;
    }

    public void SpawnRandomLoot(GameWorld world)
    {
        var availableRooms = world.Rooms.Values
            .Where(r => !r.IsCompleted && r.SpawnedLootIds.Count < _lootSettings.MaxLootPerRoom)
            .ToList();

        if (!availableRooms.Any())
        {
            _logger.LogDebug("No available rooms for random loot spawn in world {WorldId}", world.WorldId);
            return;
        }

        var room = availableRooms[_random.Next(availableRooms.Count)];

        // Choose appropriate loot table based on room state
        var tableId = room.IsCompleted ? "weapon_drops" : "consumable_drops";

        if (_lootTables.TryGetValue(tableId, out var lootTable))
        {
            var spawnedLoot = SpawnLootFromTable(world, room, lootTable);

            if (spawnedLoot.Any())
            {
                _logger.LogDebug("Random loot spawn in room {RoomId}: {Count} items",
                    room.RoomId, spawnedLoot.Count);
            }
        }
    }

    public void ProcessLootSpawning(GameWorld world, float deltaTime)
    {
        var currentTime = DateTime.UtcNow;

        // Process dynamic spawning (mob drops)
        ProcessDynamicLootSpawning(world);

        // Cleanup expired loot
        CleanupExpiredLoot(world);

        // Manage loot density
        ManageLootDensity(world);
    }

    private void ProcessDynamicLootSpawning(GameWorld world)
    {
        // Mob death loot is handled by GameEngine.HandleMobDeath (via MobAISystem.OnMobDeath event).
        // No additional processing needed here.
    }

    // =============================================
    // PLAYER LOOT INTERACTIONS
    // =============================================

    public LootGrabResult ProcessLootGrab(RealTimePlayer player, string lootId, GameWorld world)
    {
        var result = new LootGrabResult { Success = false };

        if (!player.IsAlive)
        {
            result.ErrorMessage = "Player is not alive";
            return result;
        }

        if (!world.AvailableLoot.TryGetValue(lootId, out var loot))
        {
            result.ErrorMessage = "Loot not found";
            return result;
        }

        if (!CanPlayerGrabLoot(player, loot, world))
        {
            result.ErrorMessage = "Cannot grab loot";
            result.OutOfRange = GameMathUtils.Distance(player.Position, loot.Position) > _lootSettings.LootGrabRange;
            result.WrongRoom = player.CurrentRoomId != loot.RoomId;
            return result;
        }

        // Allow stacking even at max capacity
        bool canStack = IsStackable(loot) && TryStackItem(player, loot);
        if (!canStack && player.Inventory.Count >= _settings.GameBalance.MaxInventorySize)
        {
            result.ErrorMessage = "Inventory full";
            result.InventoryFull = true;
            return result;
        }

        // Successfully grab the loot
        if (world.AvailableLoot.TryRemove(lootId, out var grabbedLoot))
        {
            if (!canStack)
                player.Inventory.Add(grabbedLoot);
            // else: TryStackItem already incremented existing stack
            result.Success = true;
            result.GrabbedItem = grabbedLoot;

            // Update room
            if (world.Rooms.TryGetValue(grabbedLoot.RoomId, out var room))
            {
                room.SpawnedLootIds.Remove(grabbedLoot.LootId);
            }

            // Update stats
            var stats = GetOrCreateWorldStats(world.WorldId);
            stats.TotalLootTaken++;
            stats.CurrentActiveLoot = Math.Max(0, stats.CurrentActiveLoot - 1);

            // Fire events
            var lootUpdate = new LootUpdate
            {
                UpdateType = "taken",
                LootId = grabbedLoot.LootId,
                ItemName = grabbedLoot.ItemName,
                Position = grabbedLoot.Position,
                TakenBy = player.PlayerId,
                RoomId = grabbedLoot.RoomId,
                Rarity = grabbedLoot.Rarity
            };

            OnLootTaken?.Invoke(world.WorldId, lootUpdate);

            _logger.LogInformation("Player {PlayerName} grabbed {ItemName} (rarity {Rarity})",
                player.PlayerName, grabbedLoot.ItemName, grabbedLoot.Rarity);
        }

        return result;
    }

    public List<LootItem> DropPlayerLoot(RealTimePlayer deadPlayer, GameWorld world)
    {
        var container = new LootContainer
        {
            ContainerId = $"corpse_{deadPlayer.PlayerId}_{Guid.NewGuid().ToString()[..8]}",
            ContainerType = "player_corpse",
            Position = deadPlayer.Position,
            RoomId = deadPlayer.CurrentRoomId,
            DespawnAfterSeconds = 300f, // 5 minutes
            SourceId = deadPlayer.PlayerId,
            DisplayName = $"{deadPlayer.PlayerName}'s Corpse"
        };

        // Collect ALL items (equipped + inventory)
        var allItems = new List<LootItem>();
        foreach (var (slot, equippedItem) in deadPlayer.Equipment)
            allItems.Add(equippedItem);
        foreach (var invItem in deadPlayer.Inventory)
            allItems.Add(invItem);

        deadPlayer.Equipment.Clear();
        deadPlayer.Inventory.Clear();

        // Item destruction: randomly destroy a percentage of items (item sink)
        var destroyCount = (int)Math.Round(allItems.Count * _lootSettings.ItemDestructionRate);
        if (destroyCount > 0 && allItems.Count > 1)
        {
            // Fisher-Yates shuffle to randomize which items get destroyed
            for (int i = allItems.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (allItems[i], allItems[j]) = (allItems[j], allItems[i]);
            }

            var destroyed = allItems.Take(destroyCount).ToList();
            allItems = allItems.Skip(destroyCount).ToList();

            _logger.LogInformation("Player {PlayerName} death: {DestroyCount} items destroyed: {Items}",
                deadPlayer.PlayerName, destroyCount,
                string.Join(", ", destroyed.Select(d => d.ItemName)));
        }

        // Put surviving items into corpse
        foreach (var item in allItems)
            container.Contents.Add(item);

        if (container.Contents.Any())
        {
            world.LootContainers[container.ContainerId] = container;
            OnContainerSpawned?.Invoke(world.WorldId, container);
        }

        _logger.LogInformation("Player {PlayerName} died, corpse has {Count} items ({Destroyed} destroyed)",
            deadPlayer.PlayerName, container.Contents.Count, destroyCount);

        return container.Contents;
    }

    public LootGrabResult GrabFromContainer(RealTimePlayer player, string containerId, string lootId, GameWorld world)
    {
        var result = new LootGrabResult { Success = false };

        if (!player.IsAlive)
        {
            result.ErrorMessage = "Player is not alive";
            return result;
        }

        if (!world.LootContainers.TryGetValue(containerId, out var container))
        {
            result.ErrorMessage = "Container not found";
            return result;
        }

        // Range check (3.5 units — slightly more than client's 3.0 to avoid edge-case rejections)
        var distance = Vector2.Distance(player.Position, container.Position);
        if (distance > 3.5f)
        {
            result.ErrorMessage = "Too far from container";
            result.OutOfRange = true;
            return result;
        }

        // Find the item in the container
        var item = container.Contents.FirstOrDefault(i => i.LootId == lootId);
        if (item == null)
        {
            result.ErrorMessage = "Item not in container";
            return result;
        }

        // Allow stacking even at max capacity
        bool canStack = IsStackable(item) && TryStackItem(player, item);
        if (!canStack && player.Inventory.Count >= _settings.GameBalance.MaxInventorySize)
        {
            result.ErrorMessage = "Inventory full";
            result.InventoryFull = true;
            return result;
        }

        // Move item to player inventory
        container.Contents.Remove(item);
        if (!canStack)
            player.Inventory.Add(item);

        result.Success = true;
        result.GrabbedItem = item;

        _logger.LogInformation("Player {PlayerName} grabbed {ItemName} from container {ContainerId}",
            player.PlayerName, item.ItemName, containerId);

        // If container is now empty, remove it
        if (!container.Contents.Any())
        {
            world.LootContainers.TryRemove(containerId, out _);
            OnContainerRemoved?.Invoke(world.WorldId, containerId);

            _logger.LogDebug("Container {ContainerId} emptied and removed", containerId);
        }
        else
        {
            OnContainerUpdated?.Invoke(world.WorldId, container);
        }

        return result;
    }

    public void CleanupExpiredContainers(GameWorld world)
    {
        var now = DateTime.UtcNow;
        var expiredIds = new List<string>();

        foreach (var (id, container) in world.LootContainers)
        {
            if (container.DespawnAfterSeconds <= 0) continue; // permanent
            var elapsed = (float)(now - container.CreatedAt).TotalSeconds;
            if (elapsed >= container.DespawnAfterSeconds)
                expiredIds.Add(id);
        }

        foreach (var id in expiredIds)
        {
            if (world.LootContainers.TryRemove(id, out _))
            {
                OnContainerRemoved?.Invoke(world.WorldId, id);
                _logger.LogDebug("Container {ContainerId} expired and removed", id);
            }
        }
    }

    public ItemUseResult UseItem(RealTimePlayer player, LootItem item, GameWorld world)
    {
        var result = new ItemUseResult { Success = false };

        if (!player.IsAlive)
        {
            result.ErrorMessage = "Player is not alive";
            return result;
        }

        switch (item.ItemType.ToLower())
        {
            case "consumable":
                var consumableResult = UseConsumable(player, item);
                result.Success = consumableResult.Success;
                result.ErrorMessage = consumableResult.ErrorMessage;
                result.HealthRestored = consumableResult.HealthRestored;
                result.ManaRestored = consumableResult.ManaRestored;
                result.AppliedEffects = consumableResult.StatusEffects;
                result.ItemConsumed = true;
                result.Message = $"Used {item.ItemName}";
                break;

            case "key":
                var keyResult = UseKey(player, item, world);
                result.Success = keyResult.Success;
                result.ErrorMessage = keyResult.ErrorMessage;
                result.Message = keyResult.Success ? keyResult.UnlockedDoor : keyResult.ErrorMessage;
                result.ItemConsumed = keyResult.Success;
                result.UnlockedConnectionId = keyResult.ConnectionId;
                break;

            case "weapon":
            case "armor":
                var equipResult = EquipItem(player, item);
                result.Success = equipResult.Success;
                result.ErrorMessage = equipResult.ErrorMessage;
                result.Message = equipResult.Success ? $"Equipped {item.ItemName}" : equipResult.ErrorMessage;
                result.ItemConsumed = false;
                break;

            default:
                result.ErrorMessage = $"Cannot use item type: {item.ItemType}";
                break;
        }

        if (result.Success)
        {
            OnItemUsed?.Invoke(player, item, result);
            _logger.LogInformation("Player {PlayerName} used {ItemName} ({ItemType})",
                player.PlayerName, item.ItemName, item.ItemType);
        }

        return result;
    }

    public bool CanPlayerGrabLoot(RealTimePlayer player, LootItem loot, GameWorld world)
    {
        // Distance check
        var distance = GameMathUtils.Distance(player.Position, loot.Position);
        if (distance > _lootSettings.LootGrabRange)
            return false;

        // Room check
        if (player.CurrentRoomId != loot.RoomId)
            return false;

        // Inventory space check (stackable items can bypass if a matching stack exists)
        if (player.Inventory.Count >= _settings.GameBalance.MaxInventorySize)
        {
            if (!IsStackable(loot) || !player.Inventory.Any(i =>
                i.ItemName == loot.ItemName && i.ItemType == loot.ItemType && i.StackCount < 99))
                return false;
        }

        return true;
    }

    // =============================================
    // STACKING HELPERS
    // =============================================

    private static bool IsStackable(LootItem item)
    {
        return item.ItemType is "consumable" or "potion" or "key";
    }

    /// <summary>
    /// Try to stack an item onto an existing matching stack in the player's inventory.
    /// Returns true if successfully stacked (caller should NOT add a new slot).
    /// </summary>
    private static bool TryStackItem(RealTimePlayer player, LootItem item)
    {
        const int MaxStackSize = 99;
        var existing = player.Inventory.FirstOrDefault(i =>
            i.ItemName == item.ItemName && i.ItemType == item.ItemType && i.StackCount < MaxStackSize);
        if (existing == null) return false;

        existing.StackCount += item.StackCount;
        if (existing.StackCount > MaxStackSize)
        {
            // Overflow — leave remainder on the incoming item (won't be added by caller since we return true)
            // For simplicity, clamp to max — single pickups won't overflow 99
            existing.StackCount = MaxStackSize;
        }
        return true;
    }

    // =============================================
    // WEIGHT HELPERS
    // =============================================

    /// <summary>
    /// Calculate total weight carried by a player (inventory + equipped items at half weight).
    /// </summary>
    public static float CalculatePlayerWeight(RealTimePlayer player)
    {
        float weight = 0f;
        foreach (var item in player.Inventory)
            weight += item.Weight * item.StackCount;
        foreach (var (_, equippedItem) in player.Equipment)
            weight += equippedItem.Weight * 0.5f; // Equipped items count at half weight
        return weight;
    }

    public int CalculateDynamicRarity(GameWorld world, Room room, string triggerCondition)
    {
        if (!_lootSettings.EnableDynamicRarity)
            return 1; // Default rarity

        var baseRarity = 1;
        var rarityModifiers = 0f;

        // World completion modifier
        var completionRate = (float)world.Rooms.Values.Count(r => r.IsCompleted) / world.Rooms.Count;
        rarityModifiers += completionRate * 2f; // Up to +2 rarity for world completion

        // Room difficulty modifier (center rooms are harder)
        var roomCoords = ExtractRoomCoordinates(room.RoomId);
        if (roomCoords.HasValue)
        {
            var (x, y) = roomCoords.Value;
            var centerDistance = Math.Sqrt(Math.Pow(x - 1.5f, 2) + Math.Pow(y - 1.5f, 2));
            if (centerDistance < 1.0f) // Center rooms
                rarityModifiers += 1f;
        }

        // Trigger condition modifier
        rarityModifiers += triggerCondition.ToLower() switch
        {
            "boss_death" => 2f,
            "room_clear" => 1f,
            "mob_death" => 0.5f,
            _ => 0f
        };

        // Time-based modifier (longer games = better loot)
        var gameAge = DateTime.UtcNow - world.CreatedAt;
        if (gameAge.TotalMinutes > 10)
            rarityModifiers += 1f;

        return Math.Max(1, baseRarity + (int)Math.Round(rarityModifiers));
    }

    public float GetLuckModifier(RealTimePlayer player)
    {
        // Class-based luck modifiers (stats removed, luck is class-intrinsic)
        var classModifier = player.PlayerClass.ToLower() switch
        {
            "scout" => 0.1f,
            "support" => 0.05f,
            "assassin" => 0.08f,
            "warlock" => 0.03f,
            "tank" => 0f,
            _ => 0f
        };

        return 1f + classModifier;
    }

    public bool ShouldDropOccur(LootDrop drop, RealTimePlayer? player = null, float baseMultiplier = 1.0f)
    {
        var finalChance = drop.DropChance * baseMultiplier * _lootSettings.GlobalDropRateMultiplier;

        if (player != null)
        {
            finalChance *= GetLuckModifier(player);
        }

        return _random.NextDouble() < finalChance;
    }

    // =============================================
    // DYNAMIC LOOT SPAWNING
    // =============================================

    public void ProcessMobDeathLoot(Mob deadMob, GameWorld world, RealTimePlayer? killer = null)
    {
        if (!_lootTables.TryGetValue("equipment", out var lootTable))
        {
            _logger.LogWarning("Equipment loot table not found for mob death");
            return;
        }

        var room = world.Rooms.Values.FirstOrDefault(r => r.RoomId == deadMob.RoomId);
        if (room == null)
        {
            _logger.LogWarning("Room {RoomId} not found for mob death loot", deadMob.RoomId);
            return;
        }

        // Drop chance gate: single roll decides IF mob drops anything at all
        float mobDropChance = deadMob.MobType.ToLower() switch
        {
            "boss" => 1.0f,
            "elite" => 0.60f,
            "caster" or "archer" or "healer" => 0.30f,
            _ => 0.25f
        };

        if (_random.NextDouble() >= mobDropChance)
        {
            _logger.LogDebug("💀 Mob {MobId} ({MobType}) failed drop gate ({Chance}%)",
                deadMob.MobId, deadMob.MobType, mobDropChance * 100);
            return;
        }

        // Create corpse container for the dead mob
        var container = new LootContainer
        {
            ContainerId = $"corpse_{deadMob.MobId}",
            ContainerType = "mob_corpse",
            Position = deadMob.Position,
            RoomId = deadMob.RoomId,
            DespawnAfterSeconds = 300f, // 5 minutes
            SourceId = deadMob.MobId,
            DisplayName = $"Fallen {deadMob.MobType}"
        };

        // Boss gets 2 item rolls, everything else gets 1
        var maxDrops = deadMob.MobType.ToLower() == "boss" ? 2 : 1;
        maxDrops = Math.Min(maxDrops, lootTable.PossibleDrops.Count);

        for (int i = 0; i < maxDrops; i++)
        {
            var eligibleDrops = lootTable.PossibleDrops
                .Where(drop => ShouldDropOccur(drop, killer))
                .ToList();

            if (eligibleDrops.Any())
            {
                var selectedDrop = eligibleDrops[_random.Next(eligibleDrops.Count)];
                var rarity = ItemRaritySystem.RollRarity(_random, room.RoomType, deadMob.MobType.ToLower());
                var loot = CreateLootItemWithRarity(selectedDrop, room, (int)rarity, deadMob.Position);
                container.Contents.Add(loot);
            }
        }

        // Roll for key drop (separate from equipment)
        if (_lootTables.TryGetValue("key_drops", out var keyTable))
        {
            float keyDropChance = deadMob.MobType.ToLower() switch
            {
                "boss" => 1.0f,
                "elite" => 0.15f,
                _ => 0.05f
            };

            if (_random.NextDouble() < keyDropChance)
            {
                var eligibleKeys = keyTable.PossibleDrops
                    .Where(drop => ShouldDropOccur(drop, killer))
                    .ToList();

                if (eligibleKeys.Any())
                {
                    var selectedKey = eligibleKeys[_random.Next(eligibleKeys.Count)];
                    var keyItem = new LootItem
                    {
                        LootId = Guid.NewGuid().ToString(),
                        ItemName = selectedKey.ItemName,
                        ItemType = selectedKey.ItemType,
                        Rarity = 2,
                        Position = deadMob.Position,
                        RoomId = deadMob.RoomId,
                        Properties = new Dictionary<string, object>(selectedKey.Properties),
                        Weight = 0.1f
                    };
                    container.Contents.Add(keyItem);
                }
            }
        }

        if (container.Contents.Any())
        {
            world.LootContainers[container.ContainerId] = container;
            OnContainerSpawned?.Invoke(world.WorldId, container);

            _logger.LogInformation("Mob corpse container created: {ContainerId} with {Count} items [{Items}]",
                container.ContainerId, container.Contents.Count,
                string.Join(", ", container.Contents.Select(c => $"{c.ItemName}({c.LootId[..8]})")));
        }
        else
        {
            _logger.LogInformation("Mob {MobId} ({MobType}) died but dropped 0 items (no container created)",
                deadMob.MobId, deadMob.MobType);
        }
    }

    public void ProcessRoomCompletionLoot(Room completedRoom, GameWorld world, string completingTeam)
    {
        if (!_lootTables.TryGetValue("consumable_drops", out var lootTable))
        {
            _logger.LogWarning("Consumable drops table not found for room completion");
            return;
        }

        var bonusDrops = CalculateBonusDrops(world, completedRoom, completingTeam);
        var spawnedLoot = SpawnLootFromTable(world, completedRoom, lootTable);

        // Bonus drops for first completion
        for (int i = 0; i < bonusDrops; i++)
        {
            var additionalLoot = SpawnLootFromTable(world, completedRoom, lootTable);
            spawnedLoot.AddRange(additionalLoot);
        }

        _logger.LogInformation("Room {RoomId} completion by team {TeamId} spawned {Count} loot items",
            completedRoom.RoomId, completingTeam, spawnedLoot.Count);
    }

    public void ProcessBossDeathLoot(Mob bossMob, GameWorld world, RealTimePlayer killer)
    {
        if (!_lootTables.TryGetValue("equipment", out var lootTable))
        {
            _logger.LogWarning("Equipment loot table not found for boss death");
            return;
        }

        var room = world.Rooms.Values.FirstOrDefault(r => r.RoomId == bossMob.RoomId);
        if (room == null) return;

        // Boss corpse with multiple drops
        var container = new LootContainer
        {
            ContainerId = $"corpse_{bossMob.MobId}",
            ContainerType = "mob_corpse",
            Position = bossMob.Position,
            RoomId = bossMob.RoomId,
            DespawnAfterSeconds = 600f, // 10 minutes for boss
            SourceId = bossMob.MobId,
            DisplayName = "Fallen Boss"
        };

        // Boss drops 2-4 items
        var dropCount = _random.Next(2, 5);
        for (int i = 0; i < dropCount; i++)
        {
            var selectedDrop = lootTable.PossibleDrops[_random.Next(lootTable.PossibleDrops.Count)];
            var rarity = ItemRaritySystem.RollRarity(_random, room.RoomType, "boss");
            var loot = CreateLootItemWithRarity(selectedDrop, room, (int)rarity, bossMob.Position);
            container.Contents.Add(loot);
        }

        world.LootContainers[container.ContainerId] = container;
        OnContainerSpawned?.Invoke(world.WorldId, container);

        _logger.LogInformation("Boss {MobId} killed by {PlayerName}, corpse has {Count} items",
            bossMob.MobId, killer.PlayerName, container.Contents.Count);
    }

    // =============================================
    // LOOT CLEANUP AND OPTIMIZATION
    // =============================================

    public void CleanupExpiredLoot(GameWorld world)
    {
        var currentTime = DateTime.UtcNow;
        _tempLootBuffer.Clear();
        foreach (var loot in world.AvailableLoot.Values)
        {
            if ((currentTime - loot.SpawnedAt).TotalMinutes > _lootSettings.LootExpirationTimeMinutes)
                _tempLootBuffer.Add(loot);
        }

        foreach (var loot in _tempLootBuffer)
        {
            if (world.AvailableLoot.TryRemove(loot.LootId, out _))
            {
                // Remove from room
                if (world.Rooms.TryGetValue(loot.RoomId, out var room))
                {
                    room.SpawnedLootIds.Remove(loot.LootId);
                }

                // Update stats
                var stats = GetOrCreateWorldStats(world.WorldId);
                stats.TotalLootExpired++;
                stats.CurrentActiveLoot = Math.Max(0, stats.CurrentActiveLoot - 1);

                // Fire event
                var lootUpdate = new LootUpdate
                {
                    UpdateType = "expired",
                    LootId = loot.LootId,
                    ItemName = loot.ItemName,
                    Position = loot.Position,
                    RoomId = loot.RoomId
                };

                OnLootRemoved?.Invoke(world.WorldId, lootUpdate);
            }
        }

        if (_tempLootBuffer.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired loot items in world {WorldId}",
                _tempLootBuffer.Count, world.WorldId);
        }
    }

    public void ManageLootDensity(GameWorld world)
    {
        foreach (var room in world.Rooms.Values)
        {
            _tempLootBuffer.Clear();
            foreach (var loot in world.AvailableLoot.Values)
            {
                if (loot.RoomId == room.RoomId)
                    _tempLootBuffer.Add(loot);
            }

            if (_tempLootBuffer.Count > _lootSettings.MaxLootPerRoom)
            {
                // Remove oldest loot items first
                _tempLootBuffer.Sort((a, b) => a.SpawnedAt.CompareTo(b.SpawnedAt));
                var removeCount = _tempLootBuffer.Count - _lootSettings.MaxLootPerRoom;
                var oldestLoot = _tempLootBuffer.GetRange(0, removeCount);

                foreach (var loot in oldestLoot)
                {
                    if (world.AvailableLoot.TryRemove(loot.LootId, out _))
                    {
                        room.SpawnedLootIds.Remove(loot.LootId);

                        var lootUpdate = new LootUpdate
                        {
                            UpdateType = "density_cleanup",
                            LootId = loot.LootId,
                            ItemName = loot.ItemName,
                            Position = loot.Position,
                            RoomId = loot.RoomId
                        };

                        OnLootRemoved?.Invoke(world.WorldId, lootUpdate);
                    }
                }

                _logger.LogDebug("Room {RoomId} density cleanup: removed {Count} items",
                    room.RoomId, oldestLoot.Count);
            }
        }
    }

    public void CleanupWorldLoot(string worldId)
    {
        _worldLootStats.Remove(worldId);
        _roomLastSpawn.Remove(worldId);

        _logger.LogDebug("Cleaned up loot data for world {WorldId}", worldId);
    }

    // =============================================
    // ITEM EFFECTS AND CONSUMABLES
    // =============================================

    public ConsumableResult UseConsumable(RealTimePlayer player, LootItem consumable)
    {
        var result = new ConsumableResult { Success = true };

        try
        {
            var healValue = GetPropertyValue<int>(consumable, "heal", 0);
            var manaValue = GetPropertyValue<int>(consumable, "mana", 0);
            var speedValue = GetPropertyValue<float>(consumable, "speed_boost", 1.0f);
            var durationValue = GetPropertyValue<int>(consumable, "duration", 0);

            // Apply healing
            if (healValue > 0)
            {
                var actualHealing = Math.Min(healValue, player.MaxHealth - player.Health);
                player.Health = Math.Min(player.MaxHealth, player.Health + healValue);
                result.HealthRestored = actualHealing;
            }

            // Apply mana restoration
            if (manaValue > 0)
            {
                var actualManaRestore = Math.Min(manaValue, player.MaxMana - player.Mana);
                player.Mana = Math.Min(player.MaxMana, player.Mana + manaValue);
                result.ManaRestored = actualManaRestore;
            }

            // Apply temporary effects
            if (speedValue > 1.0f && durationValue > 0)
            {
                var speedEffect = new StatusEffect
                {
                    EffectType = "speed",
                    Value = 0,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(durationValue),
                    SourcePlayerId = player.PlayerId
                };

                result.StatusEffects.Add(speedEffect);
                result.DurationSeconds = durationValue;
            }

            // Handle special consumables
            HandleSpecialConsumableEffects(player, consumable, result);

            _logger.LogDebug("Player {PlayerName} used consumable {ItemName}: +{Health}HP, +{Mana}MP",
                player.PlayerName, consumable.ItemName, result.HealthRestored, result.ManaRestored);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error using consumable {ItemName} for player {PlayerName}",
                consumable.ItemName, player.PlayerName);
            result.Success = false;
            result.ErrorMessage = "Failed to use consumable";
        }

        return result;
    }

    public KeyUseResult UseKey(RealTimePlayer player, LootItem key, GameWorld world)
    {
        var result = new KeyUseResult { Success = false };

        // Determine key type from Properties or ItemName
        var keyType = key.Properties.TryGetValue("key_type", out var kt) ? kt?.ToString() ?? "" : "";
        if (string.IsNullOrEmpty(keyType))
        {
            keyType = key.ItemName.ToLower() switch
            {
                "gold key" => "gold",
                "silver key" => "silver",
                "master key" => "master",
                _ => ""
            };
        }

        if (string.IsNullOrEmpty(keyType))
        {
            result.ErrorMessage = $"Unknown key type: {key.ItemName}";
            return result;
        }

        // Find locked doors connected to player's current room
        var playerRoom = player.CurrentRoomId;
        LockedDoor? targetDoor = null;

        foreach (var door in world.LockedDoors.Values)
        {
            if (!door.IsLocked) continue;
            if (door.RoomIdA != playerRoom && door.RoomIdB != playerRoom) continue;

            // Check if key can open this door
            if (CanKeyUnlock(keyType, door.RequiredKeyType))
            {
                targetDoor = door;
                break;
            }
        }

        if (targetDoor == null)
        {
            result.ErrorMessage = "No matching locked door nearby";
            return result;
        }

        // Unlock the door
        targetDoor.IsLocked = false;
        targetDoor.UnlockedByPlayerId = player.PlayerId;
        targetDoor.UnlockedAt = DateTime.UtcNow;

        result.Success = true;
        result.ConnectionId = targetDoor.ConnectionId;
        result.UnlockedDoor = $"Unlocked {targetDoor.RequiredKeyType} door with {key.ItemName}";

        _logger.LogInformation("Player {PlayerName} unlocked {KeyType} door ({ConnectionId}) with {KeyName}",
            player.PlayerName, targetDoor.RequiredKeyType, targetDoor.ConnectionId, key.ItemName);

        return result;
    }

    private static bool CanKeyUnlock(string keyType, string requiredKeyType)
    {
        return keyType switch
        {
            "master" => true,
            "gold" => requiredKeyType == "gold" || requiredKeyType == "silver",
            "silver" => requiredKeyType == "silver",
            _ => keyType == requiredKeyType
        };
    }

    public EquipResult EquipItem(RealTimePlayer player, LootItem item)
    {
        var result = new EquipResult { Success = false };

        try
        {
            switch (item.ItemType.ToLower())
            {
                case "weapon":
                    result = EquipWeapon(player, item);
                    break;

                case "armor":
                    result = EquipArmor(player, item);
                    break;

                default:
                    result.ErrorMessage = $"Cannot equip item type: {item.ItemType}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error equipping item {ItemName} for player {PlayerName}",
                item.ItemName, player.PlayerName);
            result.ErrorMessage = "Failed to equip item";
        }

        return result;
    }

    // =============================================
    // LOOT STATISTICS AND ANALYTICS
    // =============================================

    public LootStats GetLootStats(string worldId)
    {
        return GetOrCreateWorldStats(worldId);
    }

    public Dictionary<string, object> GetDetailedLootAnalytics()
    {
        // Snapshot to avoid concurrent modification issues
        var statsSnapshot = _worldLootStats.Where(s => s.Value != null).ToList();

        var totalSpawned = statsSnapshot.Sum(s => s.Value.TotalLootSpawned);
        var totalTaken = statsSnapshot.Sum(s => s.Value.TotalLootTaken);
        var totalExpired = statsSnapshot.Sum(s => s.Value.TotalLootExpired);
        var currentActive = statsSnapshot.Sum(s => s.Value.CurrentActiveLoot);

        var pickupRate = totalSpawned > 0 ? (double)totalTaken / totalSpawned : 0;
        var expirationRate = totalSpawned > 0 ? (double)totalExpired / totalSpawned : 0;

        var worldStats = new Dictionary<string, object>();
        foreach (var kvp in statsSnapshot)
        {
            worldStats[kvp.Key] = new
            {
                kvp.Value.TotalLootSpawned,
                kvp.Value.TotalLootTaken,
                kvp.Value.CurrentActiveLoot,
                kvp.Value.MostPopularItem,
                kvp.Value.RarestItemDropped
            };
        }

        return new Dictionary<string, object>
        {
            ["TotalLootSpawned"] = totalSpawned,
            ["TotalLootTaken"] = totalTaken,
            ["TotalLootExpired"] = totalExpired,
            ["CurrentActiveLoot"] = currentActive,
            ["PickupRate"] = pickupRate,
            ["ExpirationRate"] = expirationRate,
            ["ActiveWorlds"] = statsSnapshot.Count,
            ["LootByType"] = CombineLootByType(),
            ["LootByRarity"] = CombineLootByRarity(),
            ["WorldStats"] = worldStats
        };
    }

    public Dictionary<int, int> GetLootDistributionByRarity(string worldId)
    {
        if (_worldLootStats.TryGetValue(worldId, out var stats))
        {
            return new Dictionary<int, int>(stats.LootByRarity);
        }

        return new Dictionary<int, int>();
    }

    // =============================================
    // CONFIGURATION AND SETTINGS
    // =============================================

    public void UpdateLootSettings(LootSettings newSettings)
    {
        _lootSettings.GlobalDropRateMultiplier = newSettings.GlobalDropRateMultiplier;
        _lootSettings.MaxLootPerRoom = newSettings.MaxLootPerRoom;
        _lootSettings.LootExpirationTimeMinutes = newSettings.LootExpirationTimeMinutes;
        _lootSettings.LootRespawnIntervalSeconds = newSettings.LootRespawnIntervalSeconds;
        _lootSettings.LootGrabRange = newSettings.LootGrabRange;
        _lootSettings.EnableDynamicRarity = newSettings.EnableDynamicRarity;
        _lootSettings.LuckStatMultiplier = newSettings.LuckStatMultiplier;
        _lootSettings.MaxDropsPerMob = newSettings.MaxDropsPerMob;
        _lootSettings.MaxDropsPerRoom = newSettings.MaxDropsPerRoom;

        _logger.LogInformation("Updated loot settings - DropRate: {DropRate}, MaxPerRoom: {MaxRoom}",
            newSettings.GlobalDropRateMultiplier, newSettings.MaxLootPerRoom);
    }

    public LootSettings GetCurrentLootSettings()
    {
        return new LootSettings
        {
            GlobalDropRateMultiplier = _lootSettings.GlobalDropRateMultiplier,
            MaxLootPerRoom = _lootSettings.MaxLootPerRoom,
            LootExpirationTimeMinutes = _lootSettings.LootExpirationTimeMinutes,
            LootRespawnIntervalSeconds = _lootSettings.LootRespawnIntervalSeconds,
            LootGrabRange = _lootSettings.LootGrabRange,
            EnableDynamicRarity = _lootSettings.EnableDynamicRarity,
            LuckStatMultiplier = _lootSettings.LuckStatMultiplier,
            MaxDropsPerMob = _lootSettings.MaxDropsPerMob,
            MaxDropsPerRoom = _lootSettings.MaxDropsPerRoom,
            EnableLootMagnetism = _lootSettings.EnableLootMagnetism,
            MagnetismRange = _lootSettings.MagnetismRange
        };
    }

    public void UpdateLootTable(string tableId, LootTable lootTable)
    {
        _lootTables[tableId] = lootTable;
        _logger.LogInformation("Updated loot table {TableId} with {DropCount} drops",
            tableId, lootTable.PossibleDrops.Count);
    }

    // =============================================
    // HELPER METHODS
    // =============================================

    private LootStats GetOrCreateWorldStats(string worldId)
    {
        if (!_worldLootStats.TryGetValue(worldId, out var stats))
        {
            stats = new LootStats();
            _worldLootStats[worldId] = stats;
        }
        return stats;
    }

    private T GetPropertyValue<T>(LootItem item, string propertyName, T defaultValue)
    {
        if (item.Properties.TryGetValue(propertyName, out var value))
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    private (int x, int y)? ExtractRoomCoordinates(string roomId)
    {
        // Extract coordinates from room ID like "room_1_2"
        var parts = roomId.Split('_');
        if (parts.Length == 3 && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var y))
        {
            return (x, y);
        }
        return null;
    }

    private int CalculateBonusDrops(GameWorld world, Room completedRoom, string completingTeam)
    {
        var bonusDrops = 0;

        // First team to complete a room gets bonus
        if (string.IsNullOrEmpty(completedRoom.CompletedByTeam))
            bonusDrops += 1;

        // Bonus for completing difficult rooms (center rooms)
        var coords = ExtractRoomCoordinates(completedRoom.RoomId);
        if (coords.HasValue)
        {
            var (x, y) = coords.Value;
            var centerDistance = Math.Sqrt(Math.Pow(x - 1.5f, 2) + Math.Pow(y - 1.5f, 2));
            if (centerDistance < 1.0f)
                bonusDrops += 1;
        }

        return bonusDrops;
    }

    private void HandleSpecialConsumableEffects(RealTimePlayer player, LootItem consumable, ConsumableResult result)
    {
        // Handle special items with unique effects
        switch (consumable.ItemName.ToLower())
        {
            case "speed elixir":
                // Already handled in main consumable logic
                break;

            case "strength potion":
                if (GetPropertyValue<int>(consumable, "strength_boost", 0) > 0)
                {
                    var strengthEffect = new StatusEffect
                    {
                        EffectType = "strength_boost",
                        Value = GetPropertyValue<int>(consumable, "strength_boost", 0),
                        ExpiresAt = DateTime.UtcNow.AddSeconds(GetPropertyValue<int>(consumable, "duration", 60)),
                        SourcePlayerId = player.PlayerId
                    };
                    result.StatusEffects.Add(strengthEffect);
                }
                break;

            case "invisibility potion":
                var stealthEffect = new StatusEffect
                {
                    EffectType = "stealth",
                    Value = 0,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(GetPropertyValue<int>(consumable, "duration", 10)),
                    SourcePlayerId = player.PlayerId
                };
                result.StatusEffects.Add(stealthEffect);
                break;
        }
    }

    private EquipResult EquipWeapon(RealTimePlayer player, LootItem weapon)
    {
        var result = new EquipResult { Success = true, EquippedItem = weapon };

        // For now, just add weapon stats to player (in future, maintain equipped items list)
        var damage = GetPropertyValue<int>(weapon, "damage", 0);
        var speed = GetPropertyValue<float>(weapon, "speed", 1.0f);
        var critical = GetPropertyValue<float>(weapon, "critical", 0f);

        if (damage > 0)
        {
            result.StatChanges["damage"] = damage;
        }

        _logger.LogDebug("Player {PlayerName} equipped weapon {WeaponName} (+{Damage} damage)",
            player.PlayerName, weapon.ItemName, damage);

        return result;
    }

    private EquipResult EquipArmor(RealTimePlayer player, LootItem armor)
    {
        var result = new EquipResult { Success = true, EquippedItem = armor };

        // Armor provides defense bonuses
        var defense = GetPropertyValue<int>(armor, "defense", 0);
        var healthBonus = GetPropertyValue<int>(armor, "health", 0);

        if (defense > 0)
        {
            result.StatChanges["defense"] = defense;
        }

        if (healthBonus > 0)
        {
            player.MaxHealth += healthBonus;
            player.Health = Math.Min(player.Health + healthBonus, player.MaxHealth);
            result.StatChanges["health"] = healthBonus;
        }

        _logger.LogDebug("Player {PlayerName} equipped armor {ArmorName} (+{Defense} defense, +{Health} health)",
            player.PlayerName, armor.ItemName, defense, healthBonus);

        return result;
    }

    private Dictionary<string, int> CombineLootByType()
    {
        var combined = new Dictionary<string, int>();

        foreach (var worldStats in _worldLootStats.Values)
        {
            foreach (var (type, count) in worldStats.LootByType)
            {
                if (!combined.ContainsKey(type))
                    combined[type] = 0;
                combined[type] += count;
            }
        }

        return combined;
    }

    private Dictionary<int, int> CombineLootByRarity()
    {
        var combined = new Dictionary<int, int>();

        foreach (var worldStats in _worldLootStats.Values)
        {
            foreach (var (rarity, count) in worldStats.LootByRarity)
            {
                if (!combined.ContainsKey(rarity))
                    combined[rarity] = 0;
                combined[rarity] += count;
            }
        }

        return combined;
    }

    // =============================================
    // DISPOSAL
    // =============================================

    public void Dispose()
    {
        _lootTables.Clear();
        _worldLootStats.Clear();
        _roomLastSpawn.Clear();

        _logger.LogInformation("LootSystem disposed");
    }
}