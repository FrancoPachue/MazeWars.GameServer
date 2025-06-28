namespace MazeWars.GameServer.Network.Models;

public class PlayerStats
{
    public int Level { get; set; } = 1;
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int ExperiencePoints { get; set; }
    public Dictionary<string, int> BaseStats { get; set; } = new();
}
