using MessagePack;

namespace MazeWars.GameServer.Network.Models;

[MessagePackObject(keyAsPropertyName: false)]
public class EquipFromStashMessage
{
    [Key(0)]
    public string LootId { get; set; } = string.Empty;
}
