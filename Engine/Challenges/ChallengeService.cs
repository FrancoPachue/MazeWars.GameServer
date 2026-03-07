using System.Collections.Concurrent;
using MazeWars.GameServer.Network.Models;

namespace MazeWars.GameServer.Engine.Challenges;

/// <summary>
/// Manages daily (3) and weekly (1) challenges. Resets automatically.
/// Progress is tracked per-player in memory.
/// </summary>
public class ChallengeService
{
    private readonly ILogger<ChallengeService> _logger;

    // Active challenges (rotated on reset)
    private List<ChallengeDefinition> _activeDailies = new();
    private ChallengeDefinition? _activeWeekly;
    private DateTime _dailyResetUtc;
    private DateTime _weeklyResetUtc;

    // Player progress: playerName → challengeId → current progress
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _progress = new();

    // All possible challenges
    private static readonly List<ChallengeDefinition> DailyPool = new()
    {
        new("kill_mobs_10", "Slay 10 Monsters", "Kill 10 mobs in a single match", ChallengeType.Daily, "mob_kills", 10, 50),
        new("kill_mobs_25", "Monster Hunter", "Kill 25 mobs across matches", ChallengeType.Daily, "mob_kills", 25, 100),
        new("deal_damage_500", "Damage Dealer", "Deal 500 damage to enemies", ChallengeType.Daily, "damage_dealt", 500, 75),
        new("deal_damage_2000", "Wrecking Ball", "Deal 2000 damage total", ChallengeType.Daily, "damage_dealt", 2000, 150),
        new("heal_200", "Field Medic", "Heal 200 HP across matches", ChallengeType.Daily, "healing_done", 200, 75),
        new("heal_500", "Angel of Mercy", "Heal 500 HP total", ChallengeType.Daily, "healing_done", 500, 125),
        new("extract_1", "Escape Artist", "Extract from a dungeon", ChallengeType.Daily, "extractions", 1, 100),
        new("extract_2", "Dungeon Runner", "Extract from 2 dungeons", ChallengeType.Daily, "extractions", 2, 175),
        new("loot_items_5", "Treasure Hunter", "Collect 5 loot items", ChallengeType.Daily, "items_looted", 5, 50),
        new("loot_items_15", "Hoarder", "Collect 15 loot items", ChallengeType.Daily, "items_looted", 15, 100),
        new("player_kills_1", "First Blood", "Kill a player", ChallengeType.Daily, "player_kills", 1, 100),
        new("player_kills_3", "Triple Threat", "Kill 3 players", ChallengeType.Daily, "player_kills", 3, 200),
        new("open_chests_3", "Chest Cracker", "Open 3 chests", ChallengeType.Daily, "chests_opened", 3, 60),
        new("play_matches_2", "Regular", "Play 2 matches", ChallengeType.Daily, "matches_played", 2, 75),
    };

    private static readonly List<ChallengeDefinition> WeeklyPool = new()
    {
        new("weekly_kills_50", "Slaughter Week", "Kill 50 mobs this week", ChallengeType.Weekly, "mob_kills", 50, 500),
        new("weekly_extract_5", "Master Escapist", "Extract 5 times this week", ChallengeType.Weekly, "extractions", 5, 500),
        new("weekly_damage_5000", "Destruction Week", "Deal 5000 damage this week", ChallengeType.Weekly, "damage_dealt", 5000, 500),
        new("weekly_matches_5", "Dedicated Player", "Play 5 matches this week", ChallengeType.Weekly, "matches_played", 5, 400),
        new("weekly_player_kills_5", "PvP Champion", "Kill 5 players this week", ChallengeType.Weekly, "player_kills", 5, 600),
        new("weekly_heal_2000", "Support Star", "Heal 2000 HP this week", ChallengeType.Weekly, "healing_done", 2000, 500),
    };

    private readonly object _rotateLock = new();

    public ChallengeService(ILogger<ChallengeService> logger)
    {
        _logger = logger;
        RotateChallenges();
    }

    private void RotateChallenges()
    {
        // Pick 3 random dailies
        var shuffled = DailyPool.OrderBy(_ => Random.Shared.Next()).ToList();
        _activeDailies = shuffled.Take(3).ToList();

        // Pick 1 random weekly
        _activeWeekly = WeeklyPool[Random.Shared.Next(WeeklyPool.Count)];

        // Reset times
        var now = DateTime.UtcNow;
        _dailyResetUtc = now.Date.AddDays(1); // next midnight UTC
        _weeklyResetUtc = now.Date.AddDays(7 - (int)now.DayOfWeek); // next Sunday
        if (_weeklyResetUtc <= now) _weeklyResetUtc = _weeklyResetUtc.AddDays(7);

        _progress.Clear();

        _logger.LogInformation("Challenges rotated: Dailies=[{Dailies}], Weekly={Weekly}",
            string.Join(", ", _activeDailies.Select(d => d.Id)),
            _activeWeekly.Id);
    }

    private void CheckReset()
    {
        var now = DateTime.UtcNow;
        if (now < _dailyResetUtc && now < _weeklyResetUtc) return; // Fast path without lock

        lock (_rotateLock)
        {
            now = DateTime.UtcNow; // Re-check inside lock
            if (now >= _dailyResetUtc)
            {
                // Rotate dailies, keep weekly if not expired
                var shuffled = DailyPool.OrderBy(_ => Random.Shared.Next()).ToList();
                _activeDailies = shuffled.Take(3).ToList();
                _dailyResetUtc = now.Date.AddDays(1);

                // Clear daily progress only
                foreach (var (_, playerProgress) in _progress)
                {
                    foreach (var daily in DailyPool)
                        playerProgress.TryRemove(daily.Id, out _);
                }

                _logger.LogInformation("Daily challenges rotated");
            }

            if (now >= _weeklyResetUtc)
            {
                _activeWeekly = WeeklyPool[Random.Shared.Next(WeeklyPool.Count)];
                _weeklyResetUtc = now.Date.AddDays(7 - (int)now.DayOfWeek);
                if (_weeklyResetUtc <= now) _weeklyResetUtc = _weeklyResetUtc.AddDays(7);

                // Clear weekly progress
                foreach (var (_, playerProgress) in _progress)
                {
                    foreach (var weekly in WeeklyPool)
                        playerProgress.TryRemove(weekly.Id, out _);
                }

                _logger.LogInformation("Weekly challenge rotated");
            }
        }
    }

    public ChallengesData GetChallenges(string playerName)
    {
        CheckReset();

        var playerProgress = _progress.GetOrAdd(playerName, _ => new ConcurrentDictionary<string, int>());
        var entries = new List<ChallengeEntryData>();

        foreach (var daily in _activeDailies)
        {
            playerProgress.TryGetValue(daily.Id, out var progress);
            entries.Add(new ChallengeEntryData
            {
                ChallengeId = daily.Id,
                Title = daily.Title,
                Description = daily.Description,
                Type = "daily",
                TargetValue = daily.Target,
                CurrentValue = Math.Min(progress, daily.Target),
                GoldReward = daily.GoldReward,
                Completed = progress >= daily.Target
            });
        }

        if (_activeWeekly != null)
        {
            playerProgress.TryGetValue(_activeWeekly.Id, out var weeklyProgress);
            entries.Add(new ChallengeEntryData
            {
                ChallengeId = _activeWeekly.Id,
                Title = _activeWeekly.Title,
                Description = _activeWeekly.Description,
                Type = "weekly",
                TargetValue = _activeWeekly.Target,
                CurrentValue = Math.Min(weeklyProgress, _activeWeekly.Target),
                GoldReward = _activeWeekly.GoldReward,
                Completed = weeklyProgress >= _activeWeekly.Target
            });
        }

        return new ChallengesData
        {
            Entries = entries,
            DailyResetIn = (int)(_dailyResetUtc - DateTime.UtcNow).TotalSeconds,
            WeeklyResetIn = (int)(_weeklyResetUtc - DateTime.UtcNow).TotalSeconds
        };
    }

    /// <summary>
    /// Update progress for a stat type. Called from GameEngine after match events.
    /// </summary>
    public void UpdateProgress(string playerName, string statKey, int amount)
    {
        CheckReset();
        var playerProgress = _progress.GetOrAdd(playerName, _ => new ConcurrentDictionary<string, int>());

        foreach (var daily in _activeDailies)
        {
            if (daily.StatKey == statKey)
                playerProgress.AddOrUpdate(daily.Id, amount, (_, current) => current + amount);
        }

        if (_activeWeekly?.StatKey == statKey)
            playerProgress.AddOrUpdate(_activeWeekly.Id, amount, (_, current) => current + amount);
    }

    /// <summary>
    /// Claim gold reward for a completed challenge.
    /// </summary>
    public (bool Success, string Error, int GoldReward) ClaimReward(string playerName, string challengeId)
    {
        var playerProgress = _progress.GetOrAdd(playerName, _ => new ConcurrentDictionary<string, int>());

        // Find the challenge
        ChallengeDefinition? challenge = _activeDailies.FirstOrDefault(d => d.Id == challengeId);
        challenge ??= _activeWeekly?.Id == challengeId ? _activeWeekly : null;

        if (challenge == null)
            return (false, "Challenge not found", 0);

        playerProgress.TryGetValue(challengeId, out var progress);
        if (progress < challenge.Target)
            return (false, "Challenge not completed yet", 0);

        // Atomically mark as claimed — TryAdd returns false if key already exists
        if (!playerProgress.TryAdd(challengeId + "_claimed", 1))
            return (false, "Already claimed", 0);
        return (true, string.Empty, challenge.GoldReward);
    }

    public bool IsClaimed(string playerName, string challengeId)
    {
        if (!_progress.TryGetValue(playerName, out var playerProgress))
            return false;
        return playerProgress.ContainsKey(challengeId + "_claimed");
    }
}

public record ChallengeDefinition(
    string Id, string Title, string Description,
    ChallengeType Type, string StatKey, int Target, int GoldReward);

public enum ChallengeType { Daily, Weekly }
