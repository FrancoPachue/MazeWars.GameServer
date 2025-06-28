using MazeWars.GameServer.Models;
using System.Net;

public class ConnectedClient
{
    public IPEndPoint EndPoint { get; set; } = null!;
    public RealTimePlayer Player { get; set; } = null!;
    public string WorldId { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public class ClientConnectData
{
    public string PlayerName { get; set; } = string.Empty;
    public string PlayerClass { get; set; } = "scout";
    public string TeamId { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

public class ClientConnectedData
{
    public string PlayerId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public Vector2 SpawnPosition { get; set; }
    public ServerInfo ServerInfo { get; set; } = new();
}

public class ServerInfo
{
    public int TickRate { get; set; }
    public Vector2 WorldBounds { get; set; }
    public Dictionary<string, object> GameConfig { get; set; } = new();
}

public class ChatReceivedData
{
    public string PlayerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ChatType { get; set; } = "all";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class NetworkStats
{
    public int ConnectedClients { get; set; }
    public int PacketsSent { get; set; }
    public int PacketsReceived { get; set; }
    public bool IsRunning { get; set; }
    public int Port { get; set; }
}