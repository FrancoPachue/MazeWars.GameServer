using System.Collections.Concurrent;
using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Engine.Movement.Models;

public class SpatialGrid
{
    public int CellSize { get; set; } = 32;
    public ConcurrentDictionary<(int, int), List<string>> PlayerCells { get; set; } = new();
    public ConcurrentDictionary<(int, int), List<string>> MobCells { get; set; } = new();

    public (int, int) GetCellCoords(Vector2 position)
    {
        return ((int)(position.X / CellSize), (int)(position.Y / CellSize));
    }
}
