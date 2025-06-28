using MazeWars.GameServer.Engine.Combat.Models;
using MazeWars.GameServer.Engine.MobIASystem.Models;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using System.Runtime;

namespace MazeWars.GameServer.Engine.AI.Interface;

// =============================================
// MOB AI SYSTEM INTERFACE
// =============================================

public interface IMobAISystem : IDisposable
{
    // =============================================
    // CORE MOB MANAGEMENT
    // =============================================

    /// <summary>
    /// Initializes the AI system with mob templates
    /// </summary>
    void InitializeMobTemplates(Dictionary<string, MobTemplate> templates);

    /// <summary>
    /// Spawns initial mobs for a world
    /// </summary>
    List<Mob> SpawnInitialMobs(GameWorld world);

    /// <summary>
    /// Spawns a specific mob at a location
    /// </summary>
    Mob SpawnMob(GameWorld world, string mobType, Vector2 position, string roomId);

    /// <summary>
    /// Updates all mobs in a world (called each frame)
    /// </summary>
    void UpdateMobs(GameWorld world, float deltaTime);

    /// <summary>
    /// Removes dead mobs and handles cleanup
    /// </summary>
    void ProcessDeadMobs(GameWorld world);

    // =============================================
    // AI BEHAVIOR SYSTEM
    // =============================================

    /// <summary>
    /// Updates AI behavior for a specific mob
    /// </summary>
    AIBehaviorResult UpdateMobBehavior(Mob mob, GameWorld world, float deltaTime);

    /// <summary>
    /// Finds the best target for a mob
    /// </summary>
    RealTimePlayer? FindBestTarget(Mob mob, GameWorld world);

    /// <summary>
    /// Calculates optimal path to target
    /// </summary>
    List<Vector2> CalculatePath(Mob mob, Vector2 target, GameWorld world);

    /// <summary>
    /// Handles mob combat behavior
    /// </summary>
    MobCombatResult ProcessMobCombat(Mob mob, RealTimePlayer target, GameWorld world);

    // =============================================
    // MOB STATES AND TRANSITIONS
    // =============================================

    /// <summary>
    /// Changes mob state with validation
    /// </summary>
    bool ChangeState(Mob mob, MobState newState, string reason = "");

    /// <summary>
    /// Checks if state transition is valid
    /// </summary>
    bool CanTransitionToState(Mob mob, MobState currentState, MobState newState);

    /// <summary>
    /// Gets available actions for current state
    /// </summary>
    List<MobAction> GetAvailableActions(Mob mob, GameWorld world);

    // =============================================
    // ADVANCED AI FEATURES
    // =============================================

    /// <summary>
    /// Handles group AI coordination
    /// </summary>
    void ProcessGroupBehavior(List<Mob> mobGroup, GameWorld world);

    /// <summary>
    /// Manages mob spawning based on world events
    /// </summary>
    void ProcessDynamicSpawning(GameWorld world, float deltaTime);

    /// <summary>
    /// Handles boss AI and special behaviors
    /// </summary>
    void ProcessBossAI(Mob bossMob, GameWorld world, float deltaTime);

    /// <summary>
    /// Processes mob abilities and special attacks
    /// </summary>
    MobAbilityResult ProcessMobAbility(Mob mob, string abilityType, GameWorld world);

    // =============================================
    // DIFFICULTY AND SCALING
    // =============================================

    /// <summary>
    /// Scales mob difficulty based on world progression
    /// </summary>
    void ScaleMobDifficulty(GameWorld world);

    /// <summary>
    /// Calculates dynamic mob stats
    /// </summary>
    MobStats CalculateMobStats(string mobType, GameWorld world, Room room);

    /// <summary>
    /// Adjusts AI aggression based on conditions
    /// </summary>
    float CalculateAggressionLevel(Mob mob, GameWorld world);

    // =============================================
    // PERFORMANCE OPTIMIZATION
    // =============================================

    /// <summary>
    /// Optimizes AI processing based on distance to players
    /// </summary>
    void OptimizeAIProcessing(GameWorld world);

    /// <summary>
    /// Updates spatial partitioning for mob AI
    /// </summary>
    void UpdateSpatialPartitioning(GameWorld world);

    /// <summary>
    /// Processes AI in different priority levels
    /// </summary>
    void ProcessAIByPriority(GameWorld world, float deltaTime);

    // =============================================
    // CLEANUP AND MANAGEMENT
    // =============================================

    /// <summary>
    /// Cleans up AI data for a world
    /// </summary>
    void CleanupWorldAI(string worldId);

    /// <summary>
    /// Removes expired or invalid mobs
    /// </summary>
    void CleanupInvalidMobs(GameWorld world);

    /// <summary>
    /// Optimizes memory usage for AI system
    /// </summary>
    void OptimizeMemoryUsage();

    // =============================================
    // STATISTICS AND ANALYTICS
    // =============================================

    /// <summary>
    /// Gets AI statistics for a world
    /// </summary>
    AIStats GetAIStats(string worldId);

    /// <summary>
    /// Gets detailed AI analytics across all worlds
    /// </summary>
    Dictionary<string, object> GetDetailedAIAnalytics();

    /// <summary>
    /// Gets mob distribution and behavior stats
    /// </summary>
    Dictionary<string, object> GetMobDistributionStats(string worldId);

    // =============================================
    // CONFIGURATION
    // =============================================

    /// <summary>
    /// Updates AI settings at runtime
    /// </summary>
    void UpdateAISettings(AISettings newSettings);

    /// <summary>
    /// Gets current AI settings
    /// </summary>
    AISettings GetCurrentAISettings();

    /// <summary>
    /// Updates mob template
    /// </summary>
    void UpdateMobTemplate(string mobType, MobTemplate template);

    // =============================================
    // EVENTS
    // =============================================

    /// <summary>
    /// Event fired when a mob is spawned
    /// </summary>
    event Action<string, Mob>? OnMobSpawned; // worldId, mob

    /// <summary>
    /// Event fired when a mob dies
    /// </summary>
    event Action<string, Mob, RealTimePlayer?>? OnMobDeath; // worldId, mob, killer

    /// <summary>
    /// Event fired when a mob changes state
    /// </summary>
    event Action<Mob, MobState, MobState>? OnMobStateChanged; // mob, oldState, newState

    /// <summary>
    /// Event fired when a mob attacks a player
    /// </summary>
    event Action<Mob, RealTimePlayer, int>? OnMobAttack; // mob, target, damage

    /// <summary>
    /// Event fired when a boss is spawned
    /// </summary>
    event Action<string, Mob>? OnBossSpawned; // worldId, boss
}