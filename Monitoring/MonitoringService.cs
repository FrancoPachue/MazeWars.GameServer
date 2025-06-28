using System.Diagnostics;
using MazeWars.GameServer.Engine;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Services;

namespace MazeWars.GameServer.Monitoring;

public class MetricsService : BackgroundService
{
    private readonly ILogger<MetricsService> _logger;
    private readonly RealTimeGameEngine _gameEngine;
    private readonly UdpNetworkService _networkService;
    private readonly ISystemMetrics _systemMetrics;

    public MetricsService(
        ILogger<MetricsService> logger,
        RealTimeGameEngine gameEngine,
        UdpNetworkService networkService,
        ISystemMetrics systemMetrics)
    {
        _logger = logger;
        _gameEngine = gameEngine;
        _networkService = networkService;
        _systemMetrics = systemMetrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Metrics Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAndLogMetrics();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting metrics");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task CollectAndLogMetrics()
    {
        try
        {
            var gameStats = _gameEngine.GetServerStats();
            var networkStats = _networkService.GetNetworkStats();

            // SOLUCIÓN: Crear objeto tipado en lugar de usar dynamic
            var metricsData = new ServerMetrics
            {
                Timestamp = DateTime.UtcNow,
                Performance = new PerformanceMetrics
                {
                    FrameNumber = Convert.ToInt32(gameStats["FrameNumber"]),
                    WorldCount = Convert.ToInt32(gameStats["WorldCount"]),
                    TotalPlayers = Convert.ToInt32(gameStats["TotalPlayers"]),
                    InputQueueSize = Convert.ToInt32(gameStats["InputQueueSize"]),
                    CPUUsage = _systemMetrics.GetCpuUsage(),
                    MemoryUsageMB = _systemMetrics.GetMemoryUsageMB(),
                    AvailableMemoryMB = _systemMetrics.GetAvailableMemoryMB(),
                    ThreadCount = _systemMetrics.GetThreadCount(),
                    UptimeHours = _systemMetrics.GetUptime().TotalHours,
                    NetworkBytesPerSecond = _systemMetrics.GetNetworkBytesPerSecond()
                },
                Network = new NetworkMetrics
                {
                    ConnectedClients = networkStats.ConnectedClients,
                    PacketsSent = networkStats.PacketsSent,
                    PacketsReceived = networkStats.PacketsReceived,
                    IsRunning = networkStats.IsRunning
                },
                Game = new GameMetrics
                {
                    AlivePlayers = Convert.ToInt32(gameStats["AlivePlayers"]),
                    TotalMobs = Convert.ToInt32(gameStats["TotalMobs"]),
                    TotalLoot = Convert.ToInt32(gameStats["TotalLoot"]),
                    CompletedWorlds = Convert.ToInt32(gameStats["CompletedWorlds"]),
                    RecentCombatEvents = Convert.ToInt32(gameStats["RecentCombatEvents"]),
                    RecentLootUpdates = Convert.ToInt32(gameStats["RecentLootUpdates"])
                }
            };

            _logger.LogInformation("Server Metrics: {@Metrics}", metricsData);

            await SendToMonitoringSystem(metricsData);
            CheckMetricAlerts(metricsData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CollectAndLogMetrics");
        }
    }

    private async Task SendToMonitoringSystem(ServerMetrics metrics)
    {
        try
        {
            // Ejemplo de envío a sistemas de monitoreo
            // Prometheus, Application Insights, DataDog, etc.
            await Task.CompletedTask; // Placeholder
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending metrics to monitoring system");
        }
    }

    // SOLUCIÓN: Usar objeto tipado en lugar de dynamic
    private void CheckMetricAlerts(ServerMetrics metrics)
    {
        try
        {
            var performance = metrics.Performance;

            // Alerta de CPU alto
            if (performance.CPUUsage > 80.0)
            {
                _logger.LogWarning("High CPU usage detected: {CPUUsage}%", performance.CPUUsage);
            }

            // Alerta de memoria alta
            if (performance.MemoryUsageMB > 1024) // 1GB
            {
                _logger.LogWarning("High memory usage detected: {MemoryUsage}MB", performance.MemoryUsageMB);
            }

            // Alerta de cola de input grande
            if (performance.InputQueueSize > 5000)
            {
                _logger.LogWarning("Large input queue detected: {QueueSize}", performance.InputQueueSize);
            }

            // Alerta de muchos hilos
            if (performance.ThreadCount > 100)
            {
                _logger.LogWarning("High thread count detected: {ThreadCount}", performance.ThreadCount);
            }

            // Alerta de red no funcional
            if (!metrics.Network.IsRunning)
            {
                _logger.LogError("Network service is not running!");
            }

            // Alerta de muchos jugadores
            if (performance.TotalPlayers > performance.WorldCount * 20) // Más de 20 jugadores por mundo promedio
            {
                _logger.LogInformation("High player count: {PlayerCount} across {WorldCount} worlds",
                    performance.TotalPlayers, performance.WorldCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking metric alerts");
        }
    }
}