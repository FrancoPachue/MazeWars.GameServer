using System.Collections.Concurrent;
using MazeWars.GameServer.Data.Repositories;
using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Stash;

/// <summary>
/// Player stash with in-memory cache backed by SQLite database.
/// Players keep extracted items here between games and across server restarts.
/// </summary>
public class StashService
{
    private const int MaxStashSize = 50;

    // In-memory cache for fast access during gameplay
    private readonly ConcurrentDictionary<string, List<LootItem>> _stashes = new();
    private readonly IPlayerRepository _playerRepository;
    private readonly ILogger<StashService> _logger;

    public StashService(IPlayerRepository playerRepository, ILogger<StashService> logger)
    {
        _playerRepository = playerRepository;
        _logger = logger;
    }

    /// <summary>
    /// Load stash from database into memory cache on player connect.
    /// </summary>
    public async Task LoadFromDatabase(string playerName)
    {
        try
        {
            var items = await _playerRepository.LoadStash(playerName);
            _stashes[playerName] = items;
            _logger.LogInformation("Loaded {Count} stash items from DB for {Player}", items.Count, playerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load stash from DB for {Player}", playerName);
            _stashes.GetOrAdd(playerName, _ => new List<LootItem>());
        }
    }

    /// <summary>
    /// Add extracted items to a player's stash and persist to database.
    /// </summary>
    public int StashItems(string playerName, List<LootItem> items)
    {
        var stash = _stashes.GetOrAdd(playerName, _ => new List<LootItem>());
        int added = 0;

        lock (stash)
        {
            foreach (var item in items)
            {
                if (stash.Count >= MaxStashSize) break;

                var stashedItem = new LootItem
                {
                    LootId = item.LootId,
                    ItemName = item.ItemName,
                    ItemType = item.ItemType,
                    Rarity = item.Rarity,
                    Properties = new Dictionary<string, object>(item.Properties),
                    Stats = new Dictionary<string, int>(item.Stats),
                    Value = item.Value
                };
                stash.Add(stashedItem);
                added++;
            }
        }

        // Persist to DB asynchronously (fire-and-forget with error logging)
        _ = PersistStash(playerName);

        return added;
    }

    /// <summary>
    /// Get a player's stash contents.
    /// </summary>
    public List<LootItem> GetStash(string playerName)
    {
        if (!_stashes.TryGetValue(playerName, out var stash))
            return new List<LootItem>();

        lock (stash)
        {
            return new List<LootItem>(stash);
        }
    }

    /// <summary>
    /// Remove an item from the stash (e.g., when equipping from stash).
    /// </summary>
    public bool RemoveItem(string playerName, string lootId)
    {
        if (!_stashes.TryGetValue(playerName, out var stash))
            return false;

        bool removed;
        lock (stash)
        {
            removed = stash.RemoveAll(i => i.LootId == lootId) > 0;
        }

        if (removed)
            _ = PersistStash(playerName);

        return removed;
    }

    /// <summary>
    /// Get stash count for a player.
    /// </summary>
    public int GetStashCount(string playerName)
    {
        if (!_stashes.TryGetValue(playerName, out var stash))
            return 0;

        lock (stash)
        {
            return stash.Count;
        }
    }

    private async Task PersistStash(string playerName)
    {
        try
        {
            var items = GetStash(playerName);
            await _playerRepository.SaveStash(playerName, items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist stash to DB for {Player}", playerName);
        }
    }
}
