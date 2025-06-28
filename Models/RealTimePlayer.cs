using System.Net;

namespace MazeWars.GameServer.Models;

public class RealTimePlayer
{
    // Identity
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string PlayerClass { get; set; } = "scout";

    // Network
    public IPEndPoint EndPoint { get; set; } = null!;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    // Position & Movement
    public Vector2 Position { get; set; } = new();
    public Vector2 Velocity { get; set; } = new();
    public float Direction { get; set; } = 0f;
    public bool IsMoving { get; set; }
    public bool IsSprinting { get; set; }

    // Combat & Stats
    public bool IsAlive { get; set; } = true;
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Mana { get; set; } = 100;
    public int MaxMana { get; set; } = 100;
    public int Shield { get; set; } = 0;
    public int MaxShield { get; set; } = 0;
    public DateTime LastAttackTime { get; set; }
    public DateTime LastDamageTime { get; set; }
    public DateTime DeathTime { get; set; }

    // Progression
    public int Level { get; set; } = 1;
    public int ExperiencePoints { get; set; } = 0;
    public Dictionary<string, int> Stats { get; set; } = new();

    // World State
    public string CurrentRoomId { get; set; } = "room_1_1";
    public List<LootItem> Inventory { get; set; } = new();

    // Abilities & Effects
    public Dictionary<string, DateTime> AbilityCooldowns { get; set; } = new();
    public bool IsCasting { get; set; }
    public DateTime CastingUntil { get; set; }
    public List<StatusEffect> StatusEffects { get; set; } = new();
    public float DamageReduction { get; set; } = 0f;
    public float MovementSpeedModifier { get; set; } = 1f;
}
