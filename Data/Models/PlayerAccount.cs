using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Data.Models;

public class PlayerAccount
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string PlayerName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? PasswordHash { get; set; }

    [MaxLength(64)]
    public string? PasswordSalt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    // Progression
    public int CurrentLevel { get; set; } = 1;
    public long TotalExperience { get; set; }

    // Economy
    public long Gold { get; set; }

    // Career stats
    public int CareerKills { get; set; }
    public int CareerDeaths { get; set; }
    public long CareerDamageDealt { get; set; }
    public long CareerHealingDone { get; set; }
    public int TotalMatchesPlayed { get; set; }
    public int TotalExtractions { get; set; }

    // Navigation
    public List<PlayerCharacter> Characters { get; set; } = new();
    public List<StashedItem> StashedItems { get; set; } = new();
    public List<MatchRecord> MatchHistory { get; set; } = new();
}
