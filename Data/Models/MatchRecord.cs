using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MazeWars.GameServer.Data.Models;

public class MatchRecord
{
    [Key]
    public int Id { get; set; }

    public int PlayerAccountId { get; set; }

    [ForeignKey(nameof(PlayerAccountId))]
    public PlayerAccount Player { get; set; } = null!;

    public DateTime MatchStartTime { get; set; }
    public DateTime MatchEndTime { get; set; }

    public bool Extracted { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int DamageDealt { get; set; }
    public int HealingDone { get; set; }
    public int ItemsExtracted { get; set; }
    public int ExtractionValue { get; set; }
    public int XpGained { get; set; }
    public int GoldEarned { get; set; }
}
