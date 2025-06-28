using MazeWars.GameServer.Network.Services;

public class GameServerHostedService : BackgroundService
{
    private readonly UdpNetworkService _networkService;
    private readonly ILogger<GameServerHostedService> _logger;

    public GameServerHostedService(
        UdpNetworkService networkService,
        ILogger<GameServerHostedService> logger)
    {
        _networkService = networkService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Game Server Hosted Service");

        try
        {
            await _networkService.StartAsync();

            // Keep the service running until cancellation is requested
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);

                // Optional: Periodic health checks or maintenance tasks
                var stats = _networkService.GetNetworkStats();
                if (stats.ConnectedClients > 0)
                {
                    _logger.LogDebug("Network service running with {ClientCount} connected clients",
                        stats.ConnectedClients);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            _logger.LogInformation("Game Server Hosted Service cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Game Server Hosted Service");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Game Server Hosted Service");

        try
        {
            await _networkService.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping network service");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Game Server Hosted Service stopped");
    }
}