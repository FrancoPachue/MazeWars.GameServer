// Network/UdpConnectedClient.cs - Optimized Version with Complete Functionality
using MazeWars.GameServer.Models;
using System.Net;

namespace MazeWars.GameServer.Network.Services;

// =============================================
// SUPPORTING CLASSES
// =============================================

public class ConnectedClient
{
    public IPEndPoint EndPoint { get; set; } = null!;
    public RealTimePlayer Player { get; set; } = null!;
    public string WorldId { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public int MessagesSent { get; set; } = 0;
    public int MessagesReceived { get; set; } = 0;
}
