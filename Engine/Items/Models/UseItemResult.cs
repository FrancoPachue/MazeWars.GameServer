using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Items.Models;

public class UseItemResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Message { get; private set; }
    public LootItem? UsedItem { get; private set; }
    public bool ItemConsumed { get; private set; }

    private UseItemResult() { }

    public static UseItemResult Ok(string message, LootItem usedItem, bool itemConsumed = true)
    {
        return new UseItemResult
        {
            Success = true,
            Message = message,
            UsedItem = usedItem,
            ItemConsumed = itemConsumed
        };
    }

    public static UseItemResult Error(string errorMessage)
    {
        return new UseItemResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
