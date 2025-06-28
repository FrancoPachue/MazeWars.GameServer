using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Engine.Items.Interfaces;
using MazeWars.GameServer.Engine.Items.Models;
using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Items;

public class ItemSystem : IItemSystem
{
    private readonly ILogger<ItemSystem> _logger;
    private readonly ICombatSystem _combatSystem;
    private readonly IWorldInteractionSystem _worldInteractionSystem;

    public ItemSystem(
        ILogger<ItemSystem> logger,
        ICombatSystem combatSystem,
        IWorldInteractionSystem worldInteractionSystem)
    {
        _logger = logger;
        _combatSystem = combatSystem;
        _worldInteractionSystem = worldInteractionSystem;
    }

    public async Task<UseItemResult> UseItem(RealTimePlayer player, string itemId, GameWorld world)
    {
        UseItemResult result;
        if (!player.IsAlive)
        {
            return result = UseItemResult.Error("Cannot use items while dead");
        }

        var item = player.Inventory.FirstOrDefault(i => i.LootId == itemId);
        if (item == null)
        {
            return result = UseItemResult.Error("Item not found in inventory");
        }

        if (!CanUseItem(player, item))
        {
            return result = UseItemResult.Error("Cannot use this item right now");
        }

       result = item.ItemType.ToLower() switch
        {
            "consumable" => await UseConsumableItem(player, item),
            "key" => await UseKeyItem(player, item, world),
            "weapon" => await EquipWeapon(player, item),
            "armor" => await EquipArmor(player, item),
            "tool" => await UseTool(player, item, world),
            _ => result = UseItemResult.Error($"Item type '{item.ItemType}' cannot be used")
        };

        if (result.Success)
        {
            _logger.LogInformation("Player {PlayerName} used {ItemType} item {ItemName}",
                player.PlayerName, item.ItemType, item.ItemName);

            if (item.ItemType.ToLower() == "consumable")
            {
                player.Inventory.Remove(item);
            }
        }

        return result;
    }

    public bool CanUseItem(RealTimePlayer player, LootItem item)
    {
        return item.ItemType.ToLower() switch
        {
            "consumable" => CanUseConsumable(player, item),
            "key" => true, // Keys can always be used
            "weapon" => !player.IsCasting,
            "armor" => !player.IsCasting,
            "tool" => true,
            _ => false
        };
    }

    private async Task<UseItemResult> UseConsumableItem(RealTimePlayer player, LootItem item)
    {
        var healValue = item.Properties.TryGetValue("heal", out var heal) ? Convert.ToInt32(heal) : 0;
        var manaValue = item.Properties.TryGetValue("mana", out var mana) ? Convert.ToInt32(mana) : 0;
        var speedValue = item.Properties.TryGetValue("speed_boost", out var speed) ? Convert.ToSingle(speed) : 1.0f;
        var durationValue = item.Properties.TryGetValue("duration", out var duration) ? Convert.ToInt32(duration) : 0;

        var effectsApplied = new List<string>();

        // Healing
        if (healValue > 0)
        {
            var actualHeal = Math.Min(healValue, player.MaxHealth - player.Health);
            player.Health = Math.Min(player.MaxHealth, player.Health + healValue);
            effectsApplied.Add($"Healed {actualHeal} HP");
        }

        // Mana restoration
        if (manaValue > 0)
        {
            var actualMana = Math.Min(manaValue, player.MaxMana - player.Mana);
            player.Mana = Math.Min(player.MaxMana, player.Mana + manaValue);
            effectsApplied.Add($"Restored {actualMana} mana");
        }

        // Status effects (delegar al combat system)
        if (speedValue > 1.0f && durationValue > 0)
        {
            _combatSystem.ApplyStatusEffect(player, new StatusEffect
            {
                EffectType = "speed",
                Value = 0,
                ExpiresAt = DateTime.UtcNow.AddSeconds(durationValue),
                SourcePlayerId = player.PlayerId
            });
            effectsApplied.Add($"Speed boost {speedValue}x for {durationValue}s");
        }

        return UseItemResult.Ok(string.Join(", ", effectsApplied), item);
    }

    private async Task<UseItemResult> UseKeyItem(RealTimePlayer player, LootItem key, GameWorld world)
    {
        // ✅ Delegar al sistema de interacciones del mundo
        return await _worldInteractionSystem.UseKey(player, key, world);
    }

    private async Task<UseItemResult> EquipWeapon(RealTimePlayer player, LootItem weapon)
    {
        // Unequip current weapon if any
        var currentWeapon = player.Inventory.FirstOrDefault(i => i.ItemType == "weapon" && i.Properties.ContainsKey("equipped"));
        if (currentWeapon != null)
        {
            currentWeapon.Properties.Remove("equipped");
        }

        // Equip new weapon
        weapon.Properties["equipped"] = true;

        return UseItemResult.Ok($"Equipped {weapon.ItemName}", weapon);
    }

    private async Task<UseItemResult> EquipArmor(RealTimePlayer player, LootItem armor)
    {
        // Similar logic to weapons but for armor slots
        armor.Properties["equipped"] = true;
        return UseItemResult.Ok($"Equipped {armor.ItemName}", armor);
    }

    private async Task<UseItemResult> UseTool(RealTimePlayer player, LootItem tool, GameWorld world)
    {
        // Tools might have special interactions
        return tool.ItemName.ToLower() switch
        {
            "lockpick" => await _worldInteractionSystem.UseLockpick(player, tool, world),
            "rope" => await _worldInteractionSystem.UseRope(player, tool, world),
            _ => UseItemResult.Error($"Unknown tool: {tool.ItemName}")
        };
    }

    private bool CanUseConsumable(RealTimePlayer player, LootItem item)
    {
        // Check if healing is needed
        if (item.Properties.ContainsKey("heal") && player.Health >= player.MaxHealth)
            return false;

        // Check if mana restoration is needed
        if (item.Properties.ContainsKey("mana") && player.Mana >= player.MaxMana)
            return false;

        return true;
    }

    public ItemInfo GetItemInfo(string itemId)
    {
        // Return detailed info about item for UI
        return new ItemInfo { /* Implementation */ };
    }
}
