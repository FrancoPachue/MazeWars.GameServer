using MazeWars.GameServer.Models;

public class KeyUseResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UnlockedDoor { get; set; }
    public string? OpenedChest { get; set; }
    public List<LootItem> RevealedLoot { get; set; } = new();
}
