using MazeWars.GameServer.Engine;
using MazeWars.GameServer.Network.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public class GameServerHealthCheck : IHealthCheck
{
    private readonly RealTimeGameEngine _gameEngine;

    public GameServerHealthCheck(RealTimeGameEngine gameEngine)
    {
        _gameEngine = gameEngine;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _gameEngine.GetServerStats();
            var inputQueueSize = (int)stats["InputQueueSize"];

            if (inputQueueSize > 10000)
            {
                return Task.FromResult(HealthCheckResult.Degraded($"High input queue size: {inputQueueSize}"));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Game engine is running normally", stats));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Game engine health check failed", ex));
        }
    }
}

public class NetworkServiceHealthCheck : IHealthCheck
{
    private readonly UdpNetworkService _networkService;

    public NetworkServiceHealthCheck(UdpNetworkService networkService)
    {
        _networkService = networkService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _networkService.GetNetworkStats();

            if (!stats.IsRunning)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Network service is not running"));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Network service is running normally", new Dictionary<string, object>
            {
                ["ConnectedClients"] = stats.ConnectedClients,
                ["PacketsSent"] = stats.PacketsSent,
                ["PacketsReceived"] = stats.PacketsReceived,
                ["Port"] = stats.Port
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Network service health check failed", ex));
        }
    }
}