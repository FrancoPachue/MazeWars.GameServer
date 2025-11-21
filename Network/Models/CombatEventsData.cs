using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Combat events that occurred during the frame.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class CombatEventsData
{
    [Key(0)]
    public List<CombatEvent> CombatEvents { get; set; } = new();
}
