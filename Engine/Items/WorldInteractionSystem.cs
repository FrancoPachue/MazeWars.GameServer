using MazeWars.GameServer.Engine.Items.Interfaces;
using MazeWars.GameServer.Engine.Items.Models;
using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Items;

public class WorldInteractionSystem : IWorldInteractionSystem
{
    private readonly ILogger<WorldInteractionSystem> _logger;

    public WorldInteractionSystem(ILogger<WorldInteractionSystem> logger)
    {
        _logger = logger;
    }

    public async Task<UseItemResult> UseKey(RealTimePlayer player, LootItem key, GameWorld world)
    {
        // Find nearby locked doors/chests
        var nearbyInteractables = FindNearbyInteractables(player, world, "locked");

        if (!nearbyInteractables.Any())
        {
            return UseItemResult.Error("No locked objects nearby");
        }

        var keyType = key.ItemName.ToLower();
        var compatibleObject = nearbyInteractables.FirstOrDefault(obj =>
            CanKeyUnlock(keyType, obj.RequiredKeyType));

        if (compatibleObject == null)
        {
            return UseItemResult.Error("This key doesn't fit any nearby locks");
        }

        // Unlock the object
        compatibleObject.IsLocked = false;
        compatibleObject.UnlockedBy = player.PlayerId;
        compatibleObject.UnlockedAt = DateTime.UtcNow;

        _logger.LogInformation("Player {PlayerName} unlocked {ObjectType} with {KeyName}",
            player.PlayerName, compatibleObject.ObjectType, key.ItemName);

        // Key might be consumed depending on type
        var keyConsumed = key.ItemName.ToLower() != "master key";

        return UseItemResult.Ok(
            $"Unlocked {compatibleObject.ObjectType}",
            key,
            keyConsumed);
    }

    public async Task<UseItemResult> UseLockpick(RealTimePlayer player, LootItem lockpick, GameWorld world)
    {
        // Lockpicking minigame logic here
        var success = CalculateLockpickSuccess(player, lockpick);

        if (success)
        {
            return UseItemResult.Ok("Successfully picked the lock", lockpick);
        }
        else
        {
            return UseItemResult.Error("Failed to pick the lock");
        }
    }

    public async Task<UseItemResult> UseRope(RealTimePlayer player, LootItem rope, GameWorld world)
    {
        // Rope usage logic (climbing, escaping, etc.)
        return UseItemResult.Ok("Used rope", rope);
    }

    public bool CanInteractWith(RealTimePlayer player, string objectId, GameWorld world)
    {
        // Check distance, line of sight, etc.
        return true; // Simplified
    }

    private List<InteractableObject> FindNearbyInteractables(RealTimePlayer player, GameWorld world, string state)
    {
        // Find interactable objects near the player
        return new List<InteractableObject>(); // Simplified
    }

    private bool CanKeyUnlock(string keyType, string requiredKeyType)
    {
        return keyType switch
        {
            "master key" => true, // Master key opens everything
            "gold key" => requiredKeyType == "gold" || requiredKeyType == "silver",
            "silver key" => requiredKeyType == "silver",
            _ => keyType == requiredKeyType
        };
    }

    private bool CalculateLockpickSuccess(RealTimePlayer player, LootItem lockpick)
    {
        // Factor in player skills, lockpick quality, etc.
        var baseChance = 0.6f;
        var skillBonus = player.Stats.GetValueOrDefault("agility", 10) * 0.02f;
        var qualityBonus = lockpick.Rarity * 0.1f;

        var successChance = baseChance + skillBonus + qualityBonus;
        return Random.Shared.NextDouble() < successChance;
    }
}
