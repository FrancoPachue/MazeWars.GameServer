using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class LeaderboardRequestMessage
{
    [Key(0)] public string SortBy { get; set; } = "level";
}

[MessagePackObject(keyAsPropertyName: false)]
public class LeaderboardEntry
{
    [Key(0)] public int Rank { get; set; }
    [Key(1)] public string PlayerName { get; set; } = string.Empty;
    [Key(2)] public int Level { get; set; }
    [Key(3)] public int Kills { get; set; }
    [Key(4)] public int Deaths { get; set; }
    [Key(5)] public int Extractions { get; set; }
    [Key(6)] public long DamageDealt { get; set; }
    [Key(7)] public long HealingDone { get; set; }
    [Key(8)] public int MatchesPlayed { get; set; }
}

[MessagePackObject(keyAsPropertyName: false)]
public class LeaderboardData
{
    [Key(0)] public string SortBy { get; set; } = "level";
    [Key(1)] public List<LeaderboardEntry> Entries { get; set; } = new();
}
