using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Lightweight status effect data sent to clients for HUD display.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class StatusEffectData
{
    [Key(0)] public string EffectType { get; set; } = string.Empty;
    [Key(1)] public int RemainingMs { get; set; }
}

/// <summary>
/// Simplified player state for batch updates.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PlayerUpdateData
{
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(1)]
    public Vector2 Position { get; set; }

    [Key(2)]
    public Vector2 Velocity { get; set; }

    [Key(3)]
    public float Direction { get; set; }

    [Key(4)]
    public int Health { get; set; }

    [Key(5)]
    public int MaxHealth { get; set; }

    [Key(6)]
    public int Mana { get; set; }

    [Key(7)]
    public int MaxMana { get; set; }

    [Key(8)]
    public int Level { get; set; }

    [Key(9)]
    public bool IsAlive { get; set; }

    [Key(10)]
    public bool IsMoving { get; set; }

    [Key(11)]
    public bool IsCasting { get; set; }

    [Key(12)]
    public string CurrentRoomId { get; set; } = string.Empty;

    [Key(13)]
    public Vector2? MoveTarget { get; set; }

    [Key(14)]
    public string WeaponItemId { get; set; } = string.Empty;

    [Key(15)]
    public string ChestItemId { get; set; } = string.Empty;

    [Key(16)]
    public string TeamId { get; set; } = string.Empty;

    [Key(17)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(18)]
    public string PlayerClass { get; set; } = string.Empty;

    [Key(19)]
    public int AttackSpeedMs { get; set; } = 1000;

    [Key(20)]
    public float AttackRange { get; set; } = 1.5f;

    [Key(21)]
    public List<StatusEffectData>? ActiveEffects { get; set; }

    [Key(22)]
    public int ExperiencePoints { get; set; }

    [Key(23)]
    public int XpToNextLevel { get; set; }

    [Key(24)]
    public string HeadItemId { get; set; } = string.Empty;

    [Key(25)]
    public string BootsItemId { get; set; } = string.Empty;

    [Key(26)]
    public string OffhandItemId { get; set; } = string.Empty;
}
