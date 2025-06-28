using MazeWars.GameServer.Models;

public class LootGrabResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public LootItem? GrabbedItem { get; set; }
    public bool InventoryFull { get; set; }
    public bool OutOfRange { get; set; }
    public bool WrongRoom { get; set; }
}
