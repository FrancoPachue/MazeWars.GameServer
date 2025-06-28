namespace MazeWars.GameServer.Network.Models;

public class NetworkStats
{
    public int ConnectedClients { get; set; }
    public int PacketsSent { get; set; }
    public int PacketsReceived { get; set; }
    public bool IsRunning { get; set; }
    public int Port { get; set; }
}