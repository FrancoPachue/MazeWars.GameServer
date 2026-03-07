using System.Text.RegularExpressions;
using MazeWars.GameServer.Data.Repositories;
using MazeWars.GameServer.Engine.Equipment.Data;
using MazeWars.GameServer.Engine.Equipment.Models;
using MazeWars.GameServer.Engine.Vendor;
using MazeWars.GameServer.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace MazeWars.GameServer.Admin;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly IPlayerRepository _playerRepository;
    private readonly ILogger<AccountController> _logger;

    // Per-account lock to prevent concurrent gold/stash race conditions
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _accountLocks = new();

    private static SemaphoreSlim GetAccountLock(string name)
    {
        return _accountLocks.GetOrAdd(name.ToLower(), _ => new SemaphoreSlim(1, 1));
    }

    private static readonly Regex ValidNamePattern = new(@"^[a-zA-Z0-9_\-]{1,30}$", RegexOptions.Compiled);
    private static readonly Regex ValidCharNamePattern = new(@"^[a-zA-Z0-9_\- ]{3,20}$", RegexOptions.Compiled);

    private IActionResult? ValidateName(string name)
    {
        if (!ValidNamePattern.IsMatch(name))
            return BadRequest(new { success = false, error = "Invalid account name" });
        return null;
    }

    private IActionResult? ValidateCharName(string charName)
    {
        if (!ValidCharNamePattern.IsMatch(charName))
            return BadRequest(new { success = false, error = "Invalid character name" });
        return null;
    }

    public AccountController(IPlayerRepository playerRepository, ILogger<AccountController> logger)
    {
        _playerRepository = playerRepository;
        _logger = logger;
    }

    /// <summary>Login or register, returns account data + character list</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerName) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Name and password required" });

        if (request.PlayerName.Length < 3 || request.PlayerName.Length > 20)
            return BadRequest(new { success = false, error = "Name must be 3-20 characters" });

        if (!System.Text.RegularExpressions.Regex.IsMatch(request.PlayerName, @"^[a-zA-Z0-9_\-]+$"))
            return BadRequest(new { success = false, error = "Name can only contain letters, numbers, hyphens and underscores" });

        var (success, error) = await _playerRepository.AuthenticateOrRegister(request.PlayerName, request.Password);
        if (!success)
            return Ok(new { success = false, error });

        var account = await _playerRepository.GetOrCreatePlayer(request.PlayerName);
        var characters = await _playerRepository.GetCharacters(request.PlayerName);

        return Ok(new
        {
            success = true,
            playerName = account.PlayerName,
            gold = account.Gold,
            characters = characters.Select(c => new
            {
                name = c.CharacterName,
                @class = c.Class,
                level = c.CurrentLevel,
                totalExperience = c.TotalExperience,
                appearance = c.AppearancePreset,
                careerKills = c.CareerKills,
                careerDeaths = c.CareerDeaths,
                totalMatchesPlayed = c.TotalMatchesPlayed,
                totalExtractions = c.TotalExtractions,
                equipment = DeserializeEquipmentSummary(c.EquipmentJson)
            }).ToList()
        });
    }

    /// <summary>Get stash items (read-only from Hub)</summary>
    [HttpGet("{name}/stash")]
    public async Task<IActionResult> GetStash(string name)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;

        var password = Request.Headers["X-Account-Password"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new { success = false, error = "Password required" });

        var (success, error) = await _playerRepository.AuthenticateOrRegister(name, password);
        if (!success)
            return Unauthorized(new { success = false, error });

        var items = await _playerRepository.LoadStash(name);
        return Ok(new
        {
            success = true,
            items = items.Select(i => new
            {
                lootId = i.LootId,
                itemName = i.ItemName,
                itemType = i.ItemType,
                rarity = i.Rarity,
                weight = i.Weight,
                value = i.Value,
                properties = i.Properties,
                stats = i.Stats
            }).ToList()
        });
    }

    private static readonly HashSet<string> ValidClasses = new(StringComparer.OrdinalIgnoreCase)
        { "scout", "tank", "support", "assassin", "warlock" };

    /// <summary>Create a new character for an account</summary>
    [HttpPost("{name}/characters")]
    public async Task<IActionResult> CreateCharacter(string name, [FromBody] CreateCharacterRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        if (string.IsNullOrWhiteSpace(request.CharacterName) || request.CharacterName.Length < 3 || request.CharacterName.Length > 20)
            return BadRequest(new { success = false, error = "Character name must be 3-20 characters" });

        if (!System.Text.RegularExpressions.Regex.IsMatch(request.CharacterName, @"^[a-zA-Z0-9_\- ]+$"))
            return BadRequest(new { success = false, error = "Character name can only contain letters, numbers, spaces, hyphens and underscores" });

        if (!ValidClasses.Contains(request.Class))
            return BadRequest(new { success = false, error = $"Invalid class. Valid: {string.Join(", ", ValidClasses)}" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var (success, error, character) = await _playerRepository.CreateCharacter(
            name, request.CharacterName, request.Class, request.Appearance);

        if (!success)
            return Ok(new { success = false, error });

        return Ok(new
        {
            success = true,
            character = new
            {
                name = character!.CharacterName,
                @class = character.Class,
                level = character.CurrentLevel,
                appearance = character.AppearancePreset
            }
        });
    }

    /// <summary>Delete a character</summary>
    [HttpDelete("{name}/characters/{charName}")]
    public async Task<IActionResult> DeleteCharacter(string name, string charName)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        var password = Request.Headers["X-Account-Password"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var (success, error) = await _playerRepository.DeleteCharacter(name, charName);
        if (!success)
            return Ok(new { success = false, error });

        return Ok(new { success = true });
    }

    // ════════════════════════════════
    // CHARACTER EQUIPMENT
    // ════════════════════════════════

    /// <summary>Get character's current equipment loadout</summary>
    [HttpGet("{name}/characters/{charName}/equipment")]
    public async Task<IActionResult> GetEquipment(string name, string charName)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        var password = Request.Headers["X-Account-Password"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var equipment = await _playerRepository.LoadEquipment(name, charName);
        return Ok(new
        {
            success = true,
            equipment = equipment.ToDictionary(
                kvp => kvp.Key,
                kvp => FormatEquipmentSlotInfo(kvp.Value))
        });
    }

    /// <summary>Move a stash item to a character's equipment slot</summary>
    [HttpPost("{name}/characters/{charName}/equip")]
    public async Task<IActionResult> EquipFromStash(string name, string charName, [FromBody] EquipRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        // Lock per-account to prevent concurrent stash race conditions
        var accountLock = GetAccountLock(name);
        await accountLock.WaitAsync();
        try
        {
        // Load stash
        var stash = await _playerRepository.LoadStash(name);
        var stashItem = stash.FirstOrDefault(i => i.LootId == request.LootId);
        if (stashItem == null)
            return Ok(new { success = false, error = "Item not found in stash" });

        // Resolve equipment definition
        var equipId = stashItem.Properties.TryGetValue("equipment_id", out var eid) ? eid?.ToString() : null;
        if (string.IsNullOrEmpty(equipId))
            return Ok(new { success = false, error = "Not an equipment item" });

        var equipDef = EquipmentRegistry.Get(equipId);
        if (equipDef == null)
            return Ok(new { success = false, error = "Unknown equipment type" });

        var slotName = equipDef.Slot.ToString();

        // Load character equipment
        var equipment = await _playerRepository.LoadEquipment(name, charName);

        // If slot occupied, move old item back to stash
        if (equipment.TryGetValue(slotName, out var oldItem))
        {
            stash.Add(oldItem);
            equipment.Remove(slotName);
        }

        // 2H weapon rules: equipping 2H weapon removes offhand, equipping offhand removes 2H weapon
        if (equipDef.Slot == Engine.Equipment.Models.EquipmentSlot.Weapon && equipDef.IsTwoHanded)
        {
            if (equipment.TryGetValue("Offhand", out var offhand))
            {
                stash.Add(offhand);
                equipment.Remove("Offhand");
            }
        }
        else if (equipDef.Slot == Engine.Equipment.Models.EquipmentSlot.Offhand)
        {
            if (equipment.TryGetValue("Weapon", out var weapon))
            {
                var weaponEqId = weapon.Properties.TryGetValue("equipment_id", out var weid) ? weid?.ToString() : null;
                var weaponDef = weaponEqId != null ? EquipmentRegistry.Get(weaponEqId) : null;
                if (weaponDef?.IsTwoHanded == true)
                {
                    stash.Add(weapon);
                    equipment.Remove("Weapon");
                }
            }
        }

        // Remove from stash, add to equipment
        stash.RemoveAll(i => i.LootId == request.LootId);
        equipment[slotName] = stashItem;

        // Save both
        await _playerRepository.SaveStash(name, stash);
        await _playerRepository.SaveEquipment(name, charName, equipment);

        return Ok(new
        {
            success = true,
            equipment = equipment.ToDictionary(
                kvp => kvp.Key,
                kvp => FormatEquipmentSlotInfo(kvp.Value))
        });
        }
        finally
        {
            accountLock.Release();
        }
    }

    /// <summary>Move an equipped item back to stash</summary>
    [HttpPost("{name}/characters/{charName}/unequip")]
    public async Task<IActionResult> UnequipToStash(string name, string charName, [FromBody] UnequipRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var accountLock = GetAccountLock(name);
        await accountLock.WaitAsync();
        try
        {
            var equipment = await _playerRepository.LoadEquipment(name, charName);
            if (!equipment.TryGetValue(request.Slot, out var item))
                return Ok(new { success = false, error = "No item in that slot" });

            var stash = await _playerRepository.LoadStash(name);
            if (stash.Count >= 50)
                return Ok(new { success = false, error = "Stash is full (max 50 items)" });

            equipment.Remove(request.Slot);
            stash.Add(item);

            await _playerRepository.SaveStash(name, stash);
            await _playerRepository.SaveEquipment(name, charName, equipment);

            return Ok(new
            {
                success = true,
                equipment = equipment.ToDictionary(
                    kvp => kvp.Key,
                    kvp => FormatEquipmentSlotInfo(kvp.Value))
            });
        }
        finally
        {
            accountLock.Release();
        }
    }

    // ════════════════════════════════
    // CHARACTER BACKPACK (consumables/potions)
    // ════════════════════════════════

    /// <summary>Get character's backpack items</summary>
    [HttpGet("{name}/characters/{charName}/backpack")]
    public async Task<IActionResult> GetBackpack(string name, string charName)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        var password = Request.Headers["X-Account-Password"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var backpack = await _playerRepository.LoadBackpack(name, charName);
        return Ok(new
        {
            success = true,
            items = backpack.Select(FormatBackpackItem).ToList()
        });
    }

    /// <summary>Move a stash item to character's backpack (for consumables/potions)</summary>
    [HttpPost("{name}/characters/{charName}/backpack/add")]
    public async Task<IActionResult> AddToBackpack(string name, string charName, [FromBody] EquipRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var accountLock = GetAccountLock(name);
        await accountLock.WaitAsync();
        try
        {
            var stash = await _playerRepository.LoadStash(name);
            var stashItem = stash.FirstOrDefault(i => i.LootId == request.LootId);
            if (stashItem == null)
                return Ok(new { success = false, error = "Item not found in stash" });

            var backpack = await _playerRepository.LoadBackpack(name, charName);
            if (backpack.Count >= 10)
                return Ok(new { success = false, error = "Backpack full (max 10 items)" });

            stash.RemoveAll(i => i.LootId == request.LootId);
            backpack.Add(stashItem);

            await _playerRepository.SaveStash(name, stash);
            await _playerRepository.SaveBackpack(name, charName, backpack);

            return Ok(new
            {
                success = true,
                items = backpack.Select(FormatBackpackItem).ToList()
            });
        }
        finally
        {
            accountLock.Release();
        }
    }

    /// <summary>Move a backpack item back to stash</summary>
    [HttpPost("{name}/characters/{charName}/backpack/remove")]
    public async Task<IActionResult> RemoveFromBackpack(string name, string charName, [FromBody] EquipRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var accountLock = GetAccountLock(name);
        await accountLock.WaitAsync();
        try
        {
            var backpack = await _playerRepository.LoadBackpack(name, charName);
            var item = backpack.FirstOrDefault(i => i.LootId == request.LootId);
            if (item == null)
                return Ok(new { success = false, error = "Item not found in backpack" });

            backpack.RemoveAll(i => i.LootId == request.LootId);

            var stash = await _playerRepository.LoadStash(name);
            if (stash.Count >= 50)
                return Ok(new { success = false, error = "Stash is full (max 50 items)" });
            stash.Add(item);

            await _playerRepository.SaveStash(name, stash);
            await _playerRepository.SaveBackpack(name, charName, backpack);

            return Ok(new
            {
                success = true,
                items = backpack.Select(FormatBackpackItem).ToList()
            });
        }
        finally
        {
            accountLock.Release();
        }
    }

    // ════════════════════════════════
    // DEFAULT GEAR
    // ════════════════════════════════

    private static readonly Dictionary<string, string[]> DefaultGearByClass = new()
    {
        ["scout"]    = new[] { "hunting_bow", "leather_hood", "leather_vest", "leather_boots", "traveler_cloak" },
        ["tank"]     = new[] { "iron_sword", "plate_helmet", "plate_chest", "plate_boots", "wooden_shield", "battle_banner" },
        ["support"]  = new[] { "holy_staff", "cloth_hood", "cloth_robe", "cloth_boots", "tome_of_wisdom", "scholars_mantle" },
        ["assassin"] = new[] { "shadow_dagger", "leather_hood", "leather_vest", "leather_boots", "traveler_cloak" },
        ["warlock"]  = new[] { "fire_staff", "cloth_hood", "cloth_robe", "cloth_boots", "scholars_mantle" },
    };

    /// <summary>Give a character class-default starting gear (fills empty slots only)</summary>
    [HttpPost("{name}/characters/{charName}/default-gear")]
    public async Task<IActionResult> ClaimDefaultGear(string name, string charName, [FromBody] DefaultGearRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;
        var charNameError = ValidateCharName(charName);
        if (charNameError != null) return charNameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var accountLock = GetAccountLock(name);
        await accountLock.WaitAsync();
        try
        {
            var character = await _playerRepository.GetCharacter(name, charName);
            if (character == null)
                return Ok(new { success = false, error = "Character not found" });

            var playerClass = character.Class.ToLower();
            if (!DefaultGearByClass.TryGetValue(playerClass, out var defaultGear))
                defaultGear = new[] { "iron_sword", "leather_vest", "leather_boots" };

            var equipment = await _playerRepository.LoadEquipment(name, charName);

            int added = 0;
            foreach (var equipId in defaultGear)
            {
                var equipDef = EquipmentRegistry.Get(equipId);
                if (equipDef == null) continue;

                var slotName = equipDef.Slot.ToString();

                // Only fill empty slots
                if (equipment.ContainsKey(slotName)) continue;

                var lootItem = new LootItem
                {
                    LootId = $"default_{equipId}_{Guid.NewGuid():N}",
                    ItemName = equipDef.DisplayName,
                    ItemType = equipDef.Slot is Engine.Equipment.Models.EquipmentSlot.Weapon ? "weapon" : "armor",
                    Rarity = 0,
                    Properties = new Dictionary<string, object>
                    {
                        ["equipment_id"] = equipId,
                        ["rarity"] = 0,
                        ["quality"] = 0,
                        ["default_gear"] = true
                    }
                };

                equipment[slotName] = lootItem;
                added++;
            }

            if (added == 0)
                return Ok(new { success = false, error = "All slots already have equipment" });

            await _playerRepository.SaveEquipment(name, charName, equipment);

            return Ok(new
            {
                success = true,
                added,
                equipment = equipment.ToDictionary(
                    kvp => kvp.Key,
                    kvp => FormatEquipmentSlotInfo(kvp.Value))
            });
        }
        finally
        {
            accountLock.Release();
        }
    }

    // ════════════════════════════════
    // VENDOR (REST, for Hub)
    // ════════════════════════════════

    /// <summary>Get vendor catalog (base + featured + consumables) + player gold</summary>
    [HttpGet("{name}/vendor/catalog")]
    public async Task<IActionResult> GetVendorCatalog(string name)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;

        var password = Request.Headers["X-Account-Password"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        var account = await _playerRepository.GetOrCreatePlayer(name);

        // Base equipment (always available, Uncommon rarity=1)
        var baseCatalog = VendorService.GetCatalogPrices();
        var items = new List<object>();
        foreach (var (equipId, price) in baseCatalog)
        {
            var equipDef = EquipmentRegistry.GetWithRarity(equipId, 1) ?? EquipmentRegistry.Get(equipId);
            if (equipDef == null) continue;
            items.Add(new
            {
                equipmentId = equipId,
                displayName = equipDef.DisplayName,
                slot = equipDef.Slot.ToString(),
                price,
                requiredLevel = equipDef.RequiredLevel,
                rarity = 1,
                section = "base",
                description = VendorService.BuildEquipmentDescription(equipDef)
            });
        }

        // Featured equipment (rotating, Rare/Epic)
        var featured = VendorService.GetFeaturedItems();
        var featuredItems = featured.Select(f =>
        {
            var equipDef = EquipmentRegistry.GetWithRarity(f.EquipmentId, f.Rarity)
                           ?? EquipmentRegistry.Get(f.EquipmentId);
            return new
            {
                equipmentId = f.EquipmentId,
                displayName = f.DisplayName,
                slot = f.Slot,
                price = f.Price,
                requiredLevel = 0,
                rarity = f.Rarity,
                section = "featured",
                description = equipDef != null ? VendorService.BuildEquipmentDescription(equipDef) : ""
            };
        }).ToList<object>();

        // Consumables (always available)
        var consumables = VendorService.GetConsumableCatalog().Select(c => new
        {
            equipmentId = c.Name,  // Use name as ID for consumables
            displayName = c.Name,
            slot = c.ItemType,     // "consumable" or "key"
            price = c.Price,
            requiredLevel = 0,
            rarity = 0,
            section = "consumable",
            description = VendorService.BuildConsumableDescription(c.Properties)
        }).ToList<object>();

        var allItems = items.Concat(featuredItems).Concat(consumables).ToList();

        return Ok(new
        {
            success = true,
            playerGold = account.Gold,
            items = allItems,
            nextRotationSeconds = VendorService.GetSecondsUntilRotation()
        });
    }

    /// <summary>Buy an item from vendor (base/featured/consumable) → add to stash, deduct gold</summary>
    [HttpPost("{name}/vendor/buy")]
    public async Task<IActionResult> VendorBuy(string name, [FromBody] VendorBuyRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        // Resolve item and price based on section
        LootItem? item = null;
        int price = 0;
        string itemDisplayName = request.EquipmentId;

        switch (request.Section?.ToLower())
        {
            case "featured":
            {
                var featured = VendorService.GetFeaturedItems();
                var feat = featured.FirstOrDefault(f => f.EquipmentId == request.EquipmentId);
                if (feat == null)
                    return Ok(new { success = false, error = "Featured item not available (rotation may have changed)" });
                var equipDef = EquipmentRegistry.Get(feat.EquipmentId);
                if (equipDef == null)
                    return Ok(new { success = false, error = "Equipment definition not found" });
                price = feat.Price;
                itemDisplayName = feat.DisplayName;
                item = VendorService.CreateEquipmentLootItem(feat.EquipmentId, equipDef, price, feat.Rarity);
                break;
            }
            case "consumable":
            {
                item = VendorService.CreateConsumableLootItem(request.EquipmentId);
                if (item == null)
                    return Ok(new { success = false, error = "Consumable not available" });
                price = item.Value;
                itemDisplayName = item.ItemName;
                break;
            }
            default: // "base"
            {
                var catalog = VendorService.GetCatalogPrices();
                if (!catalog.TryGetValue(request.EquipmentId, out price))
                    return Ok(new { success = false, error = "Item not available from vendor" });
                var equipDef = EquipmentRegistry.Get(request.EquipmentId);
                if (equipDef == null)
                    return Ok(new { success = false, error = "Equipment definition not found" });
                itemDisplayName = equipDef.DisplayName;
                item = VendorService.CreateEquipmentLootItem(request.EquipmentId, equipDef, price, rarity: 1);
                break;
            }
        }

        var accountLock = GetAccountLock(name);
        await accountLock.WaitAsync();
        try
        {
            var account = await _playerRepository.GetOrCreatePlayer(name);
            if (account.Gold < price)
                return Ok(new { success = false, error = $"Not enough gold. Need {price}, have {account.Gold}" });

            var stash = await _playerRepository.LoadStash(name);
            if (stash.Count >= 50)
                return Ok(new { success = false, error = "Stash is full (max 50 items)" });

            await _playerRepository.UpdateCareerStats(name, "", 0, 0, 0, 0, false, goldEarned: -price);

            stash.Add(item);
            await _playerRepository.SaveStash(name, stash);

            var updatedAccount = await _playerRepository.GetOrCreatePlayer(name);

            _logger.LogInformation("Hub vendor: {Player} bought {Item} ({Section}) for {Price}g",
                name, itemDisplayName, request.Section ?? "base", price);

            return Ok(new { success = true, playerGold = updatedAccount.Gold });
        }
        finally
        {
            accountLock.Release();
        }
    }

    /// <summary>Sell a stash item → remove from stash, add gold</summary>
    [HttpPost("{name}/vendor/sell")]
    public async Task<IActionResult> VendorSell(string name, [FromBody] VendorSellRequest request)
    {
        var nameError = ValidateName(name);
        if (nameError != null) return nameError;

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, error = "Password required" });

        var (authOk, authError) = await _playerRepository.AuthenticateOrRegister(name, request.Password);
        if (!authOk)
            return Unauthorized(new { success = false, error = authError });

        // Lock per-account to prevent concurrent stash/gold race conditions
        var accountLock = GetAccountLock(name);
        await accountLock.WaitAsync();
        try
        {
            var stash = await _playerRepository.LoadStash(name);
            var item = stash.FirstOrDefault(i => i.LootId == request.LootId);
            if (item == null)
                return Ok(new { success = false, error = "Item not found in stash" });

            // Default gear sells for 0 gold; normal items sell for 40% of value (minimum 5 gold)
            bool isDefaultGear = item.LootId.StartsWith("default_");
            var sellPrice = isDefaultGear ? 0 : Math.Max(5, (int)(item.Value * 0.4));

            stash.RemoveAll(i => i.LootId == request.LootId);
            await _playerRepository.SaveStash(name, stash);
            await _playerRepository.UpdateCareerStats(name, "", 0, 0, 0, 0, false, goldEarned: sellPrice);

            var account = await _playerRepository.GetOrCreatePlayer(name);

            _logger.LogInformation("Hub vendor: {Player} sold {Item} for {Price}g", name, item.ItemName, sellPrice);

            return Ok(new { success = true, playerGold = account.Gold, goldEarned = sellPrice });
        }
        finally
        {
            accountLock.Release();
        }
    }

    // ════════════════════════════════
    // HELPERS
    // ════════════════════════════════

    private static object FormatEquipmentSlotInfo(LootItem item)
    {
        return new
        {
            lootId = item.LootId,
            itemName = item.ItemName,
            itemType = item.ItemType,
            rarity = item.Rarity,
            value = item.Value,
            equipmentId = item.Properties.TryGetValue("equipment_id", out var eid) ? eid?.ToString() ?? "" : "",
            properties = item.Properties,
            stats = item.Stats
        };
    }

    private static object FormatBackpackItem(LootItem item)
    {
        return new
        {
            lootId = item.LootId,
            itemName = item.ItemName,
            itemType = item.ItemType,
            rarity = item.Rarity,
            value = item.Value,
            weight = item.Weight,
            stackCount = item.StackCount,
            properties = item.Properties,
            stats = item.Stats
        };
    }

    private static Dictionary<string, object> DeserializeEquipmentSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, object>();

        try
        {
            var equipment = JsonConvert.DeserializeObject<Dictionary<string, LootItem>>(json);
            if (equipment == null) return new Dictionary<string, object>();
            return equipment.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)FormatEquipmentSlotInfo(kvp.Value));
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }
}

public class LoginRequest
{
    public string PlayerName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class CreateCharacterRequest
{
    public string Password { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string Class { get; set; } = "scout";
    public int Appearance { get; set; }
}

public class EquipRequest
{
    public string Password { get; set; } = string.Empty;
    public string LootId { get; set; } = string.Empty;
}

public class UnequipRequest
{
    public string Password { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;
}

public class DefaultGearRequest
{
    public string Password { get; set; } = string.Empty;
}

public class VendorBuyRequest
{
    public string Password { get; set; } = string.Empty;
    public string EquipmentId { get; set; } = string.Empty;
    public string Section { get; set; } = "base";  // "base", "featured", "consumable"
}

public class VendorSellRequest
{
    public string Password { get; set; } = string.Empty;
    public string LootId { get; set; } = string.Empty;
}
