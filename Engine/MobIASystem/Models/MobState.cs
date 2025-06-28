namespace MazeWars.GameServer.Engine.MobIASystem.Models;

// =============================================
// MOB MODELS AND ENUMS
// =============================================

public enum MobState
{
    Spawning,
    Idle,
    Patrol,
    Alert,
    Pursuing,
    Attacking,
    Stunned,
    Fleeing,
    Dead,
    Casting,
    Enraged,
    Guarding
}
