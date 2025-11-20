using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Essential world information without heavy details.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class WorldInfoEssentialData
{
    [Key(0)]
    public string WorldId { get; set; } = string.Empty;

    [Key(1)]
    public bool IsCompleted { get; set; }

    [Key(2)]
    public string WinningTeam { get; set; } = string.Empty;

    [Key(3)]
    public int CompletedRooms { get; set; }

    [Key(4)]
    public int TotalRooms { get; set; }

    [Key(5)]
    public int TotalLoot { get; set; }
}
