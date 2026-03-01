namespace MazeWars.GameServer.Engine.Equipment.Models;

public class EquipmentDefinition
{
    public string ItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public EquipmentSlot Slot { get; set; }
    public int Tier { get; set; } = 1;
    public int RequiredLevel { get; set; } = 1;
    public bool IsTwoHanded { get; set; }

    // Flat bonuses
    public int BonusHealth { get; set; }
    public int BonusMana { get; set; }

    // Percentage bonuses
    public float BonusDamagePercent { get; set; }
    public float BonusHealingPercent { get; set; }
    public float DamageReduction { get; set; }
    public float MovementSpeedBonus { get; set; }
    public float CooldownReduction { get; set; }
    public float AttackSpeedBonus { get; set; }
    public float CritChance { get; set; }

    // Regen
    public float HealthRegenPerSecond { get; set; }
    public float ManaRegenPerSecond { get; set; }

    // Weight
    public float Weight { get; set; } = 1.0f;

    // Abilities granted (IDs from AbilityRegistry)
    public List<string> GrantedAbilities { get; set; } = new();
}
