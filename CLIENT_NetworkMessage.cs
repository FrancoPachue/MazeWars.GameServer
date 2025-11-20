// ========================================
// ESTE ARCHIVO ES PARA EL CLIENTE (Unity)
// Copiar a: Assets/Scripts/Network/Models/NetworkMessage.cs
// ========================================

using MessagePack;
using System;

namespace MazeWars.Client.Network.Models
{
    /// <summary>
    /// Base network message with MessagePack serialization support.
    /// IMPORTANTE: Este modelo DEBE coincidir EXACTAMENTE con el del servidor.
    /// keyAsPropertyName: false = array format (más compacto)
    /// </summary>
    [MessagePackObject(keyAsPropertyName: false)]
    public class NetworkMessage
    {
        [Key(0)]
        public string Type { get; set; } = string.Empty;

        [Key(1)]
        public string PlayerId { get; set; } = string.Empty;

        [Key(2)]
        public object Data { get; set; } = null;

        [Key(3)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
