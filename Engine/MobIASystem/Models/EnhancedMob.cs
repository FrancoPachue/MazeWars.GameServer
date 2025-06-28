using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.MobIASystem.Models;

// =============================================
// ENHANCED MOB MODEL
// =============================================

public class EnhancedMob : Mob
{
    // AI State
    public MobState CurrentState { get; set; } = MobState.Spawning;
    public MobState PreviousState { get; set; } = MobState.Spawning;
    public DateTime StateChangedAt { get; set; } = DateTime.UtcNow;
    public string StateChangeReason { get; set; } = string.Empty;

    // AI Behavior
    public Vector2 OriginalPosition { get; set; }
    public Vector2 LastKnownPlayerPosition { get; set; }
    public string? TargetPlayerId { get; set; }
    public DateTime LastPlayerSeen { get; set; }
    public float AggressionLevel { get; set; } = 1.0f;
    public AIProcessingPriority ProcessingPriority { get; set; } = AIProcessingPriority.Low;

    // Combat
    public DateTime LastAttackTime { get; set; }
    public DateTime LastAbilityTime { get; set; }
    public List<string> KnownPlayers { get; set; } = new(); // Players it has detected
    public int DamageTaken { get; set; }
    public string? LastAttacker { get; set; }

    // Group Behavior
    public string? GroupId { get; set; }
    public List<string> AlliedMobs { get; set; } = new();
    public bool IsGroupLeader { get; set; }
    public DateTime LastHelpCall { get; set; }

    // Performance
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public float DistanceToNearestPlayer { get; set; } = float.MaxValue;
    public bool RequiresUpdate { get; set; } = true;

    // Template Reference
    public MobTemplate? Template { get; set; }
    public MobStats EnhancedStats { get; set; } = new();

    // Abilities
    public Dictionary<string, DateTime> AbilityCooldowns { get; set; } = new();
    public List<StatusEffect> StatusEffects { get; set; } = new();

    // Pathfinding
    public List<Vector2> CurrentPath { get; set; } = new();
    public int CurrentPathIndex { get; set; }
    public DateTime PathCalculatedAt { get; set; }
    public bool IsPathBlocked { get; set; }

    // ⭐ CORREGIDO: Movement tracking agregado
    public List<Vector2> RecentPositions { get; set; } = new();
    public DateTime LastPositionUpdate { get; set; } = DateTime.UtcNow;

    // Debug and Analytics
    public int StateChanges { get; set; }
    public int AttacksPerformed { get; set; }
    public int AbilitiesUsed { get; set; }
    public TimeSpan TotalLifetime => DateTime.UtcNow - StateChangedAt;
}

