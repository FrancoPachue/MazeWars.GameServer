using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Configuration;

public class WorldGeneration
{
    [Range(2, 10)]
    public int WorldSizeX { get; set; } = 4;

    [Range(2, 10)]
    public int WorldSizeY { get; set; } = 4;

    [Range(10, 100)]
    public float RoomSizeX { get; set; } = 50f;

    [Range(10, 100)]
    public float RoomSizeY { get; set; } = 50f;

    [Range(30, 200)]
    public float RoomSpacing { get; set; } = 60f;

    [Range(0, 20)]
    public int MobsPerRoom { get; set; } = 3;

    [Range(10, 200)]
    public int InitialLootCount { get; set; } = 12;

    [Range(30, 600)]
    public int LootRespawnIntervalSeconds { get; set; } = 120;

    // Propiedades derivadas
    public int ExtractionPointCount { get; set; } = 4;
}
