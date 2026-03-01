using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Compact revival altar data sent to clients as part of world state.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class RevivalAltarData
{
    [Key(0)]
    public string AltarId { get; set; } = string.Empty;

    [Key(1)]
    public float PositionX { get; set; }

    [Key(2)]
    public float PositionY { get; set; }

    [Key(3)]
    public string RoomId { get; set; } = string.Empty;

    [Key(4)]
    public bool IsActive { get; set; } = true;
}
