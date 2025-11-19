using MazeWars.GameServer.Engine.Network;
using MazeWars.GameServer.Models;
using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Server response to reconnect request with restored state.
/// </summary>
[MessagePackObject]
public class ReconnectResponseData
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public string ErrorMessage { get; set; } = string.Empty;

    [Key(2)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(3)]
    public string WorldId { get; set; } = string.Empty;

    [Key(4)]
    public bool IsInLobby { get; set; }

    // Restored player state
    [Key(5)]
    public Vector2 Position { get; set; }

    [Key(6)]
    public Vector2 Velocity { get; set; }

    [Key(7)]
    public float Direction { get; set; }

    [Key(8)]
    public string CurrentRoomId { get; set; } = string.Empty;

    [Key(9)]
    public int Health { get; set; }

    [Key(10)]
    public int MaxHealth { get; set; }

    [Key(11)]
    public int Mana { get; set; }

    [Key(12)]
    public int MaxMana { get; set; }

    [Key(13)]
    public int Shield { get; set; }

    [Key(14)]
    public int Level { get; set; }

    [Key(15)]
    public int ExperiencePoints { get; set; }

    [Key(16)]
    public bool IsAlive { get; set; }

    [Key(17)]
    public string TeamId { get; set; } = string.Empty;

    [Key(18)]
    public string PlayerClass { get; set; } = string.Empty;

    // Inventory and effects (simplified - client will receive full state in next WorldUpdate)
    [Key(19)]
    public int InventoryCount { get; set; }

    [Key(20)]
    public int ActiveEffectsCount { get; set; }

    // Server time for synchronization
    [Key(21)]
    public float ServerTime { get; set; }

    // Time since disconnect (for client to adjust animations, etc.)
    [Key(22)]
    public float TimeSinceDisconnect { get; set; }
}
