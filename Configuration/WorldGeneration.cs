using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Configuration;

public class WorldGeneration
{
    [Range(2, 10)]
    public int WorldSizeX { get; set; } = 5;

    [Range(2, 10)]
    public int WorldSizeY { get; set; } = 5;

    [Range(10, 100)]
    public float RoomSizeX { get; set; } = 50f;

    [Range(10, 100)]
    public float RoomSizeY { get; set; } = 50f;

    [Range(30, 200)]
    public float RoomSpacing { get; set; } = 55f;

    // Variable room sizes by category
    public float RoomSizeSmallMin { get; set; } = 26f;
    public float RoomSizeSmallMax { get; set; } = 32f;
    public float RoomSizeMediumMin { get; set; } = 34f;
    public float RoomSizeMediumMax { get; set; } = 40f;
    public float RoomSizeLargeMin { get; set; } = 42f;
    public float RoomSizeLargeMax { get; set; } = 48f;

    // Position jitter for non-edge rooms (breaks grid regularity)
    public float RoomPositionJitter { get; set; } = 2f;

    [Range(0, 20)]
    public int MobsPerRoom { get; set; } = 3;

    [Range(10, 200)]
    public int InitialLootCount { get; set; } = 20;

    [Range(30, 600)]
    public int LootRespawnIntervalSeconds { get; set; } = 120;

    // Propiedades derivadas
    public int ExtractionPointCount { get; set; } = 4;
}
