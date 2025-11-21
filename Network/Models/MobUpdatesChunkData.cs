using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Chunked mob updates to avoid exceeding packet size limits.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class MobUpdatesChunkData
{
    [Key(0)]
    public List<MobUpdate> MobUpdates { get; set; } = new();

    [Key(1)]
    public int ChunkIndex { get; set; }

    [Key(2)]
    public int TotalChunks { get; set; }
}
