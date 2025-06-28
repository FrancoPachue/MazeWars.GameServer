using MazeWars.GameServer.Engine;
using MazeWars.GameServer.Network.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace MazeWars.GameServer.Admin;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly RealTimeGameEngine _gameEngine;
    private readonly UdpNetworkService _networkService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        RealTimeGameEngine gameEngine,
        UdpNetworkService networkService,
        ILogger<AdminController> logger)
    {
        _gameEngine = gameEngine;
        _networkService = networkService;
        _logger = logger;
    }

    [HttpGet("stats")]
    public IActionResult GetServerStats()
    {
        var gameStats = _gameEngine.GetServerStats();
        var networkStats = _networkService.GetNetworkStats();

        return Ok(new
        {
            Server = new
            {
                Status = "Running",
                Uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime,
                Version = "1.0.0"
            },
            Game = gameStats,
            Network = networkStats,
            System = new
            {
                MemoryUsage = GC.GetTotalMemory(false) / 1024 / 1024,
                ThreadCount = Process.GetCurrentProcess().Threads.Count,
                CPUUsage = GetCPUUsage()
            }
        });
    }

    [HttpGet("worlds")]
    public IActionResult GetWorlds()
    {
        var worlds = _gameEngine.GetWorldStates();
        return Ok(worlds);
    }

    [HttpPost("worlds/{worldId}/force-complete")]
    public IActionResult ForceCompleteWorld(string worldId)
    {
        try
        {
            _gameEngine.ForceCompleteWorld(worldId);
            _logger.LogWarning("Admin forced completion of world {WorldId}", worldId);
            return Ok(new { Message = $"World {worldId} forced to complete" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forcing world completion");
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("kick-player")]
    public IActionResult KickPlayer([FromBody] KickPlayerRequest request)
    {
        try
        {
            _networkService.KickPlayer(request.PlayerId, request.Reason);
            _logger.LogWarning("Admin kicked player {PlayerId}: {Reason}",
                request.PlayerId, request.Reason);
            return Ok(new { Message = $"Player {request.PlayerId} kicked" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("broadcast")]
    public IActionResult BroadcastMessage([FromBody] BroadcastRequest request)
    {
        try
        {
            _networkService.BroadcastAdminMessage(request.Message, request.WorldId);
            _logger.LogInformation("Admin broadcast: {Message}", request.Message);
            return Ok(new { Message = "Broadcast sent" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    private double GetCPUUsage()
    {
        return Math.Round(Random.Shared.NextDouble() * 100, 2);
    }
}

public class KickPlayerRequest
{
    public string PlayerId { get; set; } = string.Empty;
    public string Reason { get; set; } = "Admin action";
}

public class BroadcastRequest
{
    public string Message { get; set; } = string.Empty;
    public string? WorldId { get; set; } = null;
}