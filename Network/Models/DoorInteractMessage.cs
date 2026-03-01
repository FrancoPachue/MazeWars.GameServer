using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Door interaction request from client to server.
/// Client sends this when player channels to open a door.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class DoorInteractMessage
{
    [Key(0)]
    public string DoorId { get; set; } = string.Empty;
}
