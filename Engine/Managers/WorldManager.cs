using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine.Equipment.Data;
using MazeWars.GameServer.Engine.Equipment.Models;
using MazeWars.GameServer.Engine.Loot.Interface;
using MazeWars.GameServer.Engine.Loot.Models;
using MazeWars.GameServer.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MazeWars.GameServer.Engine.Managers;

/// <summary>
/// ⭐ REFACTORED: Manages active game worlds, world generation, and world lifecycle.
/// Extracted from GameEngine to reduce complexity and improve maintainability.
/// </summary>
public class WorldManager
{
    private readonly ILogger<WorldManager> _logger;
    private readonly GameServerSettings _settings;
    private readonly ILootSystem _lootSystem;
    private readonly Dictionary<string, GameWorld> _worlds = new();
    private readonly Dictionary<string, LootTable> _baseLootTables = new();
    private readonly object _worldsLock = new object();
    private readonly Random _random = new();

    public WorldManager(
        ILogger<WorldManager> _logger,
        IOptions<GameServerSettings> settings,
        ILootSystem lootSystem)
    {
        this._logger = _logger;
        _settings = settings.Value;
        _lootSystem = lootSystem;

        // Initialize loot tables and pass to LootSystem
        InitializeLootTables();
        _lootSystem.InitializeLootTables(_baseLootTables);

        _logger.LogInformation("WorldManager initialized with {WorldSizeX}x{WorldSizeY} grid",
            _settings.WorldGeneration.WorldSizeX, _settings.WorldGeneration.WorldSizeY);
    }

    // =============================================
    // WORLD GENERATION
    // =============================================

    /// <summary>
    /// Create a new game world with rooms, extraction points, and initial loot.
    /// Accepts an optional GameModeConfig to override grid size and loot count.
    /// </summary>
    public GameWorld CreateWorld(string worldId, Dictionary<string, RealTimePlayer> lobbyPlayers, string gameMode = "trios", GameModeConfig? modeConfig = null)
    {
        lock (_worldsLock)
        {
            var gridX = modeConfig?.GridSizeX ?? _settings.WorldGeneration.WorldSizeX;
            var gridY = modeConfig?.GridSizeY ?? _settings.WorldGeneration.WorldSizeY;

            var world = new GameWorld
            {
                WorldId = worldId,
                GameMode = gameMode,
                ModeConfig = modeConfig,
                Rooms = GenerateWorldRooms(gridX, gridY),
                ExtractionPoints = GenerateExtractionPoints(gridX, gridY),
                LootTables = new List<LootTable>(_baseLootTables.Values),
                CreatedAt = DateTime.UtcNow,
                Players = new ConcurrentDictionary<string, RealTimePlayer>(lobbyPlayers)
            };

            GenerateLockedDoors(world);
            GenerateDoors(world);
            GenerateRevivalAltars(world);
            SpawnInitialLoot(world);

            _worlds[worldId] = world;

            _logger.LogInformation("Created world {WorldId} with {RoomCount} rooms, {PlayerCount} players",
                worldId, world.Rooms.Count, world.Players.Count);

            return world;
        }
    }

    /// <summary>
    /// Generate rooms in a grid layout with connections.
    /// </summary>
    private Dictionary<string, Room> GenerateWorldRooms(int gridSizeX = 0, int gridSizeY = 0)
    {
        var rooms = new Dictionary<string, Room>();
        var settings = _settings.WorldGeneration;
        var sizeX = gridSizeX > 0 ? gridSizeX : settings.WorldSizeX;
        var sizeY = gridSizeY > 0 ? gridSizeY : settings.WorldSizeY;
        var maxX = sizeX - 1;
        var maxY = sizeY - 1;

        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                var roomType = AssignRoomType(x, y, maxX, maxY);

                // Skip empty rooms entirely (corners) — avoids invisible collision bounds
                if (roomType == "empty") continue;

                var roomId = $"room_{x}_{y}";
                var roomSize = CalculateRoomSize(roomType, settings);

                // Apply position jitter to non-edge rooms
                var jitterX = 0f;
                var jitterY = 0f;
                bool isEdge = x == 0 || x == maxX || y == 0 || y == maxY;
                if (!isEdge)
                {
                    jitterX = ((float)_random.NextDouble() - 0.5f) * 2f * settings.RoomPositionJitter;
                    jitterY = ((float)_random.NextDouble() - 0.5f) * 2f * settings.RoomPositionJitter;
                }

                var room = new Room
                {
                    RoomId = roomId,
                    Position = new Vector2(
                        x * settings.RoomSpacing + jitterX,
                        y * settings.RoomSpacing + jitterY),
                    Size = roomSize,
                    Connections = new List<string>(),
                    RoomType = roomType
                };

                // Add connections only to adjacent rooms that exist (not empty corners)
                if (x > 0 && !IsEmptyPosition(x - 1, y, maxX, maxY))
                    room.Connections.Add($"room_{x - 1}_{y}");
                if (x < maxX && !IsEmptyPosition(x + 1, y, maxX, maxY))
                    room.Connections.Add($"room_{x + 1}_{y}");
                if (y > 0 && !IsEmptyPosition(x, y - 1, maxX, maxY))
                    room.Connections.Add($"room_{x}_{y - 1}");
                if (y < maxY && !IsEmptyPosition(x, y + 1, maxX, maxY))
                    room.Connections.Add($"room_{x}_{y + 1}");

                rooms[roomId] = room;
            }
        }

        _logger.LogDebug("Generated {RoomCount} rooms for world with variable sizes", rooms.Count);
        return rooms;
    }

    /// <summary>
    /// Calculate room size based on room type. Boss arenas are large, patrols are small/medium.
    /// Returns a slightly non-square size for visual variety.
    /// </summary>
    private Vector2 CalculateRoomSize(string roomType, WorldGeneration settings)
    {
        float minSize, maxSize;

        switch (roomType)
        {
            case "spawn":
            case "extraction_zone":
                // Small starting/extraction cells
                minSize = settings.RoomSizeSmallMin;
                maxSize = settings.RoomSizeSmallMin + 4;
                break;
            case "patrol":
            case "guard_post":
                if (_random.NextDouble() < 0.5)
                {
                    minSize = settings.RoomSizeSmallMin;
                    maxSize = settings.RoomSizeSmallMax;
                }
                else
                {
                    minSize = settings.RoomSizeMediumMin;
                    maxSize = settings.RoomSizeMediumMax;
                }
                break;
            case "ambush":
                minSize = settings.RoomSizeMediumMin;
                maxSize = settings.RoomSizeMediumMax;
                break;
            case "elite_chamber":
            case "treasure_vault":
                if (_random.NextDouble() < 0.3)
                {
                    minSize = settings.RoomSizeMediumMin;
                    maxSize = settings.RoomSizeMediumMax;
                }
                else
                {
                    minSize = settings.RoomSizeLargeMin;
                    maxSize = settings.RoomSizeLargeMax;
                }
                break;
            case "boss_arena":
                minSize = settings.RoomSizeLargeMin;
                maxSize = settings.RoomSizeLargeMax;
                break;
            default:
                minSize = settings.RoomSizeMediumMin;
                maxSize = settings.RoomSizeMediumMax;
                break;
        }

        var baseSize = minSize + (float)_random.NextDouble() * (maxSize - minSize);
        // Slight aspect ratio variation (±7.5%) so rooms aren't perfect squares
        var aspectVariation = 1f + ((float)_random.NextDouble() - 0.5f) * 0.15f;

        return new Vector2(baseSize, baseSize * aspectVariation);
    }

    /// <summary>
    /// Generate locked doors on connections to boss_arena and treasure_vault rooms.
    /// </summary>
    private void GenerateLockedDoors(GameWorld world)
    {
        var processed = new HashSet<string>();

        foreach (var room in world.Rooms.Values)
        {
            foreach (var connId in room.Connections)
            {
                if (!world.Rooms.TryGetValue(connId, out var neighbor)) continue;

                var connectionId = string.Compare(room.RoomId, connId, StringComparison.Ordinal) < 0
                    ? $"{room.RoomId}_{connId}" : $"{connId}_{room.RoomId}";
                if (processed.Contains(connectionId)) continue;
                processed.Add(connectionId);

                string? keyType = null;

                // Boss arena connections always require gold key
                if (room.RoomType == "boss_arena" || neighbor.RoomType == "boss_arena")
                    keyType = "gold";
                // Treasure vault connections have 50% chance of silver lock
                else if ((room.RoomType == "treasure_vault" || neighbor.RoomType == "treasure_vault")
                         && _random.NextDouble() < 0.5)
                    keyType = "silver";

                if (keyType != null)
                {
                    world.LockedDoors[connectionId] = new LockedDoor
                    {
                        ConnectionId = connectionId,
                        RoomIdA = string.Compare(room.RoomId, connId, StringComparison.Ordinal) < 0 ? room.RoomId : connId,
                        RoomIdB = string.Compare(room.RoomId, connId, StringComparison.Ordinal) < 0 ? connId : room.RoomId,
                        RequiredKeyType = keyType
                    };
                }
            }
        }

        _logger.LogInformation("Generated {DoorCount} locked doors for world {WorldId}",
            world.LockedDoors.Count, world.WorldId);
    }

    /// <summary>
    /// Generate a RoomDoor for every room connection. Doors start closed and require
    /// channeling to open (Dark and Darker style). Doors overlapping with LockedDoors
    /// are marked IsLocked = true so they cannot be channeled open without a key.
    /// </summary>
    private void GenerateDoors(GameWorld world)
    {
        var processed = new HashSet<string>();

        foreach (var room in world.Rooms.Values)
        {
            foreach (var connId in room.Connections)
            {
                if (!world.Rooms.TryGetValue(connId, out var neighbor)) continue;

                var connectionId = string.Compare(room.RoomId, connId, StringComparison.Ordinal) < 0
                    ? $"{room.RoomId}_{connId}" : $"{connId}_{room.RoomId}";

                if (!processed.Add(connectionId)) continue;

                // Spawn room doors start open so players can leave spawn
                bool isSpawnDoor = room.RoomType == "spawn" || neighbor.RoomType == "spawn";

                var door = new RoomDoor
                {
                    DoorId = connectionId,
                    RoomIdA = string.Compare(room.RoomId, connId, StringComparison.Ordinal) < 0 ? room.RoomId : connId,
                    RoomIdB = string.Compare(room.RoomId, connId, StringComparison.Ordinal) < 0 ? connId : room.RoomId,
                    IsOpen = isSpawnDoor,
                    IsLocked = isSpawnDoor ? false : world.LockedDoors.ContainsKey(connectionId),
                };

                world.Doors[connectionId] = door;
            }
        }

        _logger.LogInformation("Generated {DoorCount} doors ({LockedCount} locked) for world {WorldId}",
            world.Doors.Count, world.Doors.Values.Count(d => d.IsLocked), world.WorldId);
    }

    /// <summary>
    /// Generate Revival Altars in eligible rooms. Places 1 altar per 3 rooms,
    /// skipping boss arenas and spawn rooms.
    /// </summary>
    private void GenerateRevivalAltars(GameWorld world)
    {
        var roomCounter = 0;
        var altarCount = 0;

        foreach (var room in world.Rooms.Values)
        {
            // Skip boss rooms, spawn rooms, and empty rooms
            if (room.RoomType is "boss_arena" or "spawn" or "empty" or "extraction_zone")
                continue;

            roomCounter++;

            // Place an altar every 3 eligible rooms
            if (roomCounter % 3 != 0)
                continue;

            // Altar position: room center + small random offset
            var offsetX = ((float)_random.NextDouble() - 0.5f) * room.Size.X * 0.3f;
            var offsetY = ((float)_random.NextDouble() - 0.5f) * room.Size.Y * 0.3f;

            var altarId = $"altar_{room.RoomId}";
            var altar = new RevivalAltar
            {
                AltarId = altarId,
                Position = new Vector2(room.Position.X + offsetX, room.Position.Y + offsetY),
                RoomId = room.RoomId,
                IsActive = true
            };

            world.RevivalAltars[altarId] = altar;
            altarCount++;
        }

        _logger.LogInformation("Generated {AltarCount} revival altars for world {WorldId}",
            altarCount, world.WorldId);
    }

    /// <summary>
    /// Check if a grid position is an empty room that should be skipped.
    /// Corners are now extraction_zone rooms and are NOT empty.
    /// </summary>
    private static bool IsEmptyPosition(int x, int y, int maxX, int maxY)
    {
        return false; // No positions are skipped — corners are now extraction_zone rooms
    }

    /// <summary>
    /// Assign room type based on grid position with randomization for variety.
    /// Center cluster is dangerous (bosses + elites), edges are safer.
    /// </summary>
    private string AssignRoomType(int x, int y, int maxX, int maxY)
    {
        bool isCorner = (x == 0 || x == maxX) && (y == 0 || y == maxY);
        if (isCorner) return "extraction_zone";

        // Team spawn rooms: small safe cells with no mobs
        bool isTeamSpawn = (x == 1 && y == 0) || (x == maxX - 1 && y == 0) ||
                           (x == 1 && y == maxY) || (x == maxX - 1 && y == maxY);
        if (isTeamSpawn) return "spawn";

        bool isEdge = x == 0 || x == maxX || y == 0 || y == maxY;
        int centerX = maxX / 2;
        int centerY = maxY / 2;
        bool isCenter = x == centerX && y == centerY;
        int distFromCenter = Math.Abs(x - centerX) + Math.Abs(y - centerY);

        // Center room is always boss_arena
        if (isCenter) return "boss_arena";

        // Rooms directly adjacent to center (distance 1, non-edge)
        if (distFromCenter == 1 && !isEdge)
        {
            // One adjacent room is a second boss_arena, rest elite/treasure
            if (x == centerX && y == centerY - 1) return "boss_arena";
            if (x == centerX + 1 && y == centerY) return "elite_chamber";
            if (x == centerX && y == centerY + 1) return "elite_chamber";
            return "treasure_vault";
        }

        // Distance 2 from center, non-edge: mix of elite/treasure/ambush
        if (distFromCenter == 2 && !isEdge)
        {
            var roll = _random.NextDouble();
            if (roll < 0.35) return "elite_chamber";
            if (roll < 0.65) return "treasure_vault";
            return "ambush";
        }

        // Edge rooms (non-corner): patrol/guard/ambush
        if (isEdge)
        {
            var roll = _random.NextDouble();
            if (roll < 0.35) return "patrol";
            if (roll < 0.65) return "guard_post";
            return "ambush";
        }

        // Remaining inner rooms
        var innerRoll = _random.NextDouble();
        if (innerRoll < 0.30) return "ambush";
        if (innerRoll < 0.55) return "guard_post";
        if (innerRoll < 0.75) return "elite_chamber";
        return "treasure_vault";
    }

    /// <summary>
    /// Create a small lobby arena world (2x2 rooms) for warmup before game starts.
    /// Uses the same worldId as the lobbyId so broadcast infrastructure works seamlessly.
    /// </summary>
    public GameWorld CreateLobbyWorld(string lobbyId)
    {
        lock (_worldsLock)
        {
            var rooms = GenerateLobbyArenaRooms();

            var world = new GameWorld
            {
                WorldId = lobbyId,
                Rooms = rooms,
                ExtractionPoints = new Dictionary<string, ExtractionPoint>(),
                LootTables = new List<LootTable>(),
                CreatedAt = DateTime.UtcNow,
                IsLobbyWorld = true
            };

            _worlds[lobbyId] = world;

            _logger.LogInformation("Created lobby arena world {WorldId} with {RoomCount} rooms",
                lobbyId, rooms.Count);

            return world;
        }
    }

    /// <summary>
    /// Generate a single room for the lobby arena.
    /// One 60x60 room centered at (75, 75).
    /// </summary>
    private Dictionary<string, Room> GenerateLobbyArenaRooms()
    {
        var rooms = new Dictionary<string, Room>
        {
            ["lobby_arena"] = new Room
            {
                RoomId = "lobby_arena",
                Position = new Vector2(75, 75),
                Size = new Vector2(60, 60),
                Connections = new List<string>()
            }
        };

        _logger.LogDebug("Generated lobby arena room (60x60 at center 75,75)");
        return rooms;
    }

    /// <summary>
    /// Get spawn position for a team in the lobby arena.
    /// All teams spawn in the same room, slightly offset.
    /// </summary>
    public Vector2 GetLobbySpawnPosition(string teamId)
    {
        return teamId.ToLower() switch
        {
            "team1" => new Vector2(65, 65),
            "team2" => new Vector2(85, 65),
            "team3" => new Vector2(65, 85),
            "team4" => new Vector2(85, 85),
            _ => new Vector2(75, 75)
        };
    }

    /// <summary>
    /// Generate extraction points in the corners of the world.
    /// </summary>
    private Dictionary<string, ExtractionPoint> GenerateExtractionPoints(int gridSizeX = 0, int gridSizeY = 0)
    {
        var extractionPoints = new Dictionary<string, ExtractionPoint>();
        var settings = _settings.WorldGeneration;
        var sizeX = gridSizeX > 0 ? gridSizeX : settings.WorldSizeX;
        var sizeY = gridSizeY > 0 ? gridSizeY : settings.WorldSizeY;

        var centerX = (sizeX - 1) / 2;
        var centerY = (sizeY - 1) / 2;

        var maxXIdx = sizeX - 1;
        var maxYIdx = sizeY - 1;

        var cornerPositions = new[]
        {
            new { Id = "extract_0_0", Position = new Vector2(0, 0), RoomId = "room_0_0" },
            new { Id = $"extract_{maxXIdx}_0", Position = new Vector2(maxXIdx * settings.RoomSpacing, 0), RoomId = $"room_{maxXIdx}_0" },
            new { Id = $"extract_0_{maxYIdx}", Position = new Vector2(0, maxYIdx * settings.RoomSpacing), RoomId = $"room_0_{maxYIdx}" },
            new { Id = $"extract_{maxXIdx}_{maxYIdx}", Position = new Vector2(maxXIdx * settings.RoomSpacing, maxYIdx * settings.RoomSpacing), RoomId = $"room_{maxXIdx}_{maxYIdx}" },
            new { Id = "extract_center", Position = new Vector2(centerX * settings.RoomSpacing, centerY * settings.RoomSpacing), RoomId = $"room_{centerX}_{centerY}" }
        };

        foreach (var corner in cornerPositions)
        {
            extractionPoints[corner.Id] = new ExtractionPoint
            {
                ExtractionId = corner.Id,
                Position = corner.Position,
                RoomId = corner.RoomId,
                IsActive = true, // Extraction always available from game start
                ExtractionTimeSeconds = _settings.GameBalance.ExtractionTimeSeconds
            };
        }

        _logger.LogDebug("Generated {Count} extraction points", extractionPoints.Count);
        return extractionPoints;
    }

    /// <summary>
    /// Spawn treasure chests in rooms. Each non-empty room gets 1-2 chests
    /// with items rolled using the probabilistic rarity system.
    /// </summary>
    private void SpawnInitialLoot(GameWorld world)
    {
        var chestCount = 0;
        var allEquipIds = EquipmentRegistry.GetAllBaseIds();

        foreach (var room in world.Rooms.Values)
        {
            if (room.RoomType is "empty" or "extraction_zone" or "spawn") continue;

            // Number of chests based on room type
            var numChests = room.RoomType switch
            {
                "patrol" => 1,
                "guard_post" => 1,
                "ambush" => 1,
                "elite_chamber" => 2,
                "boss_arena" => 2,
                "treasure_vault" => 3,
                _ => 1
            };

            for (int c = 0; c < numChests; c++)
            {
                // Position chest within room
                var offsetX = (float)(_random.NextDouble() - 0.5) * room.Size.X * 0.6f;
                var offsetY = (float)(_random.NextDouble() - 0.5) * room.Size.Y * 0.6f;
                var chestPos = new Vector2(room.Position.X + offsetX, room.Position.Y + offsetY);

                var container = new LootContainer
                {
                    ContainerId = $"chest_{room.RoomId}_{c}",
                    ContainerType = "chest",
                    Position = chestPos,
                    RoomId = room.RoomId,
                    DespawnAfterSeconds = 0, // Permanent
                    DisplayName = room.RoomType == "treasure_vault" ? "Treasure Chest" : "Chest"
                };

                // 2-4 items per chest depending on room type
                var itemCount = room.RoomType switch
                {
                    "treasure_vault" => _random.Next(3, 5),
                    "boss_arena" => _random.Next(2, 4),
                    "elite_chamber" => _random.Next(2, 4),
                    _ => _random.Next(1, 3)
                };

                for (int i = 0; i < itemCount; i++)
                {
                    // In treasure_vault and boss_arena: 15% chance for valuable items
                    if ((room.RoomType is "treasure_vault" or "boss_arena") && _random.NextDouble() < 0.15)
                    {
                        var valuable = _lootSystem.CreateValuableItem(room, chestPos);
                        container.Contents.Add(valuable);
                        continue;
                    }

                    // 70% equipment, 30% consumable
                    if (_random.NextDouble() < 0.7 && allEquipIds.Count > 0)
                    {
                        var eqId = allEquipIds[_random.Next(allEquipIds.Count)];
                        var equipDef = EquipmentRegistry.Get(eqId);
                        if (equipDef == null) continue;

                        var rarity = ItemRaritySystem.RollRarity(_random, room.RoomType, "patrol");
                        var qualityTier = ItemRaritySystem.RollQualityTier(_random);
                        var qualityName = (int)qualityTier > 0 ? $"{ItemRaritySystem.GetQualityName((int)qualityTier)} " : "";
                        var rarityName = (int)rarity > 0 ? $"{ItemRaritySystem.GetRarityName((int)rarity)} " : "";

                        container.Contents.Add(new LootItem
                        {
                            LootId = Guid.NewGuid().ToString(),
                            ItemName = $"{qualityName}{rarityName}{equipDef.DisplayName}",
                            ItemType = equipDef.Slot is EquipmentSlot.Weapon ? "weapon" : "armor",
                            Rarity = (int)rarity,
                            Position = chestPos,
                            RoomId = room.RoomId,
                            SpawnedAt = DateTime.UtcNow,
                            Properties = new Dictionary<string, object>
                            {
                                ["equipment_id"] = eqId,
                                ["rarity"] = (int)rarity,
                                ["quality"] = (int)qualityTier
                            }
                        });
                    }
                    else
                    {
                        var roll = _random.NextDouble();
                        var (potionName, props) = roll switch
                        {
                            < 0.30 => ("Health Potion", new Dictionary<string, object> { ["heal"] = 50 }),
                            < 0.55 => ("Mana Potion", new Dictionary<string, object> { ["mana"] = 40 }),
                            < 0.70 => ("Speed Elixir", new Dictionary<string, object> { ["speed_boost"] = 30, ["duration"] = 8 }),
                            < 0.80 => ("Shield Potion", new Dictionary<string, object> { ["shield_amount"] = 30, ["duration"] = 15 }),
                            < 0.90 => ("Antidote", new Dictionary<string, object> { ["cleanse"] = 1 }),
                            _ => ("Strength Tonic", new Dictionary<string, object> { ["damage_boost"] = 25, ["duration"] = 10 }),
                        };
                        container.Contents.Add(new LootItem
                        {
                            LootId = Guid.NewGuid().ToString(),
                            ItemName = potionName,
                            ItemType = "consumable",
                            Rarity = 0,
                            Position = chestPos,
                            RoomId = room.RoomId,
                            SpawnedAt = DateTime.UtcNow,
                            Properties = props
                        });
                    }
                }

                world.LootContainers[container.ContainerId] = container;
                chestCount++;
            }
        }

        _logger.LogInformation("Spawned {Count} chests across rooms in world {WorldId}",
            chestCount, world.WorldId);
    }

    /// <summary>
    /// Initialize base loot tables from configuration.
    /// </summary>
    /// <summary>
    /// Helper to create an equipment LootDrop. Rarity is rolled at drop time, not predefined.
    /// </summary>
    private static LootDrop EquipDrop(string name, string eqId, string type, float chance) => new()
    {
        ItemName = name,
        ItemType = type,
        Rarity = 0, // Base rarity — actual rarity rolled at drop time via ItemRaritySystem
        DropChance = chance,
        Properties = new() { ["equipment_id"] = eqId }
    };

    private void InitializeLootTables()
    {
        // ── Single equipment pool: all gear types, rarity determined at drop time ──
        _baseLootTables["equipment"] = new LootTable
        {
            TableId = "equipment",
            PossibleDrops = new List<LootDrop>
            {
                // Weapons
                EquipDrop("Iron Sword", "iron_sword", "weapon", 0.10f),
                EquipDrop("Hunting Bow", "hunting_bow", "weapon", 0.10f),
                EquipDrop("Fire Staff", "fire_staff", "weapon", 0.08f),
                EquipDrop("Holy Staff", "holy_staff", "weapon", 0.08f),
                // Head
                EquipDrop("Plate Helmet", "plate_helmet", "armor", 0.06f),
                EquipDrop("Leather Hood", "leather_hood", "armor", 0.06f),
                EquipDrop("Cloth Hood", "cloth_hood", "armor", 0.06f),
                // Chest
                EquipDrop("Plate Chestplate", "plate_chest", "armor", 0.05f),
                EquipDrop("Leather Vest", "leather_vest", "armor", 0.06f),
                EquipDrop("Cloth Robe", "cloth_robe", "armor", 0.05f),
                // Boots
                EquipDrop("Plate Boots", "plate_boots", "armor", 0.05f),
                EquipDrop("Leather Boots", "leather_boots", "armor", 0.06f),
                EquipDrop("Cloth Sandals", "cloth_boots", "armor", 0.05f),
                // Offhand
                EquipDrop("Wooden Shield", "wooden_shield", "armor", 0.05f),
                EquipDrop("Torch", "torch", "armor", 0.05f),
                EquipDrop("Tome of Wisdom", "tome_of_wisdom", "armor", 0.04f),
                // Cape
                EquipDrop("Battle Banner", "battle_banner", "armor", 0.04f),
                EquipDrop("Traveler's Cloak", "traveler_cloak", "armor", 0.04f),
                EquipDrop("Scholar's Mantle", "scholars_mantle", "armor", 0.04f),
            }
        };

        // ── Consumable drops (room completion, patrol mobs) ──
        _baseLootTables["consumable_drops"] = new LootTable
        {
            TableId = "consumable_drops",
            PossibleDrops = new List<LootDrop>
            {
                new() { ItemName = "Health Potion", DropChance = 0.30f, Rarity = 0, ItemType = "consumable",
                    Properties = new Dictionary<string, object> { ["heal"] = 50 } },
                new() { ItemName = "Mana Potion", DropChance = 0.25f, Rarity = 0, ItemType = "consumable",
                    Properties = new Dictionary<string, object> { ["mana"] = 40 } },
                new() { ItemName = "Speed Elixir", DropChance = 0.15f, Rarity = 0, ItemType = "consumable",
                    Properties = new Dictionary<string, object> { ["speed_boost"] = 30, ["duration"] = 8 } },
                new() { ItemName = "Shield Potion", DropChance = 0.10f, Rarity = 0, ItemType = "consumable",
                    Properties = new Dictionary<string, object> { ["shield_amount"] = 30, ["duration"] = 15 } },
                new() { ItemName = "Antidote", DropChance = 0.10f, Rarity = 0, ItemType = "consumable",
                    Properties = new Dictionary<string, object> { ["cleanse"] = 1 } },
                new() { ItemName = "Strength Tonic", DropChance = 0.10f, Rarity = 0, ItemType = "consumable",
                    Properties = new Dictionary<string, object> { ["damage_boost"] = 25, ["duration"] = 10 } },
            }
        };

        // Legacy aliases all point to the single equipment pool
        _baseLootTables["common"] = _baseLootTables["equipment"];
        _baseLootTables["guard_drops"] = _baseLootTables["equipment"];
        _baseLootTables["elite_drops"] = _baseLootTables["equipment"];
        _baseLootTables["boss_drops"] = _baseLootTables["equipment"];
        _baseLootTables["rare"] = _baseLootTables["equipment"];
        _baseLootTables["weapon_drops"] = _baseLootTables["equipment"];
        _baseLootTables["key_drops"] = new LootTable
        {
            TableId = "key_drops",
            PossibleDrops = new List<LootDrop>
            {
                new() { ItemName = "Silver Key", ItemType = "key", DropChance = 0.7f,
                         Properties = new() { ["key_type"] = "silver" } },
                new() { ItemName = "Gold Key", ItemType = "key", DropChance = 0.3f,
                         Properties = new() { ["key_type"] = "gold" } }
            }
        };

        _logger.LogInformation("Initialized loot tables (probabilistic rarity system)");
    }

    // =============================================
    // WORLD ACCESS AND QUERIES
    // =============================================

    /// <summary>
    /// Get a world by ID.
    /// </summary>
    public GameWorld? GetWorld(string worldId)
    {
        lock (_worldsLock)
        {
            return _worlds.TryGetValue(worldId, out var world) ? world : null;
        }
    }

    /// <summary>
    /// Get all active worlds.
    /// </summary>
    public List<GameWorld> GetAllWorlds()
    {
        lock (_worldsLock)
        {
            return _worlds.Values.ToList();
        }
    }

    /// <summary>
    /// Get world IDs that have available space.
    /// </summary>
    public List<string> GetAvailableWorlds(int maxPlayersPerWorld)
    {
        lock (_worldsLock)
        {
            return _worlds.Values
                .Where(w => w.Players.Count < maxPlayersPerWorld)
                .Select(w => w.WorldId)
                .ToList();
        }
    }

    /// <summary>
    /// Find world containing a specific player.
    /// </summary>
    public GameWorld? FindWorldByPlayer(string playerId)
    {
        lock (_worldsLock)
        {
            return _worlds.Values.FirstOrDefault(w => w.Players.ContainsKey(playerId));
        }
    }

    /// <summary>
    /// Find world ID containing a specific player.
    /// </summary>
    public string? FindWorldIdByPlayer(string playerId)
    {
        lock (_worldsLock)
        {
            return _worlds.Values
                .FirstOrDefault(w => w.Players.ContainsKey(playerId))
                ?.WorldId;
        }
    }

    /// <summary>
    /// Find player across all worlds.
    /// </summary>
    public RealTimePlayer? FindPlayer(string playerId)
    {
        lock (_worldsLock)
        {
            foreach (var world in _worlds.Values)
            {
                if (world.Players.TryGetValue(playerId, out var player))
                {
                    return player;
                }
            }
            return null;
        }
    }

    // =============================================
    // WORLD MODIFICATION
    // =============================================

    /// <summary>
    /// Add a player to an existing world.
    /// </summary>
    public bool AddPlayerToWorld(string worldId, RealTimePlayer player)
    {
        lock (_worldsLock)
        {
            if (!_worlds.TryGetValue(worldId, out var world))
                return false;

            world.Players[player.PlayerId] = player;
            _logger.LogInformation("Added player {PlayerName} to world {WorldId}",
                player.PlayerName, worldId);
            return true;
        }
    }

    /// <summary>
    /// Remove a player from their world.
    /// </summary>
    public bool RemovePlayerFromWorld(string worldId, string playerId)
    {
        lock (_worldsLock)
        {
            if (!_worlds.TryGetValue(worldId, out var world))
                return false;

            if (world.Players.TryRemove(playerId, out var player))
            {
                _logger.LogInformation("Removed player {PlayerName} from world {WorldId}",
                    player.PlayerName, worldId);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Remove a world (e.g., when game ends).
    /// </summary>
    public bool RemoveWorld(string worldId)
    {
        lock (_worldsLock)
        {
            if (_worlds.Remove(worldId))
            {
                _logger.LogInformation("Removed world {WorldId}", worldId);
                return true;
            }
            return false;
        }
    }

    // =============================================
    // HELPER METHODS
    // =============================================

    /// <summary>
    /// Get spawn position based on team ID, using optional mode config for grid size.
    /// </summary>
    public Vector2 GetTeamSpawnPosition(string teamId, GameModeConfig? modeConfig = null)
    {
        var settings = _settings.WorldGeneration;
        var spacing = settings.RoomSpacing;
        var sizeX = modeConfig?.GridSizeX ?? settings.WorldSizeX;
        var sizeY = modeConfig?.GridSizeY ?? settings.WorldSizeY;
        var maxX = (sizeX - 1) * spacing;
        var maxY = (sizeY - 1) * spacing;

        return teamId.ToLower() switch
        {
            "team1" => new Vector2(spacing, 0),
            "team2" => new Vector2(maxX - spacing, 0),
            "team3" => new Vector2(spacing, maxY),
            "team4" => new Vector2(maxX - spacing, maxY),
            _ => new Vector2(maxX / 2, maxY / 2)
        };
    }

    /// <summary>
    /// Check if a room is in a corner position.
    /// </summary>
    public bool IsCornerRoom(string roomId, GameModeConfig? modeConfig = null)
    {
        var settings = _settings.WorldGeneration;
        var sizeX = modeConfig?.GridSizeX ?? settings.WorldSizeX;
        var sizeY = modeConfig?.GridSizeY ?? settings.WorldSizeY;
        var cornerRooms = new[]
        {
            "room_0_0",
            $"room_{sizeX - 1}_0",
            $"room_0_{sizeY - 1}",
            $"room_{sizeX - 1}_{sizeY - 1}"
        };

        return cornerRooms.Contains(roomId);
    }

    // =============================================
    // STATISTICS AND DIAGNOSTICS
    // =============================================

    /// <summary>
    /// Get world statistics for monitoring.
    /// </summary>
    public Dictionary<string, object> GetWorldStats()
    {
        lock (_worldsLock)
        {
            var totalWorlds = _worlds.Count;
            var totalPlayers = _worlds.Values.Sum(w => w.Players.Count);
            var totalMobs = _worlds.Values.Sum(w => w.Mobs.Count);

            return new Dictionary<string, object>
            {
                ["TotalWorlds"] = totalWorlds,
                ["TotalPlayers"] = totalPlayers,
                ["TotalMobs"] = totalMobs,
                ["AveragePlayersPerWorld"] = totalWorlds > 0 ? (double)totalPlayers / totalWorlds : 0,
                ["AverageMobsPerWorld"] = totalWorlds > 0 ? (double)totalMobs / totalWorlds : 0,
                ["WorldIds"] = _worlds.Keys.ToList()
            };
        }
    }

    /// <summary>
    /// Get detailed state of a specific world.
    /// </summary>
    public Dictionary<string, object>? GetWorldState(string worldId)
    {
        lock (_worldsLock)
        {
            if (!_worlds.TryGetValue(worldId, out var world))
                return null;

            return new Dictionary<string, object>
            {
                ["WorldId"] = world.WorldId,
                ["PlayerCount"] = world.Players.Count,
                ["MobCount"] = world.Mobs.Count,
                ["RoomCount"] = world.Rooms.Count,
                ["ExtractionPointsActive"] = world.ExtractionPoints.Values.Count(e => e.IsActive),
                ["CreatedAt"] = world.CreatedAt,
                ["IsCompleted"] = world.IsCompleted
            };
        }
    }

    // =============================================
    // DISPOSAL
    // =============================================

    public void Dispose()
    {
        lock (_worldsLock)
        {
            _worlds.Clear();
        }
        _logger.LogInformation("WorldManager disposed");
    }
}
