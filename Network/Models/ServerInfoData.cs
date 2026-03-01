using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Server configuration information sent on connection.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ServerInfoData
{
    [Key(0)]
    public int TickRate { get; set; }

    [Key(1)]
    public WorldBoundsData WorldBounds { get; set; } = new();

    [Key(2)]
    public int MaxPlayersPerWorld { get; set; }

    [Key(3)]
    public int MinPlayersToStart { get; set; }

    [Key(4)]
    public List<RoomInfoData> Rooms { get; set; } = new();
}

/// <summary>
/// Room geometry data sent to client for map rendering.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class RoomInfoData
{
    [Key(0)] public string RoomId { get; set; } = "";
    [Key(1)] public float PositionX { get; set; }
    [Key(2)] public float PositionY { get; set; }
    [Key(3)] public float SizeX { get; set; }
    [Key(4)] public float SizeY { get; set; }
    [Key(5)] public string RoomType { get; set; } = "";
    [Key(6)] public List<string> Connections { get; set; } = new();
}

/// <summary>
/// Batch of room data sent in small UDP-safe chunks after game_started.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class RoomDataBatch
{
    [Key(0)] public List<RoomInfoData> Rooms { get; set; } = new();
}
