namespace MazeWars.GameServer.Models;

/// <summary>
/// A Revival Altar placed in certain rooms during world generation.
/// Players carrying a dead teammate's soul can channel here to revive them.
/// </summary>
public class RevivalAltar
{
    public string AltarId { get; set; } = string.Empty;
    public Vector2 Position { get; set; } = new();
    public string RoomId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
