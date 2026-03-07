using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Configuration;

public class GameServerSettings
{
    [Required]
    public int UdpPort { get; set; } = 7001;

    [Required]
    public int MaxPlayersPerWorld { get; set; } = 24;

    [Required]
    [Range(10, 120)]
    public int TargetFPS { get; set; } = 60;

    [Required]
    public int MaxWorldInstances { get; set; } = 8;

    [Required]
    public string ServerId { get; set; } = "MazeWars-Server-001";

    public int MinPlayersPerWorld => LobbySettings?.MinPlayersToStart ?? 2;

    [Required]
    public GameBalance GameBalance { get; set; } = new();

    [Required]
    public WorldGeneration WorldGeneration { get; set; } = new();

    [Required]
    public NetworkSettings NetworkSettings { get; set; } = new();

    [Required]
    public LobbySettings LobbySettings { get; set; } = new();

    public Dictionary<string, GameModeConfig> GameModes { get; set; } = new()
    {
        ["solos"] = new GameModeConfig
        {
            MaxTeamSize = 1,
            GridSizeX = 7,
            GridSizeY = 7,
            MobsPerRoom = 2,
            MobHealthMultiplier = 0.7f,
            MobDamageMultiplier = 0.7f,
            MaxPlayersPerLobby = 8,
            InitialLootCount = 40
        },
        ["trios"] = new GameModeConfig
        {
            MaxTeamSize = 3,
            GridSizeX = 9,
            GridSizeY = 9,
            MobsPerRoom = 3,
            MobHealthMultiplier = 1.0f,
            MobDamageMultiplier = 1.0f,
            MaxPlayersPerLobby = 18,
            InitialLootCount = 60
        },
        ["arena"] = new GameModeConfig
        {
            MaxTeamSize = 1,
            GridSizeX = 1,
            GridSizeY = 1,
            MobsPerRoom = 0,
            MobHealthMultiplier = 0f,
            MobDamageMultiplier = 0f,
            MaxPlayersPerLobby = 4,
            InitialLootCount = 0
        }
    };
}

public class GameModeConfig
{
    public int MaxTeamSize { get; set; } = 3;
    public int GridSizeX { get; set; } = 9;
    public int GridSizeY { get; set; } = 9;
    public int MobsPerRoom { get; set; } = 3;
    public float MobHealthMultiplier { get; set; } = 1.0f;
    public float MobDamageMultiplier { get; set; } = 1.0f;
    public int MaxPlayersPerLobby { get; set; } = 12;
    public int InitialLootCount { get; set; } = 25;
}
