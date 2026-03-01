using MazeWars.GameServer.Models;

public class EquipResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public LootItem? EquippedItem { get; set; }
    public LootItem? UnequippedItem { get; set; }
    public Dictionary<string, int> StatChanges { get; set; } = new();

    public static EquipResult Ok(string message = "") => new() { Success = true };
    public static EquipResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
