namespace MazeWars.GameServer.Network.Models;

public class EnhancedPlayerStateUpdate : PlayerStateUpdate
{
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int Level { get; set; }
    public int ExperiencePoints { get; set; }
    public string CurrentRoomId { get; set; } = string.Empty;
    public List<ActiveStatusEffect> StatusEffects { get; set; } = new();
    public int InventoryCount { get; set; }
    public float MovementSpeedModifier { get; set; } = 1f;
    public int Shield { get; set; }
    public int MaxShield { get; set; }
}
