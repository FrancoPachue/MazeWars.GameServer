using System.Collections.Concurrent;
using System.Net;

namespace MazeWars.GameServer.Security;

public class RateLimitingService
{
    private readonly ConcurrentDictionary<IPEndPoint, ClientRateLimit> _clientLimits = new();
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
        var limit = _clientLimits.GetOrAdd(clientEndPoint, _ => new ClientRateLimit());
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
        foreach (var kvp in _clientLimits)
        {
            if (kvp.Value.IsExpired())
            {
                _clientLimits.TryRemove(kvp.Key, out _);
            }
        }
    }
}

public class ClientRateLimit
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<DateTime>> _messageTimes = new();
    private readonly List<string> _violations = new();
    private DateTime _lastActivity = DateTime.UtcNow;

    public int ViolationCount
    {
        get { lock (_lock) { return _violations.Count; } }
    }

    public bool IsAllowed(string messageType)
    {
        lock (_lock)
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
                RecordViolationInternal($"Rate limit exceeded for {messageType}");
                return false;
            }

            times.Enqueue(DateTime.UtcNow);
            return true;
        }
    }

    public void RecordViolation(string reason)
    {
        lock (_lock)
        {
            RecordViolationInternal(reason);
        }
    }

    private void RecordViolationInternal(string reason)
    {
        _violations.Add($"{DateTime.UtcNow}: {reason}");

        if (_violations.Count > 10)
        {
            _violations.RemoveAt(0);
        }
    }

    public bool ShouldBeBanned()
    {
        lock (_lock) { return _violations.Count >= 5; }
    }

    public bool IsExpired()
    {
        lock (_lock) { return (DateTime.UtcNow - _lastActivity).TotalMinutes > 10; }
    }

    private static int GetRateLimit(string messageType)
    {
        return messageType.ToLower() switch
        {
            // 60 FPS * 60 seconds = 3600/min, allow 4000 for burst tolerance
            "player_input" => 4000,
            "chat" => 10,
            "loot_grab" => 30,
            "use_item" => 20,
            "ping" => 6,
            // Server pings every 30 frames at 60fps = 2/sec = 120/min, allow 2x margin
            "server_pong" => 240,
            "heartbeat" => 120,
            "message_ack" => 240,
            _ => 60
        };
    }
}
