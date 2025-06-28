// Configuration/LobbySettings.cs - Adaptado a tu estructura actual

using System.ComponentModel.DataAnnotations;

namespace MazeWars.GameServer.Configuration;

public class LobbySettings
{
    /// <summary>
    /// Tiempo máximo de espera en segundos antes de iniciar una partida con jugadores mínimos
    /// </summary>
    [Range(10, 300)]
    public int MaxWaitTimeSeconds { get; set; } = 30;

    /// <summary>
    /// Tiempo máximo absoluto de espera antes de iniciar sin importar cuántos jugadores haya
    /// </summary>
    [Range(30, 600)]
    public int AbsoluteMaxWaitTimeSeconds { get; set; } = 60;

    /// <summary>
    /// Mínimo de jugadores requeridos para iniciar una partida
    /// </summary>
    [Range(1, 16)]
    public int MinPlayersToStart { get; set; } = 4; // Ajustado para tu configuración

    /// <summary>
    /// Mínimo de equipos requeridos para iniciar una partida
    /// </summary>
    [Range(1, 8)]
    public int MinTeamsToStart { get; set; } = 2;

    /// <summary>
    /// Si se debe balancear equipos automáticamente
    /// </summary>
    public bool AutoBalanceTeams { get; set; } = true;

    /// <summary>
    /// Tiempo en minutos para limpiar lobbies vacíos
    /// </summary>
    [Range(1, 60)]
    public int EmptyLobbyCleanupMinutes { get; set; } = 5;

    /// <summary>
    /// Tiempo en minutos para limpiar lobbies con error
    /// </summary>
    [Range(1, 10)]
    public int ErrorLobbyCleanupMinutes { get; set; } = 1;

    /// <summary>
    /// Mostrar información de lobby en logs
    /// </summary>
    public bool EnableLobbyLogging { get; set; } = true;

    /// <summary>
    /// Permitir que los lobbies inicien con menos del mínimo de equipos si hay suficientes jugadores
    /// </summary>
    public bool AllowSingleTeamStart { get; set; } = false;

    /// <summary>
    /// Tiempo en segundos entre verificaciones de inicio de lobby
    /// </summary>
    [Range(1, 30)]
    public int LobbyCheckIntervalSeconds { get; set; } = 5;
}