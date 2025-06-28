using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MazeWars.GameServer.Models;

public class ServerMetrics
{
    public DateTime Timestamp { get; set; }
    public PerformanceMetrics Performance { get; set; } = new();
    public NetworkMetrics Network { get; set; } = new();
    public GameMetrics Game { get; set; } = new();
}

public class PerformanceMetrics
{
    public int FrameNumber { get; set; }
    public int WorldCount { get; set; }
    public int TotalPlayers { get; set; }
    public int InputQueueSize { get; set; }
    public double CPUUsage { get; set; }
    public double MemoryUsageMB { get; set; }
    public double AvailableMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public double UptimeHours { get; set; }
    public double NetworkBytesPerSecond { get; set; }
}

public class NetworkMetrics
{
    public int ConnectedClients { get; set; }
    public int PacketsSent { get; set; }
    public int PacketsReceived { get; set; }
    public bool IsRunning { get; set; }
}

public class GameMetrics
{
    public int AlivePlayers { get; set; }
    public int TotalMobs { get; set; }
    public int TotalLoot { get; set; }
    public int CompletedWorlds { get; set; }
    public int RecentCombatEvents { get; set; }
    public int RecentLootUpdates { get; set; }
}
