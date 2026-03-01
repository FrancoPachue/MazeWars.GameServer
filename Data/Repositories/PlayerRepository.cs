using MazeWars.GameServer.Data.Models;
using MazeWars.GameServer.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace MazeWars.GameServer.Data.Repositories;

public interface IPlayerRepository
{
    Task<PlayerAccount> GetOrCreatePlayer(string playerName);
    Task UpdateCareerStats(string playerName, int kills, int deaths, int damageDealt, int healingDone, bool extracted, int xpGained = 0, int goldEarned = 0);
    Task SaveStash(string playerName, List<LootItem> items);
    Task<List<LootItem>> LoadStash(string playerName);
    Task RecordMatch(string playerName, MatchRecord record);
}

public class PlayerRepository : IPlayerRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerRepository> _logger;

    public PlayerRepository(IServiceScopeFactory scopeFactory, ILogger<PlayerRepository> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<PlayerAccount> GetOrCreatePlayer(string playerName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerName == playerName);
        if (player == null)
        {
            player = new PlayerAccount
            {
                PlayerName = playerName,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            db.Players.Add(player);
            await db.SaveChangesAsync();
            _logger.LogInformation("Created new player account: {PlayerName}", playerName);
        }
        else
        {
            player.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return player;
    }

    public async Task UpdateCareerStats(string playerName, int kills, int deaths, int damageDealt, int healingDone, bool extracted, int xpGained = 0, int goldEarned = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerName == playerName);
        if (player == null) return;

        player.CareerKills += kills;
        player.CareerDeaths += deaths;
        player.CareerDamageDealt += damageDealt;
        player.CareerHealingDone += healingDone;
        player.TotalMatchesPlayed++;
        if (extracted) player.TotalExtractions++;

        // Persist XP and calculate level
        player.TotalExperience += xpGained;
        player.CurrentLevel = CalculateLevel(player.TotalExperience);

        // Persist gold
        player.Gold += goldEarned;

        await db.SaveChangesAsync();
    }

    private static int CalculateLevel(long totalXP)
    {
        int level = 1;
        long accumulated = 0;
        long xpNeeded = 1000;

        while (accumulated + xpNeeded <= totalXP)
        {
            accumulated += xpNeeded;
            level++;
            xpNeeded = (long)(1000 * Math.Pow(1.5, level - 1));
        }

        return level;
    }

    public async Task SaveStash(string playerName, List<LootItem> items)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var player = await db.Players.Include(p => p.StashedItems)
                             .FirstOrDefaultAsync(p => p.PlayerName == playerName);
        if (player == null) return;

        // Clear existing stash and replace
        player.StashedItems.Clear();

        foreach (var item in items)
        {
            player.StashedItems.Add(new StashedItem
            {
                PlayerAccountId = player.Id,
                ItemName = item.ItemName,
                ItemType = item.ItemType,
                Rarity = item.Rarity,
                Weight = item.Weight,
                Value = item.Value,
                PropertiesJson = JsonConvert.SerializeObject(item.Properties),
                StatsJson = JsonConvert.SerializeObject(item.Stats)
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<LootItem>> LoadStash(string playerName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var player = await db.Players.Include(p => p.StashedItems)
                             .FirstOrDefaultAsync(p => p.PlayerName == playerName);
        if (player == null) return new List<LootItem>();

        return player.StashedItems.Select(s => new LootItem
        {
            LootId = Guid.NewGuid().ToString(),
            ItemName = s.ItemName,
            ItemType = s.ItemType,
            Rarity = s.Rarity,
            Weight = s.Weight,
            Value = s.Value,
            Properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(s.PropertiesJson) ?? new(),
            Stats = JsonConvert.DeserializeObject<Dictionary<string, int>>(s.StatsJson) ?? new()
        }).ToList();
    }

    public async Task RecordMatch(string playerName, MatchRecord record)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerName == playerName);
        if (player == null) return;

        record.PlayerAccountId = player.Id;
        db.MatchHistory.Add(record);
        await db.SaveChangesAsync();
    }
}
