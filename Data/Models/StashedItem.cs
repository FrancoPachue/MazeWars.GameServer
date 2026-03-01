using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MazeWars.GameServer.Data.Models;

public class StashedItem
{
    [Key]
    public int Id { get; set; }

    public int PlayerAccountId { get; set; }

    [ForeignKey(nameof(PlayerAccountId))]
    public PlayerAccount Player { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string ItemName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ItemType { get; set; } = string.Empty;

    public int Rarity { get; set; }
    public int StackCount { get; set; } = 1;
    public float Weight { get; set; }
    public int Value { get; set; }

    /// <summary>Serialized dictionary of item properties (JSON)</summary>
    public string PropertiesJson { get; set; } = "{}";

    /// <summary>Serialized dictionary of item stats (JSON)</summary>
    public string StatsJson { get; set; } = "{}";
}
