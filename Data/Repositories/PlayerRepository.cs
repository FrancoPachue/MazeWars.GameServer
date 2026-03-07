using System.Security.Cryptography;
using System.Text;
using MazeWars.GameServer.Data.Models;
using MazeWars.GameServer.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace MazeWars.GameServer.Data.Repositories;

public interface IPlayerRepository
{
    Task<PlayerAccount> GetOrCreatePlayer(string playerName);
    Task<(bool Success, string Error)> AuthenticateOrRegister(string playerName, string password);
    Task UpdateCareerStats(string playerName, string characterName, int kills, int deaths, int damageDealt, int healingDone, bool extracted, int xpGained = 0, int goldEarned = 0);
    Task SaveStash(string playerName, List<LootItem> items);
    Task<List<LootItem>> LoadStash(string playerName);
    Task RecordMatch(string playerName, MatchRecord record);
    Task<List<PlayerCharacter>> GetLeaderboardCharacters(string sortBy, int limit = 20);

    // Character CRUD
    Task<List<PlayerCharacter>> GetCharacters(string accountName);
    Task<PlayerCharacter?> GetCharacter(string accountName, string characterName);
    Task<(bool Success, string Error, PlayerCharacter? Character)> CreateCharacter(string accountName, string characterName, string playerClass, int appearance);
    Task<(bool Success, string Error)> DeleteCharacter(string accountName, string characterName);

    // Character Equipment
    Task SaveEquipment(string accountName, string characterName, Dictionary<string, LootItem> equipment);
    Task<Dictionary<string, LootItem>> LoadEquipment(string accountName, string characterName);

    // Character Backpack (consumables carried into dungeon)
    Task SaveBackpack(string accountName, string characterName, List<LootItem> backpack);
    Task<List<LootItem>> LoadBackpack(string accountName, string characterName);
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

    public async Task<(bool Success, string Error)> AuthenticateOrRegister(string playerName, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            return (false, "Password must be at least 4 characters");

        if (password.Length > 64)
            return (false, "Password must be at most 64 characters");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerName == playerName);

        if (player == null)
        {
            // New player — register
            var salt = GenerateSalt();
            var hash = HashPassword(password, salt);

            player = new PlayerAccount
            {
                PlayerName = playerName,
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            db.Players.Add(player);
            await db.SaveChangesAsync();
            _logger.LogInformation("Registered new player: {PlayerName}", playerName);
            return (true, string.Empty);
        }

        // Existing player — verify password
        if (string.IsNullOrEmpty(player.PasswordHash) || string.IsNullOrEmpty(player.PasswordSalt))
        {
            // Legacy account without password — set it now
            var salt = GenerateSalt();
            player.PasswordHash = HashPassword(password, salt);
            player.PasswordSalt = salt;
            player.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _logger.LogInformation("Set password for legacy account: {PlayerName}", playerName);
            return (true, string.Empty);
        }

        var expectedHash = HashPassword(password, player.PasswordSalt);
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(player.PasswordHash),
            Convert.FromBase64String(expectedHash)))
            return (false, "Incorrect password");

        player.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, string.Empty);
    }

    private static string GenerateSalt()
    {
        var saltBytes = new byte[16];
        RandomNumberGenerator.Fill(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }

    private static string HashPassword(string password, string salt)
    {
        var combined = Encoding.UTF8.GetBytes(password + salt);
        var hash = SHA256.HashData(combined);
        return Convert.ToBase64String(hash);
    }

    // ========================
    // Character CRUD
    // ========================

    public async Task<List<PlayerCharacter>> GetCharacters(string accountName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        return await db.Characters
            .Where(c => c.Account.PlayerName == accountName)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<PlayerCharacter?> GetCharacter(string accountName, string characterName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        return await db.Characters
            .FirstOrDefaultAsync(c => c.Account.PlayerName == accountName && c.CharacterName == characterName);
    }

    public async Task<(bool Success, string Error, PlayerCharacter? Character)> CreateCharacter(string accountName, string characterName, string playerClass, int appearance)
    {
        if (string.IsNullOrWhiteSpace(characterName) || characterName.Length < 3 || characterName.Length > 20)
            return (false, "Character name must be 3-20 characters", null);

        var validClasses = new[] { "scout", "tank", "support", "assassin", "warlock" };
        if (!validClasses.Contains(playerClass.ToLower()))
            return (false, "Invalid class", null);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var account = await db.Players.Include(p => p.Characters)
            .FirstOrDefaultAsync(p => p.PlayerName == accountName);
        if (account == null)
            return (false, "Account not found", null);

        if (account.Characters.Count >= 5)
            return (false, "Maximum 5 characters per account", null);

        // Check name uniqueness
        var nameExists = await db.Characters.AnyAsync(c => c.CharacterName == characterName);
        if (nameExists)
            return (false, "Character name already taken", null);

        var character = new PlayerCharacter
        {
            PlayerAccountId = account.Id,
            CharacterName = characterName,
            Class = playerClass.ToLower(),
            AppearancePreset = appearance,
            CreatedAt = DateTime.UtcNow
        };

        db.Characters.Add(character);
        await db.SaveChangesAsync();
        _logger.LogInformation("Created character '{CharacterName}' ({Class}) for account {Account}", characterName, playerClass, accountName);
        return (true, string.Empty, character);
    }

    public async Task<(bool Success, string Error)> DeleteCharacter(string accountName, string characterName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var character = await db.Characters
            .FirstOrDefaultAsync(c => c.Account.PlayerName == accountName && c.CharacterName == characterName);

        if (character == null)
            return (false, "Character not found");

        db.Characters.Remove(character);
        await db.SaveChangesAsync();
        _logger.LogInformation("Deleted character '{CharacterName}' from account {Account}", characterName, accountName);
        return (true, string.Empty);
    }

    // ========================
    // Character Equipment
    // ========================

    public async Task SaveEquipment(string accountName, string characterName, Dictionary<string, LootItem> equipment)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var character = await db.Characters
            .FirstOrDefaultAsync(c => c.Account.PlayerName == accountName && c.CharacterName == characterName);
        if (character == null) return;

        character.EquipmentJson = JsonConvert.SerializeObject(equipment);
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, LootItem>> LoadEquipment(string accountName, string characterName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var character = await db.Characters
            .FirstOrDefaultAsync(c => c.Account.PlayerName == accountName && c.CharacterName == characterName);
        if (character == null || string.IsNullOrWhiteSpace(character.EquipmentJson) || character.EquipmentJson == "{}")
            return new Dictionary<string, LootItem>();

        try
        {
            var result = JsonConvert.DeserializeObject<Dictionary<string, LootItem>>(character.EquipmentJson) ?? new();
            foreach (var item in result.Values)
                item.Properties = NormalizeProperties(item.Properties);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize equipment for {Character}", characterName);
            return new Dictionary<string, LootItem>();
        }
    }

    // ========================
    // Character Backpack
    // ========================

    public async Task SaveBackpack(string accountName, string characterName, List<LootItem> backpack)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var character = await db.Characters
            .FirstOrDefaultAsync(c => c.Account.PlayerName == accountName && c.CharacterName == characterName);
        if (character == null) return;

        character.BackpackJson = JsonConvert.SerializeObject(backpack);
        await db.SaveChangesAsync();
    }

    public async Task<List<LootItem>> LoadBackpack(string accountName, string characterName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var character = await db.Characters
            .FirstOrDefaultAsync(c => c.Account.PlayerName == accountName && c.CharacterName == characterName);
        if (character == null || string.IsNullOrWhiteSpace(character.BackpackJson) || character.BackpackJson == "[]")
            return new List<LootItem>();

        try
        {
            var result = JsonConvert.DeserializeObject<List<LootItem>>(character.BackpackJson) ?? new();
            foreach (var item in result)
                item.Properties = NormalizeProperties(item.Properties);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize backpack for {Character}", characterName);
            return new List<LootItem>();
        }
    }

    // ========================
    // Career Stats (now targets character, gold goes to account)
    // ========================

    public async Task UpdateCareerStats(string playerName, string characterName, int kills, int deaths, int damageDealt, int healingDone, bool extracted, int xpGained = 0, int goldEarned = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        // Update character stats
        var character = await db.Characters
            .FirstOrDefaultAsync(c => c.Account.PlayerName == playerName && c.CharacterName == characterName);
        if (character != null)
        {
            character.CareerKills += kills;
            character.CareerDeaths += deaths;
            character.CareerDamageDealt += damageDealt;
            character.CareerHealingDone += healingDone;
            character.TotalMatchesPlayed++;
            if (extracted) character.TotalExtractions++;
            character.TotalExperience += xpGained;
            character.CurrentLevel = CalculateLevel(character.TotalExperience);
        }

        // Gold goes to account
        var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerName == playerName);
        if (player != null)
        {
            player.Gold = Math.Max(0, player.Gold + goldEarned);
        }

        await db.SaveChangesAsync();
    }

    public static int CalculateLevel(long totalXP)
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

        // Explicitly delete existing rows (more reliable than orphan cascade)
        if (player.StashedItems.Count > 0)
            db.StashedItems.RemoveRange(player.StashedItems);

        // Deduplicate by LootId before saving
        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            if (!seen.Add(item.LootId)) continue; // Skip duplicates

            player.StashedItems.Add(new StashedItem
            {
                PlayerAccountId = player.Id,
                LootId = item.LootId,
                ItemName = item.ItemName,
                ItemType = item.ItemType,
                Rarity = item.Rarity,
                StackCount = item.StackCount,
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

        // Fix empty LootIds: generate and persist stable GUIDs
        bool needsSave = false;
        foreach (var s in player.StashedItems)
        {
            if (string.IsNullOrEmpty(s.LootId))
            {
                s.LootId = Guid.NewGuid().ToString();
                needsSave = true;
            }
        }
        if (needsSave)
            await db.SaveChangesAsync();

        return player.StashedItems.Select(s => new LootItem
        {
            LootId = s.LootId,
            ItemName = s.ItemName,
            ItemType = s.ItemType,
            Rarity = s.Rarity,
            StackCount = s.StackCount,
            Weight = s.Weight,
            Value = s.Value,
            Properties = NormalizeProperties(JsonConvert.DeserializeObject<Dictionary<string, object>>(s.PropertiesJson) ?? new()),
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

    // ========================
    // Leaderboard (now queries characters)
    // ========================

    public async Task<List<PlayerCharacter>> GetLeaderboardCharacters(string sortBy, int limit = 20)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MazeWarsDbContext>();

        var query = db.Characters.Include(c => c.Account).Where(c => c.TotalMatchesPlayed > 0);

        query = sortBy switch
        {
            "kills" => query.OrderByDescending(c => c.CareerKills),
            "level" => query.OrderByDescending(c => c.CurrentLevel).ThenByDescending(c => c.TotalExperience),
            "extractions" => query.OrderByDescending(c => c.TotalExtractions),
            "kd" => query.OrderByDescending(c => c.CareerDeaths > 0 ? (double)c.CareerKills / c.CareerDeaths : c.CareerKills),
            "damage" => query.OrderByDescending(c => c.CareerDamageDealt),
            "healing" => query.OrderByDescending(c => c.CareerHealingDone),
            _ => query.OrderByDescending(c => c.CurrentLevel).ThenByDescending(c => c.TotalExperience)
        };

        return await query.Take(limit).ToListAsync();
    }

    /// <summary>Convert Newtonsoft JValue/JToken objects to native .NET types for System.Text.Json compatibility</summary>
    private static Dictionary<string, object> NormalizeProperties(Dictionary<string, object> props)
    {
        var result = new Dictionary<string, object>(props.Count);
        foreach (var (key, value) in props)
        {
            result[key] = value switch
            {
                Newtonsoft.Json.Linq.JValue jv => jv.Value ?? string.Empty,
                Newtonsoft.Json.Linq.JArray ja => ja.ToString(),
                Newtonsoft.Json.Linq.JObject jo => jo.ToString(),
                _ => value
            };
        }
        return result;
    }
}
