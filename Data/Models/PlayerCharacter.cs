using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Data.Models;

public class PlayerCharacter
{
    [Key]
    public int Id { get; set; }

    public int PlayerAccountId { get; set; }
    public PlayerAccount Account { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string CharacterName { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Class { get; set; } = "scout";

    public int AppearancePreset { get; set; }

    // Progression
    public int CurrentLevel { get; set; } = 1;
    public long TotalExperience { get; set; }

    // Career stats
    public int CareerKills { get; set; }
    public int CareerDeaths { get; set; }
    public long CareerDamageDealt { get; set; }
    public long CareerHealingDone { get; set; }
    public int TotalMatchesPlayed { get; set; }
    public int TotalExtractions { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>JSON-serialized equipment loadout: Dictionary&lt;slotName, LootItem&gt;</summary>
    public string EquipmentJson { get; set; } = "{}";

    /// <summary>JSON-serialized backpack items (consumables/potions): List&lt;LootItem&gt;</summary>
    public string BackpackJson { get; set; } = "[]";
}
