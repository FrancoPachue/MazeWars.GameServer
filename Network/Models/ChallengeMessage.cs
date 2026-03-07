using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class ChallengeEntryData
{
    [Key(0)] public string ChallengeId { get; set; } = string.Empty;
    [Key(1)] public string Title { get; set; } = string.Empty;
    [Key(2)] public string Description { get; set; } = string.Empty;
    [Key(3)] public string Type { get; set; } = "daily";
    [Key(4)] public int TargetValue { get; set; }
    [Key(5)] public int CurrentValue { get; set; }
    [Key(6)] public int GoldReward { get; set; }
    [Key(7)] public bool Completed { get; set; }
}

[MessagePackObject(keyAsPropertyName: false)]
public class ChallengesData
{
    [Key(0)] public List<ChallengeEntryData> Entries { get; set; } = new();
    [Key(1)] public int DailyResetIn { get; set; }
    [Key(2)] public int WeeklyResetIn { get; set; }
}

[MessagePackObject(keyAsPropertyName: false)]
public class ClaimChallengeMessage
{
    [Key(0)] public string ChallengeId { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: false)]
public class ClaimChallengeResponseData
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string Error { get; set; } = string.Empty;
    [Key(2)] public int GoldReward { get; set; }
    [Key(3)] public long TotalGold { get; set; }
}
