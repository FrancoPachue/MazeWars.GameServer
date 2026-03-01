using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Configuration;

public class NetworkSettings
{
    [Range(5, 120)]
    public int WorldUpdateRate { get; set; } = 20;

    [Range(10, 120)]
    public int PlayerUpdateRate { get; set; } = 60;

    [Range(1, 10)]
    public int ReliableMessageRetries { get; set; } = 3;

    [Range(10, 300)]
    public int ClientTimeoutSeconds { get; set; } = 30;

    [Range(512, 65535)]
    public int MaxPacketSize { get; set; } = 1400;

    public int SocketTimeoutMs { get; set; } = 5000;

    public int CompressionThreshold { get; set; } = 1200;

    // RTT measurement
    public int PingIntervalMs { get; set; } = 1000;
    public float RttSmoothingFactor { get; set; } = 0.125f;

    // Congestion control
    public float PacketLossHighThreshold { get; set; } = 0.10f;
    public float PacketLossLowThreshold { get; set; } = 0.02f;
    [Range(1, 10)]
    public int MaxSendFrameSkip { get; set; } = 4;
    public int CongestionAdjustmentIntervalMs { get; set; } = 5000;

    // Lag compensation
    public int MaxLagCompensationMs { get; set; } = 200;

    // Auto-kick
    public int MaxCheatViolations { get; set; } = 10;
    public int ViolationWindowSeconds { get; set; } = 60;

    // Snapshot
    public int FullSnapshotIntervalSeconds { get; set; } = 30;

}
