using System.Net;
using MazeWars.GameServer.Engine.Equipment.Models;

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

    // Click-to-move state (server-side)
    public Vector2? MoveTarget { get; set; }
    public bool IsPathing { get; set; }

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
    public DateTime LastDashTime { get; set; }

    // Progression
    public int Level { get; set; } = 1;
    public int ExperiencePoints { get; set; } = 0;

    // World State
    public string CurrentRoomId { get; set; } = "room_1_1";
    public List<LootItem> Inventory { get; set; } = new();

    // Equipment (6 slots)
    public Dictionary<EquipmentSlot, LootItem> Equipment { get; set; } = new();
    public int EquipmentBonusHealth { get; set; }
    public int EquipmentBonusMana { get; set; }
    public float EquipmentDamageReduction { get; set; }
    public float EquipmentSpeedBonus { get; set; }
    public float EquipmentManaRegen { get; set; }

    // Direct bonus system (replaces Stats)
    public float BonusDamagePercent { get; set; }
    public float BonusHealingPercent { get; set; }
    public float CritChance { get; set; }
    public float AttackSpeedBonus { get; set; }
    public float CooldownReduction { get; set; }
    public float HealthRegenPerSecond { get; set; }
    public float BaseManaRegenPerSecond { get; set; } = 5f;

    // Regen accumulators (fractional HP/mana accumulated until >= 1)
    public float HealthRegenAccumulator { get; set; }
    public float ManaRegenAccumulator { get; set; }

    // Per-run level bonuses (accumulated)
    public float LevelBonusDamagePercent { get; set; }
    public float LevelBonusHealingPercent { get; set; }
    public int LevelBonusHealth { get; set; }
    public int LevelBonusMana { get; set; }

    // Weight / Encumbrance
    public float CurrentWeight { get; set; }

    /// <summary>ID of dead player whose soul is being carried (null if not carrying)</summary>
    public string? CarryingSoulOfPlayerId { get; set; }

    // Thread safety for inventory operations
    public readonly object InventoryLock = new();

    // Match statistics
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int DamageDealt { get; set; }
    public int HealingDone { get; set; }
    public int MobKills { get; set; }
    public int ContainersLooted { get; set; }

    // Abilities & Effects
    public Dictionary<string, DateTime> AbilityCooldowns { get; set; } = new();
    public bool IsCasting { get; set; }
    public DateTime CastingUntil { get; set; }
    public string? ChannelingAbility { get; set; }
    public string? ChannelingTargetId { get; set; }
    public DateTime ChannelingStartTime { get; set; }
    public float ChannelingDuration { get; set; }
    public List<StatusEffect> StatusEffects { get; set; } = new();
    public float DamageReduction { get; set; } = 0f;
    public float MovementSpeedModifier { get; set; } = 1f;

    // Lag compensation: position history for hit validation
    private readonly (Vector2 Position, float Time)[] _positionHistory = new (Vector2, float)[12];
    private int _positionHistoryIndex = 0;
    private int _positionHistoryCount = 0;

    public void RecordPosition(float serverTime)
    {
        _positionHistory[_positionHistoryIndex] = (Position, serverTime);
        _positionHistoryIndex = (_positionHistoryIndex + 1) % _positionHistory.Length;
        _positionHistoryCount = Math.Min(_positionHistoryCount + 1, _positionHistory.Length);
    }

    public Vector2 GetPositionAtTime(float targetTime)
    {
        if (_positionHistoryCount == 0) return Position;

        // Find the two closest samples (before and after targetTime) for interpolation
        int beforeIdx = -1, afterIdx = -1;
        float beforeTime = float.MinValue, afterTime = float.MaxValue;

        for (int i = 0; i < _positionHistoryCount; i++)
        {
            var t = _positionHistory[i].Time;
            if (t <= targetTime && t > beforeTime)
            {
                beforeTime = t;
                beforeIdx = i;
            }
            if (t >= targetTime && t < afterTime)
            {
                afterTime = t;
                afterIdx = i;
            }
        }

        if (beforeIdx == -1 && afterIdx == -1) return Position;
        if (beforeIdx == -1) return _positionHistory[afterIdx].Position;
        if (afterIdx == -1) return _positionHistory[beforeIdx].Position;
        if (beforeIdx == afterIdx) return _positionHistory[beforeIdx].Position;

        // Linear interpolation between the two nearest samples
        var timeDiff = afterTime - beforeTime;
        if (timeDiff <= 0) return _positionHistory[beforeIdx].Position;

        var t_factor = (targetTime - beforeTime) / timeDiff;
        var from = _positionHistory[beforeIdx].Position;
        var to = _positionHistory[afterIdx].Position;

        return new Vector2(
            from.X + (to.X - from.X) * t_factor,
            from.Y + (to.Y - from.Y) * t_factor
        );
    }

    // ⭐ PERF: Delta Compression - Track last sent state
    private Vector2 _lastSentPosition = new();
    private Vector2 _lastSentVelocity = new();
    private float _lastSentDirection = 0f;
    private int _lastSentHealth = 100;
    private int _lastSentMaxHealth = 100;
    private bool _lastSentIsAlive = true;
    private bool _lastSentIsMoving = false;
    private bool _lastSentIsCasting = false;
    private Vector2? _lastSentMoveTarget;
    private string _lastSentWeaponId = string.Empty;
    private string _lastSentChestId = string.Empty;
    private int _lastSentEffectCount = -1;

    // Thresholds for determining significant changes
    private const float POSITION_THRESHOLD = 0.01f; // 1cm
    private const float VELOCITY_THRESHOLD = 0.01f;
    private const float DIRECTION_THRESHOLD = 0.5f; // ~0.5 degrees

    /// <summary>
    /// ⭐ DELTA COMPRESSION: Check if player has changed significantly since last update
    /// Reduces bandwidth by 70-90% by only sending changed players
    /// </summary>
    public bool HasSignificantChange()
    {
        // Position changed significantly
        if (Math.Abs(Position.X - _lastSentPosition.X) > POSITION_THRESHOLD ||
            Math.Abs(Position.Y - _lastSentPosition.Y) > POSITION_THRESHOLD)
        {
            return true;
        }

        // Velocity changed significantly
        if (Math.Abs(Velocity.X - _lastSentVelocity.X) > VELOCITY_THRESHOLD ||
            Math.Abs(Velocity.Y - _lastSentVelocity.Y) > VELOCITY_THRESHOLD)
        {
            return true;
        }

        // Direction changed
        if (Math.Abs(Direction - _lastSentDirection) > DIRECTION_THRESHOLD)
        {
            return true;
        }

        // Critical state changes
        if (Health != _lastSentHealth ||
            MaxHealth != _lastSentMaxHealth ||
            IsAlive != _lastSentIsAlive ||
            IsMoving != _lastSentIsMoving ||
            IsCasting != _lastSentIsCasting)
        {
            return true;
        }

        // MoveTarget changed (click-to-move destination)
        if (MoveTarget != _lastSentMoveTarget)
        {
            return true;
        }

        // Equipment visual changed
        var weaponId = Equipment.TryGetValue(EquipmentSlot.Weapon, out var w) && w.Properties.TryGetValue("equipment_id", out var wId) ? wId?.ToString() ?? string.Empty : string.Empty;
        var chestId = Equipment.TryGetValue(EquipmentSlot.Chest, out var c) && c.Properties.TryGetValue("equipment_id", out var cId) ? cId?.ToString() ?? string.Empty : string.Empty;
        if (weaponId != _lastSentWeaponId || chestId != _lastSentChestId)
        {
            return true;
        }

        // Status effects changed (added/removed)
        if (StatusEffects.Count != _lastSentEffectCount)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// ⭐ DELTA COMPRESSION: Mark current state as sent
    /// Call after successfully sending update to client
    /// </summary>
    public void MarkAsSent()
    {
        _lastSentPosition = Position;
        _lastSentVelocity = Velocity;
        _lastSentDirection = Direction;
        _lastSentHealth = Health;
        _lastSentMaxHealth = MaxHealth;
        _lastSentIsAlive = IsAlive;
        _lastSentIsMoving = IsMoving;
        _lastSentIsCasting = IsCasting;
        _lastSentMoveTarget = MoveTarget;
        _lastSentWeaponId = Equipment.TryGetValue(EquipmentSlot.Weapon, out var w) && w.Properties.TryGetValue("equipment_id", out var wId) ? wId?.ToString() ?? string.Empty : string.Empty;
        _lastSentChestId = Equipment.TryGetValue(EquipmentSlot.Chest, out var c) && c.Properties.TryGetValue("equipment_id", out var cId) ? cId?.ToString() ?? string.Empty : string.Empty;
        _lastSentEffectCount = StatusEffects.Count;
    }

    /// <summary>
    /// Force next update to include this player (e.g., after respawn, teleport)
    /// </summary>
    public void ForceNextUpdate()
    {
        // Reset all last sent values to trigger HasSignificantChange
        _lastSentPosition = new Vector2(float.MaxValue, float.MaxValue);
        _lastSentVelocity = new Vector2(float.MaxValue, float.MaxValue);
        _lastSentDirection = float.MaxValue;
        _lastSentHealth = -1;
        _lastSentMaxHealth = -1;
        _lastSentIsAlive = !IsAlive;
        _lastSentIsMoving = !IsMoving;
        _lastSentIsCasting = !IsCasting;
        _lastSentMoveTarget = new Vector2(float.MaxValue, float.MaxValue);
        _lastSentWeaponId = "\0";
        _lastSentChestId = "\0";
        _lastSentEffectCount = -1;
    }
}
