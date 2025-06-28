namespace MazeWars.GameServer.Engine.Movement.Settings;

public class MovementSettings
{
    public float BaseMovementSpeed { get; set; } = 5.0f;
    public float SprintMultiplier { get; set; } = 1.5f;
    public float MaxInputMagnitude { get; set; } = 1.1f; // Allow slight tolerance for floating point
    public float PlayerCollisionRadius { get; set; } = 0.8f;
    public float MobCollisionRadius { get; set; } = 1.0f;
    public float WallPushbackForce { get; set; } = 2.0f;
    public float MinMovementThreshold { get; set; } = 0.1f;
    public int ManaCostPerSprintSecond { get; set; } = 1;
    public float TeleportMaxDistance { get; set; } = 15.0f;
    public bool EnableCollisionPrediction { get; set; } = true;
    public float CollisionPredictionTime { get; set; } = 0.1f; // seconds ahead
}