using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class EquipItemMessage
{
    [Key(0)]
    public string ItemId { get; set; } = string.Empty;

    [Key(1)]
    public bool Unequip { get; set; }

    [Key(2)]
    public int SlotIndex { get; set; }
}
