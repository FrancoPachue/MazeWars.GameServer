using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Match summary sent to a player upon extraction or game end.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class MatchSummaryData
{
    [Key(0)] public string PlayerName { get; set; } = string.Empty;
    [Key(1)] public string TeamId { get; set; } = string.Empty;
    [Key(2)] public string WinningTeam { get; set; } = string.Empty;
    [Key(3)] public bool Extracted { get; set; }
    [Key(4)] public int Kills { get; set; }
    [Key(5)] public int Deaths { get; set; }
    [Key(6)] public int DamageDealt { get; set; }
    [Key(7)] public int HealingDone { get; set; }
    [Key(8)] public int ItemsExtracted { get; set; }
    [Key(9)] public int ExtractionValue { get; set; }
    [Key(10)] public int XpGained { get; set; }
    [Key(11)] public int FinalLevel { get; set; }
    [Key(12)] public double GameDurationSeconds { get; set; }
    [Key(13)] public long AccountTotalXP { get; set; }
    [Key(14)] public int AccountLevel { get; set; }
    [Key(15)] public int MobKills { get; set; }
    [Key(16)] public int ContainersLooted { get; set; }
    [Key(17)] public int GoldEarned { get; set; }
    [Key(18)] public long AccountGold { get; set; }
}
