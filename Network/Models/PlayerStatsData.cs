using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Player statistics sent on connection.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PlayerStatsData
{
    [Key(0)]
    public int Level { get; set; }

    [Key(1)]
    public int Health { get; set; }

    [Key(2)]
    public int MaxHealth { get; set; }

    [Key(3)]
    public int Mana { get; set; }

    [Key(4)]
    public int MaxMana { get; set; }

    [Key(5)]
    public int ExperiencePoints { get; set; }
}
