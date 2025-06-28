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

}
