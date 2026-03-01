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

    // ⭐ RECONNECTION: Session token for reconnection handling
    public string SessionToken { get; set; } = string.Empty;

    // RTT tracking
    public float RttMs { get; set; } = 0f;
    public float RttVariance { get; set; } = 0f;
    public uint LastPingId { get; set; } = 0;
    public DateTime LastPingSentAt { get; set; }
    public float PacketLossRate { get; set; } = 0f;

    // Congestion control
    public int SendFrameSkip { get; set; } = 1;
    public DateTime LastCongestionAdjustment { get; set; } = DateTime.UtcNow;

    // Anti-cheat auto-kick
    public int CheatViolations { get; set; } = 0;
    public DateTime FirstViolationAt { get; set; }
    public DateTime LastViolationAt { get; set; }
}
