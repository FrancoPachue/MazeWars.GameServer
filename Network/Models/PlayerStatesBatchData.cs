using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Batch of player states to reduce network overhead.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class PlayerStatesBatchData
{
    [Key(0)]
    public List<PlayerUpdateData> Players { get; set; } = new();

    [Key(1)]
    public int BatchIndex { get; set; }

    [Key(2)]
    public int TotalBatches { get; set; }

    /// <summary>
    /// Input acknowledgments for client-side prediction reconciliation.
    /// Maps PlayerId -> Last Acknowledged Sequence Number
    /// </summary>
    [Key(3)]
    public Dictionary<string, uint> AcknowledgedInputs { get; set; } = new();
}
