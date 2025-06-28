// Network/UdpConnectedClientInfo.cs - Optimized Version with Complete Functionality
namespace MazeWars.GameServer.Network.Services;

public class ConnectedClientInfo
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string PlayerClass { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string EndPoint { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; }
    public bool IsAlive { get; set; }
    public int Level { get; set; }
    public string CurrentRoomId { get; set; } = string.Empty;
}