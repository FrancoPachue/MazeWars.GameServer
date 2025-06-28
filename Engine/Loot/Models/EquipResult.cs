using MazeWars.GameServer.Models;

public class EquipResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public LootItem? EquippedItem { get; set; }
    public LootItem? UnequippedItem { get; set; }
    public Dictionary<string, int> StatChanges { get; set; } = new();
}
