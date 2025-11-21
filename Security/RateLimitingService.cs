using System.Net;

namespace MazeWars.GameServer.Security;

public class RateLimitingService
{
    private readonly Dictionary<IPEndPoint, ClientRateLimit> _clientLimits = new();
    private readonly Timer _cleanupTimer;
    private readonly ILogger<RateLimitingService> _logger;

    public RateLimitingService(ILogger<RateLimitingService> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpiredLimits, null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsAllowed(IPEndPoint clientEndPoint, string messageType)
    {
        if (!_clientLimits.TryGetValue(clientEndPoint, out var limit))
        {
            limit = new ClientRateLimit();
            _clientLimits[clientEndPoint] = limit;
        }

        return limit.IsAllowed(messageType);
    }

    public void RecordViolation(IPEndPoint clientEndPoint, string reason)
    {
        if (_clientLimits.TryGetValue(clientEndPoint, out var limit))
        {
            limit.RecordViolation(reason);

            if (limit.ShouldBeBanned())
            {
                _logger.LogWarning("Client {EndPoint} should be banned: {Violations} violations",
                    clientEndPoint, limit.ViolationCount);
            }
        }
    }

    private void CleanupExpiredLimits(object? state)
    {
        var expiredClients = _clientLimits
            .Where(kvp => kvp.Value.IsExpired())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var client in expiredClients)
        {
            _clientLimits.Remove(client);
        }
    }
}

public class ClientRateLimit
{
    private readonly Dictionary<string, Queue<DateTime>> _messageTimes = new();
    private readonly List<string> _violations = new();
    private DateTime _lastActivity = DateTime.UtcNow;

    public int ViolationCount => _violations.Count;

    public bool IsAllowed(string messageType)
    {
        _lastActivity = DateTime.UtcNow;

        if (!_messageTimes.TryGetValue(messageType, out var times))
        {
            times = new Queue<DateTime>();
            _messageTimes[messageType] = times;
        }

        while (times.Count > 0 && (DateTime.UtcNow - times.Peek()).TotalMinutes > 1)
        {
            times.Dequeue();
        }

        var limit = GetRateLimit(messageType);
        if (times.Count >= limit)
        {
            RecordViolation($"Rate limit exceeded for {messageType}");
            return false;
        }

        times.Enqueue(DateTime.UtcNow);
        return true;
    }

    public void RecordViolation(string reason)
    {
        _violations.Add($"{DateTime.UtcNow}: {reason}");

        if (_violations.Count > 10)
        {
            _violations.RemoveAt(0);
        }
    }

    public bool ShouldBeBanned() => _violations.Count >= 5;

    public bool IsExpired() => (DateTime.UtcNow - _lastActivity).TotalMinutes > 10;

    private int GetRateLimit(string messageType)
    {
        return messageType.ToLower() switch
        {
            // 60 FPS * 60 seconds = 3600/min, allow 4000 for burst tolerance
            "player_input" => 4000,
            "chat" => 10,
            "loot_grab" => 30,
            "use_item" => 20,
            "ping" => 6,
            _ => 60
        };
    }
}
