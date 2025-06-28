namespace MazeWars.GameServer.Models;

public class LootTable
{
    public string TableId { get; set; } = string.Empty;
    public List<LootDrop> PossibleDrops { get; set; } = new();
    public string TriggerCondition { get; set; } = string.Empty;
}
