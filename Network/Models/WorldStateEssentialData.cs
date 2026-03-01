using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Essential world state updates sent periodically.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class WorldStateEssentialData
{
    [Key(0)]
    public WorldInfoEssentialData WorldInfo { get; set; } = new();

    [Key(1)]
    public List<RevivalAltarData> RevivalAltars { get; set; } = new();

    [Key(2)]
    public List<DoorStateData> DoorStates { get; set; } = new();

    [Key(3)]
    public List<ExtractionPointData> ExtractionPoints { get; set; } = new();

    [Key(4)]
    public List<ScoreboardEntryData> Scoreboard { get; set; } = new();
}

[MessagePackObject(keyAsPropertyName: false)]
public class ScoreboardEntryData
{
    [Key(0)] public string PlayerName { get; set; } = string.Empty;
    [Key(1)] public string TeamId { get; set; } = string.Empty;
    [Key(2)] public int Kills { get; set; }
    [Key(3)] public int Deaths { get; set; }
    [Key(4)] public int DamageDealt { get; set; }
    [Key(5)] public int HealingDone { get; set; }
    [Key(6)] public int Level { get; set; }
    [Key(7)] public bool IsAlive { get; set; }
    [Key(8)] public string PlayerClass { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: false)]
public class ExtractionPointData
{
    [Key(0)] public string ExtractionId { get; set; } = string.Empty;
    [Key(1)] public float PositionX { get; set; }
    [Key(2)] public float PositionY { get; set; }
    [Key(3)] public bool IsActive { get; set; } = true;
    [Key(4)] public string RoomId { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: false)]
public class DoorStateData
{
    [Key(0)] public string DoorId { get; set; } = string.Empty;
    [Key(1)] public bool IsOpen { get; set; }
    [Key(2)] public bool IsLocked { get; set; }
}
