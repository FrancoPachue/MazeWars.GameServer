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
}
