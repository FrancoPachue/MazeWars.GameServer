using MessagePack;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Connection messages from client to server.
/// Index [1] is CharacterName — server looks up the character to get the class.
/// </summary>
[MessagePackObject(keyAsPropertyName: false)]
public class ClientConnectData
{
    [Key(0)]
    public string PlayerName { get; set; } = string.Empty;

    [Key(1)]
    public string CharacterName { get; set; } = string.Empty;

    [Key(2)]
    public string TeamId { get; set; } = string.Empty;

    [Key(3)]
    public string AuthToken { get; set; } = string.Empty;

    [Key(4)]
    public string GameMode { get; set; } = "trios";

    [Key(5)]
    public string DifficultyTier { get; set; } = "normal";
}
