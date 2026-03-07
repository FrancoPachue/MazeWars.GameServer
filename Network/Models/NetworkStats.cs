namespace MazeWars.GameServer.Network.Models;

public class NetworkStats
{
    public int ConnectedClients { get; set; }
    public long PacketsSent { get; set; }
    public long PacketsReceived { get; set; }
    public bool IsRunning { get; set; }
    public int Port { get; set; }
}