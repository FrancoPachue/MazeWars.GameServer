using MazeWars.GameServer.Data.Repositories;
using MazeWars.GameServer.Engine.Equipment.Data;
using MazeWars.GameServer.Engine.Equipment.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Vendor;

/// <summary>
/// Vendor NPC service. Sells equipment and consumables for gold, buys player items.
/// Features a rotating "featured" section with higher-rarity gear every 6 hours.
/// </summary>
public class VendorService
{
    private readonly IPlayerRepository _playerRepository;
    private readonly ILogger<VendorService> _logger;

    private const int RotationHours = 6;
    private const int FeaturedSlots = 4;

    // ═══════════════════════════════
    // BASE CATALOG (always available)
    // ═══════════════════════════════

    // Base equipment prices (Uncommon, Rarity 1)
    private static readonly Dictionary<string, int> _baseCatalog = new()
    {
        // Weapons
        ["iron_sword"] = 100,
        ["fire_staff"] = 120,
        ["hunting_bow"] = 110,
        ["holy_staff"] = 130,
        ["shadow_dagger"] = 115,
        ["ice_staff"] = 125,
        // Offhand
        ["wooden_shield"] = 60,
        ["tome_of_wisdom"] = 80,
        ["torch"] = 30,
        // Head
        ["plate_helmet"] = 70,
        ["leather_hood"] = 50,
        ["cloth_hood"] = 45,
        // Chest
        ["plate_chest"] = 90,
        ["leather_vest"] = 65,
        ["cloth_robe"] = 55,
        // Boots
        ["plate_boots"] = 60,
        ["leather_boots"] = 40,
        ["cloth_boots"] = 35,
        // Cape
        ["battle_banner"] = 75,
        ["traveler_cloak"] = 50,
        ["scholars_mantle"] = 65,
    };

    // Consumables (always available)
    private static readonly List<ConsumableEntry> _consumableCatalog = new()
    {
        new("Health Potion", 25, new() { ["heal"] = 50 }),
        new("Mana Potion", 20, new() { ["mana"] = 40 }),
        new("Speed Elixir", 35, new() { ["speed_boost"] = 30, ["duration"] = 8 }),
        new("Shield Potion", 40, new() { ["shield_amount"] = 30, ["duration"] = 15 }),
        new("Antidote", 15, new() { ["cleanse"] = 1 }),
        new("Strength Tonic", 45, new() { ["damage_boost"] = 25, ["duration"] = 10 }),
        new("Silver Key", 60, new() { ["key_type"] = "silver" }, ItemType: "key"),
    };

    private record ConsumableEntry(string Name, int Price, Dictionary<string, object> Properties, string ItemType = "consumable");

    // ═══════════════════════════════
    // FEATURED ROTATION
    // ═══════════════════════════════

    private static readonly object _rotationLock = new();
    private static long _currentRotationEpoch = -1;
    private static List<FeaturedItem> _featuredItems = new();

    public record FeaturedItem(string EquipmentId, int Rarity, int Price, string DisplayName, string Slot);

    /// <summary>Get the current rotation epoch (changes every RotationHours)</summary>
    private static long GetRotationEpoch() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (RotationHours * 3600);

    /// <summary>Get featured items, rotating if epoch changed</summary>
    public static List<FeaturedItem> GetFeaturedItems()
    {
        var epoch = GetRotationEpoch();
        if (epoch == _currentRotationEpoch) return _featuredItems;

        lock (_rotationLock)
        {
            if (epoch == _currentRotationEpoch) return _featuredItems;
            _featuredItems = GenerateFeaturedItems(epoch);
            _currentRotationEpoch = epoch;
        }
        return _featuredItems;
    }

    private static List<FeaturedItem> GenerateFeaturedItems(long epoch)
    {
        var rng = new Random((int)(epoch ^ 0x5DEECE66D));
        var allIds = _baseCatalog.Keys.ToList();
        var featured = new List<FeaturedItem>();

        // Shuffle and pick FeaturedSlots items
        for (int i = allIds.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (allIds[i], allIds[j]) = (allIds[j], allIds[i]);
        }

        for (int i = 0; i < Math.Min(FeaturedSlots, allIds.Count); i++)
        {
            var equipId = allIds[i];
            var basePrice = _baseCatalog[equipId];
            var equipDef = EquipmentRegistry.Get(equipId);
            if (equipDef == null) continue;

            // 70% Rare (Rarity 2), 30% Epic (Rarity 3)
            int rarity = rng.NextDouble() < 0.70 ? 2 : 3;
            double priceMultiplier = rarity == 2 ? 2.5 : 5.0;
            int price = (int)(basePrice * priceMultiplier);

            var rarityDef = EquipmentRegistry.GetWithRarity(equipId, rarity);
            var displayName = rarityDef?.DisplayName ?? equipDef.DisplayName;

            featured.Add(new FeaturedItem(equipId, rarity, price, displayName, equipDef.Slot.ToString()));
        }

        return featured;
    }

    /// <summary>Seconds until next rotation</summary>
    public static int GetSecondsUntilRotation()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var periodSeconds = RotationHours * 3600;
        var nextRotation = ((now / periodSeconds) + 1) * periodSeconds;
        return (int)(nextRotation - now);
    }

    // ═══════════════════════════════
    // COMBINED CATALOG
    // ═══════════════════════════════

    /// <summary>Get the static base catalog prices (for backward compat)</summary>
    public static IReadOnlyDictionary<string, int> GetCatalogPrices() => _baseCatalog;

    /// <summary>Look up an item across all vendor sections. Returns (price, rarity) or null.</summary>
    public static (int Price, int Rarity)? LookupItem(string equipmentId)
    {
        // Check featured first (higher rarity)
        var featured = GetFeaturedItems();
        var feat = featured.FirstOrDefault(f => f.EquipmentId == equipmentId && f.Rarity > 1);
        // Featured items are looked up via a composite key (see BuyFeatured)

        // Check base catalog
        if (_baseCatalog.TryGetValue(equipmentId, out var basePrice))
            return (basePrice, 1);

        return null;
    }

    public VendorService(IPlayerRepository playerRepository, ILogger<VendorService> logger)
    {
        _playerRepository = playerRepository;
        _logger = logger;
    }

    public VendorCatalogData GetCatalog(long playerGold)
    {
        var items = new List<VendorCatalogItem>();
        foreach (var (equipId, price) in _baseCatalog)
        {
            var equipDef = EquipmentRegistry.Get(equipId);
            if (equipDef == null) continue;

            items.Add(new VendorCatalogItem
            {
                EquipmentId = equipId,
                DisplayName = equipDef.DisplayName,
                Slot = equipDef.Slot.ToString(),
                Price = price,
                RequiredLevel = equipDef.RequiredLevel
            });
        }

        return new VendorCatalogData
        {
            Items = items,
            PlayerGold = playerGold
        };
    }

    // ═══════════════════════════════
    // BUY (in-game, UDP)
    // ═══════════════════════════════

    public (bool Success, string Error, LootItem? Item) BuyItem(RealTimePlayer player, string equipmentId)
    {
        if (!_baseCatalog.TryGetValue(equipmentId, out var price))
            return (false, "Item not available from vendor", null);

        var equipDef = EquipmentRegistry.Get(equipmentId);
        if (equipDef == null)
            return (false, "Equipment definition not found", null);

        var item = CreateEquipmentLootItem(equipmentId, equipDef, price, rarity: 1);

        lock (player.InventoryLock)
        {
            if (player.AccountGold < price)
                return (false, $"Not enough gold. Need {price}, have {player.AccountGold}", null);

            player.AccountGold -= price;
            player.Inventory.Add(item);
        }

        PersistGoldChange(player, -price, item, "purchase");

        _logger.LogInformation("Player {PlayerName} bought {ItemName} for {Price} gold (remaining: {Gold})",
            player.PlayerName, equipDef.DisplayName, price, player.AccountGold);

        return (true, string.Empty, item);
    }

    public (bool Success, string Error, int GoldEarned) SellItem(RealTimePlayer player, string lootId)
    {
        LootItem? item;
        int sellPrice;

        lock (player.InventoryLock)
        {
            item = player.Inventory.FirstOrDefault(i => i.LootId == lootId);
            if (item == null)
                return (false, "Item not in inventory", 0);

            player.Inventory.Remove(item);

            bool isDefaultGear = item.LootId.StartsWith("default_");
            sellPrice = isDefaultGear ? 0 : Math.Max(5, (int)(item.Value * 0.4));
            player.AccountGold += sellPrice;
        }

        PersistGoldChange(player, sellPrice, item, "sale");

        _logger.LogInformation("Player {PlayerName} sold {ItemName} for {Price} gold (total: {Gold})",
            player.PlayerName, item.ItemName, sellPrice, player.AccountGold);

        return (true, string.Empty, sellPrice);
    }

    // ═══════════════════════════════
    // HELPERS
    // ═══════════════════════════════

    /// <summary>Create a LootItem for a featured equipment purchase (specific rarity)</summary>
    public static LootItem CreateEquipmentLootItem(string equipmentId, EquipmentDefinition equipDef, int price, int rarity)
    {
        var rarityDef = rarity > 1 ? EquipmentRegistry.GetWithRarity(equipmentId, rarity) : null;
        var displayName = rarityDef?.DisplayName ?? equipDef.DisplayName;

        return new LootItem
        {
            LootId = $"vendor_{equipmentId}_{Guid.NewGuid():N}",
            ItemName = displayName,
            ItemType = equipDef.Slot is EquipmentSlot.Weapon ? "weapon" : "armor",
            Rarity = rarity,
            Value = price,
            Properties = new Dictionary<string, object>
            {
                ["equipment_id"] = equipmentId,
                ["rarity"] = rarity,
                ["quality"] = 0
            }
        };
    }

    /// <summary>Create a LootItem for a consumable purchase</summary>
    public static LootItem? CreateConsumableLootItem(string consumableName)
    {
        var entry = _consumableCatalog.FirstOrDefault(c =>
            c.Name.Equals(consumableName, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        return new LootItem
        {
            LootId = $"vendor_consumable_{Guid.NewGuid():N}",
            ItemName = entry.Name,
            ItemType = entry.ItemType,
            Rarity = 0,
            Value = entry.Price,
            StackCount = 1,
            Properties = new Dictionary<string, object>(entry.Properties)
        };
    }

    /// <summary>Get consumable catalog for REST/UDP responses</summary>
    public static List<(string Name, int Price, string ItemType, Dictionary<string, object> Properties)> GetConsumableCatalog() =>
        _consumableCatalog.Select(c => (c.Name, c.Price, c.ItemType, c.Properties)).ToList();

    /// <summary>Build a human-readable description of equipment stats</summary>
    public static string BuildEquipmentDescription(EquipmentDefinition def)
    {
        var parts = new List<string>();
        if (def.BonusHealth > 0) parts.Add($"+{def.BonusHealth} HP");
        if (def.BonusMana > 0) parts.Add($"+{def.BonusMana} Mana");
        if (def.BonusDamagePercent > 0) parts.Add($"+{def.BonusDamagePercent:0.#}% Damage");
        if (def.BonusHealingPercent > 0) parts.Add($"+{def.BonusHealingPercent:0.#}% Healing");
        if (def.DamageReduction > 0) parts.Add($"+{def.DamageReduction:0.#}% Armor");
        if (def.MovementSpeedBonus > 0) parts.Add($"+{def.MovementSpeedBonus:0.#}% Speed");
        if (def.CooldownReduction > 0) parts.Add($"-{def.CooldownReduction:0.#}% Cooldown");
        if (def.AttackSpeedBonus > 0) parts.Add($"+{def.AttackSpeedBonus:0.#}% Atk Speed");
        if (def.CritChance > 0) parts.Add($"+{def.CritChance:0.#}% Crit");
        if (def.HealthRegenPerSecond > 0) parts.Add($"+{def.HealthRegenPerSecond:0.#} HP/s");
        if (def.ManaRegenPerSecond > 0) parts.Add($"+{def.ManaRegenPerSecond:0.#} MP/s");
        if (def.GrantedAbilities.Count > 0) parts.Add($"Abilities: {string.Join(", ", def.GrantedAbilities)}");
        if (def.IsTwoHanded) parts.Add("Two-Handed");
        return parts.Count > 0 ? string.Join(", ", parts) : "No bonus stats";
    }

    /// <summary>Build a human-readable description of a consumable</summary>
    public static string BuildConsumableDescription(Dictionary<string, object> props)
    {
        var parts = new List<string>();
        foreach (var (key, value) in props)
        {
            var desc = key switch
            {
                "heal" => $"Heals {value} HP",
                "mana" => $"Restores {value} Mana",
                "speed_boost" => $"+{value}% Speed",
                "shield_amount" => $"+{value} Shield",
                "damage_boost" => $"+{value}% Damage",
                "duration" => $"for {value}s",
                "cleanse" => "Removes debuffs",
                "key_type" => $"Opens {value} doors",
                _ => null
            };
            if (desc != null) parts.Add(desc);
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "";
    }

    private void PersistGoldChange(RealTimePlayer player, int goldDelta, LootItem item, string operation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _playerRepository.UpdateCareerStats(player.PlayerName, player.CharacterName ?? "", 0, 0, 0, 0, false, goldEarned: goldDelta);
            }
            catch (Exception ex)
            {
                lock (player.InventoryLock)
                {
                    player.AccountGold -= goldDelta;
                    if (operation == "purchase")
                        player.Inventory.Remove(item);
                    else
                        player.Inventory.Add(item);
                }
                _logger.LogError(ex, "Failed to persist vendor {Op} for {Player}, reversed in-memory state", operation, player.PlayerName);
            }
        });
    }
}
