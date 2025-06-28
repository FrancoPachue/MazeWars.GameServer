using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Loot.Interface;
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

    // Events
    public event Action<string, LootUpdate>? OnLootSpawned;
    public event Action<string, LootUpdate>? OnLootTaken;
    public event Action<string, LootUpdate>? OnLootRemoved;
    public event Action<RealTimePlayer, LootItem, ItemUseResult>? OnItemUsed;

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
                        Position = loot.Position
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

        return new LootItem
        {
            LootId = Guid.NewGuid().ToString(),
            ItemName = drop.ItemName,
            ItemType = drop.ItemType,
            Rarity = drop.Rarity,
            Position = position,
            RoomId = room.RoomId,
            SpawnedAt = DateTime.UtcNow,
            Properties = new Dictionary<string, object>(drop.Properties)
        };
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

        // Check for timed respawns
        if ((currentTime - world.LastLootSpawn).TotalSeconds > _lootSettings.LootRespawnIntervalSeconds)
        {
            SpawnRandomLoot(world);
            world.LastLootSpawn = currentTime;
        }

        // Process dynamic spawning
        ProcessDynamicLootSpawning(world);

        // Cleanup expired loot
        CleanupExpiredLoot(world);

        // Manage loot density
        ManageLootDensity(world);
    }

    private void ProcessDynamicLootSpawning(GameWorld world)
    {
        // Handle dead mobs
        var deadMobs = world.Mobs.Values
            .Where(m => m.Health <= 0 && m.State != "dead")
            .ToList();

        foreach (var mob in deadMobs)
        {
            ProcessMobDeathLoot(mob, world);
            mob.State = "dead"; // Mark as processed
        }
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

        if (player.Inventory.Count >= _settings.GameBalance.MaxInventorySize)
        {
            result.ErrorMessage = "Inventory full";
            result.InventoryFull = true;
            return result;
        }

        // Successfully grab the loot
        if (world.AvailableLoot.TryRemove(lootId, out var grabbedLoot))
        {
            player.Inventory.Add(grabbedLoot);
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
                TakenBy = player.PlayerId
            };

            OnLootTaken?.Invoke(world.WorldId, lootUpdate);

            _logger.LogInformation("Player {PlayerName} grabbed {ItemName} (rarity {Rarity})",
                player.PlayerName, grabbedLoot.ItemName, grabbedLoot.Rarity);
        }

        return result;
    }

    public List<LootItem> DropPlayerLoot(RealTimePlayer deadPlayer, GameWorld world, int maxItemsToDrop = 3)
    {
        var droppedItems = new List<LootItem>();
        var itemsToDrop = deadPlayer.Inventory.Take(maxItemsToDrop).ToList();

        foreach (var item in itemsToDrop)
        {
            var lootId = Guid.NewGuid().ToString();
            var droppedLoot = new LootItem
            {
                LootId = lootId,
                ItemName = item.ItemName,
                ItemType = item.ItemType,
                Rarity = item.Rarity,
                Properties = new Dictionary<string, object>(item.Properties),
                Position = deadPlayer.Position + new Vector2(
                    (float)(_random.NextDouble() - 0.5) * 4,
                    (float)(_random.NextDouble() - 0.5) * 4
                ),
                RoomId = deadPlayer.CurrentRoomId,
                SpawnedAt = DateTime.UtcNow
            };

            world.AvailableLoot[lootId] = droppedLoot;
            deadPlayer.Inventory.Remove(item);
            droppedItems.Add(droppedLoot);

            // Fire event
            var lootUpdate = new LootUpdate
            {
                UpdateType = "spawned",
                LootId = droppedLoot.LootId,
                ItemName = droppedLoot.ItemName,
                Position = droppedLoot.Position
            };

            OnLootSpawned?.Invoke(world.WorldId, lootUpdate);
        }

        _logger.LogInformation("Player {PlayerName} dropped {Count} items on death",
            deadPlayer.PlayerName, droppedItems.Count);

        return droppedItems;
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
                result.Message = keyResult.Success ? $"Used key: {item.ItemName}" : keyResult.ErrorMessage;
                result.ItemConsumed = keyResult.Success;
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

        // Inventory space check
        if (player.Inventory.Count >= _settings.GameBalance.MaxInventorySize)
            return false;

        return true;
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
        var luckValue = 0f;

        if (player.Stats.TryGetValue("luck", out var luck))
        {
            luckValue = luck * _lootSettings.LuckStatMultiplier;
        }

        // Class-based luck modifiers
        var classModifier = player.PlayerClass.ToLower() switch
        {
            "scout" => 0.1f, // Scouts are naturally luckier
            "support" => 0.05f,
            "tank" => 0f,
            _ => 0f
        };

        return 1f + luckValue + classModifier;
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
        var tableId = deadMob.MobType.ToLower() switch
        {
            "boss" => "key_drops",
            "guard" => "weapon_drops",
            _ => "weapon_drops"
        };

        if (!_lootTables.TryGetValue(tableId, out var lootTable))
        {
            _logger.LogWarning("Loot table {TableId} not found for mob type {MobType}",
                tableId, deadMob.MobType);
            return;
        }

        var room = world.Rooms.Values.FirstOrDefault(r => r.RoomId == deadMob.RoomId);
        if (room == null)
        {
            _logger.LogWarning("Room {RoomId} not found for mob death loot", deadMob.RoomId);
            return;
        }

        // Apply luck modifier if there's a killer
        var spawnedLoot = new List<LootItem>();
        var maxDrops = Math.Min(_lootSettings.MaxDropsPerMob, lootTable.PossibleDrops.Count);

        for (int i = 0; i < maxDrops; i++)
        {
            var eligibleDrops = lootTable.PossibleDrops
                .Where(drop => ShouldDropOccur(drop, killer))
                .ToList();

            if (eligibleDrops.Any())
            {
                var selectedDrop = eligibleDrops[_random.Next(eligibleDrops.Count)];
                var loot = CreateLootItem(selectedDrop, room, deadMob.Position);

                world.AvailableLoot[loot.LootId] = loot;
                room.SpawnedLootIds.Add(loot.LootId);
                world.TotalLootSpawned++;
                spawnedLoot.Add(loot);

                // Fire event
                var lootUpdate = new LootUpdate
                {
                    UpdateType = "spawned",
                    LootId = loot.LootId,
                    ItemName = loot.ItemName,
                    Position = loot.Position
                };

                OnLootSpawned?.Invoke(world.WorldId, lootUpdate);
            }
        }

        if (spawnedLoot.Any())
        {
            _logger.LogDebug("Mob {MobId} death spawned {Count} loot items",
                deadMob.MobId, spawnedLoot.Count);
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
        if (!_lootTables.TryGetValue("key_drops", out var lootTable))
        {
            _logger.LogWarning("Key drops table not found for boss death");
            return;
        }

        var room = world.Rooms.Values.FirstOrDefault(r => r.RoomId == bossMob.RoomId);
        if (room == null) return;

        // Boss always drops something good
        var guaranteedDrops = lootTable.PossibleDrops.Where(d => d.Rarity >= 3).ToList();
        if (guaranteedDrops.Any())
        {
            var selectedDrop = guaranteedDrops[_random.Next(guaranteedDrops.Count)];
            var loot = CreateLootItem(selectedDrop, room, bossMob.Position);

            world.AvailableLoot[loot.LootId] = loot;
            room.SpawnedLootIds.Add(loot.LootId);
            world.TotalLootSpawned++;

            var lootUpdate = new LootUpdate
            {
                UpdateType = "spawned",
                LootId = loot.LootId,
                ItemName = loot.ItemName,
                Position = loot.Position
            };

            OnLootSpawned?.Invoke(world.WorldId, lootUpdate);

            _logger.LogInformation("Boss {MobId} killed by {PlayerName} dropped {ItemName}",
                bossMob.MobId, killer.PlayerName, loot.ItemName);
        }
    }

    // =============================================
    // LOOT CLEANUP AND OPTIMIZATION
    // =============================================

    public void CleanupExpiredLoot(GameWorld world)
    {
        var currentTime = DateTime.UtcNow;
        var expiredLoot = world.AvailableLoot.Values
            .Where(loot => (currentTime - loot.SpawnedAt).TotalMinutes > _lootSettings.LootExpirationTimeMinutes)
            .ToList();

        foreach (var loot in expiredLoot)
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
                    Position = loot.Position
                };

                OnLootRemoved?.Invoke(world.WorldId, lootUpdate);
            }
        }

        if (expiredLoot.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired loot items in world {WorldId}",
                expiredLoot.Count, world.WorldId);
        }
    }

    public void ManageLootDensity(GameWorld world)
    {
        foreach (var room in world.Rooms.Values)
        {
            var lootInRoom = world.AvailableLoot.Values
                .Where(loot => loot.RoomId == room.RoomId)
                .ToList();

            if (lootInRoom.Count > _lootSettings.MaxLootPerRoom)
            {
                // Remove oldest loot items first
                var oldestLoot = lootInRoom
                    .OrderBy(loot => loot.SpawnedAt)
                    .Take(lootInRoom.Count - _lootSettings.MaxLootPerRoom)
                    .ToList();

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
                            Position = loot.Position
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

        // Basic key usage - can be expanded for doors, chests, etc.
        switch (key.ItemName.ToLower())
        {
            case "silver key":
            case "gold key":
            case "master key":
                result.Success = true;
                result.UnlockedDoor = $"Door unlocked with {key.ItemName}";
                _logger.LogInformation("Player {PlayerName} used key: {KeyName}",
                    player.PlayerName, key.ItemName);
                break;

            default:
                result.ErrorMessage = $"Unknown key type: {key.ItemName}";
                break;
        }

        return result;
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
        var totalSpawned = _worldLootStats.Values.Sum(s => s.TotalLootSpawned);
        var totalTaken = _worldLootStats.Values.Sum(s => s.TotalLootTaken);
        var totalExpired = _worldLootStats.Values.Sum(s => s.TotalLootExpired);
        var currentActive = _worldLootStats.Values.Sum(s => s.CurrentActiveLoot);

        var pickupRate = totalSpawned > 0 ? (double)totalTaken / totalSpawned : 0;
        var expirationRate = totalSpawned > 0 ? (double)totalExpired / totalSpawned : 0;

        return new Dictionary<string, object>
        {
            ["TotalLootSpawned"] = totalSpawned,
            ["TotalLootTaken"] = totalTaken,
            ["TotalLootExpired"] = totalExpired,
            ["CurrentActiveLoot"] = currentActive,
            ["PickupRate"] = pickupRate,
            ["ExpirationRate"] = expirationRate,
            ["ActiveWorlds"] = _worldLootStats.Count,
            ["LootByType"] = CombineLootByType(),
            ["LootByRarity"] = CombineLootByRarity(),
            ["WorldStats"] = _worldLootStats.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    kvp.Value.TotalLootSpawned,
                    kvp.Value.TotalLootTaken,
                    kvp.Value.CurrentActiveLoot,
                    kvp.Value.MostPopularItem,
                    kvp.Value.RarestItemDropped
                }
            )
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