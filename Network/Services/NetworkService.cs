using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Engine;
using MazeWars.GameServer.Engine.Network;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MazeWars.GameServer.Security;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MessagePack;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MazeWars.GameServer.Network.Services;

public class UdpNetworkService : IDisposable
{
    private readonly ILogger<UdpNetworkService> _logger;
    private readonly GameServerSettings _settings;
    private readonly RealTimeGameEngine _gameEngine;
    private readonly RateLimitingService _rateLimitingService;
    private readonly SessionManager _sessionManager;

    // Network components
    private UdpClient? _udpServer;
    private readonly ConcurrentDictionary<IPEndPoint, ConnectedClient> _connectedClients = new();
    private readonly ConcurrentDictionary<string, ReliableMessage> _pendingAcks = new();

    // Timers for background tasks
    private readonly Timer _networkSendTimer;
    private readonly Timer _clientTimeoutTimer;
    private readonly Timer _reliabilityTimer;
    private readonly Timer _sessionCleanupTimer;

    // State tracking
    private bool _isRunning = false;
    private int _packetsSent = 0;
    private int _packetsReceived = 0;

    // Performance settings - OPTIMIZED VALUES
    private readonly int _worldUpdateRate = 10; // Reduced from 20 to 10 FPS for world updates
    private readonly int _maxUdpPacketSize = 1400; // Safe UDP packet size (under MTU)
    private int _worldUpdateCounter = 0;
    private const int SocketReceiveTimeout = 5000; // 5 segundos

    public UdpNetworkService(
        ILogger<UdpNetworkService> logger,
        IOptions<GameServerSettings> settings,
        RealTimeGameEngine gameEngine,
        RateLimitingService rateLimitingService,
        SessionManager sessionManager)
    {
        _logger = logger;
        _settings = settings.Value;
        _gameEngine = gameEngine;
        _rateLimitingService = rateLimitingService;
        _sessionManager = sessionManager;

        // Timer para enviar updates a 30 FPS (reduced from 60)
        var sendIntervalMs = 1000.0 / 30;
        _networkSendTimer = new Timer(SendUpdatesToClients, null,
            TimeSpan.FromMilliseconds(sendIntervalMs),
            TimeSpan.FromMilliseconds(sendIntervalMs));

        // Timer para cleanup de clientes desconectados (cada 5 segundos)
        _clientTimeoutTimer = new Timer(CleanupDisconnectedClients, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        // Timer para retry de mensajes confiables (cada segundo)
        _reliabilityTimer = new Timer(ProcessReliableMessages, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // ⭐ RECONNECTION: Timer para cleanup de sesiones expiradas (cada 30 segundos)
        _sessionCleanupTimer = new Timer(CleanupExpiredSessions, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _gameEngine.OnGameStarted += HandleGameStarted;
    }

    // =============================================
    // SERVICE LIFECYCLE METHODS
    // =============================================

    public async Task StartAsync()
    {
        try
        {
            _udpServer = new UdpClient(_settings.UdpPort)
            {
                Client =
            {
                ReceiveTimeout = 5000, // 5 segundos timeout
                SendTimeout = 5000,
                // ⭐ CONFIGURACIONES ADICIONALES PARA ROBUSTEZ
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            }
            };

            _isRunning = true;

            _logger.LogInformation("🚀 UDP Network Service started on port {Port} (with robust error handling)",
                _settings.UdpPort);

            // Start listening for incoming messages
            _ = Task.Run(ListenForMessages);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _logger.LogError("❌ Port {Port} is already in use. Another server instance running?",
                _settings.UdpPort);
            throw;
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "❌ Socket error starting UDP server on port {Port}: {ErrorCode}",
                _settings.UdpPort, ex.SocketErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to start UDP Network Service on port {Port}", _settings.UdpPort);
            throw;
        }
    }

    private UdpClient InitializeUdpServer(int port)
    {
            try
            {
                var udpServer = new UdpClient(port)
                {
                    Client =
                        {
                            ReceiveTimeout = SocketReceiveTimeout,
                            SendTimeout = SocketReceiveTimeout
                        }
                };
                _isRunning = true;
                _logger.LogInformation($"UDP server started on port {port}");
                return udpServer;
            }
            catch (SocketException ex)
            {
                _logger.LogError($"Could not start UDP server: {ex.Message}");
                throw;
            }
     }


    public async Task StopAsync()
    {
        _isRunning = false;
        _udpServer?.Close();
        _logger.LogInformation("UDP Network Service stopped");
    }

    // =============================================
    // MESSAGE DATA DESERIALIZATION HELPER
    // =============================================

    /// <summary>
    /// Deserializes message.Data (pre-serialized MessagePack bytes) to specific type T.
    /// Following MessagePack spec where Data contains pre-serialized bytes.
    /// </summary>
    private T? ConvertMessageData<T>(byte[] data) where T : class
    {
        try
        {
            if (data == null || data.Length == 0)
            {
                _logger.LogWarning("Empty data for type {Type}", typeof(T).Name);
                return null;
            }

            // Use StandardResolverAllowPrivate which handles array format [Key] attributes
            var options = MessagePackSerializerOptions.Standard
                .WithResolver(MessagePack.Resolvers.StandardResolverAllowPrivate.Instance);
            return MessagePackSerializer.Deserialize<T>(data, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize message data to type {Type}", typeof(T).Name);
            return null;
        }
    }

    /// <summary>
    /// Creates a NetworkMessage with pre-serialized data following MessagePack spec.
    /// </summary>
    private NetworkMessage CreateNetworkMessage<T>(string type, string playerId, T data) where T : class
    {
        var options = MessagePackSerializerOptions.Standard
            .WithResolver(MessagePack.Resolvers.StandardResolverAllowPrivate.Instance);
        return new NetworkMessage
        {
            Type = type,
            PlayerId = playerId,
            Data = MessagePackSerializer.Serialize(data, options),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a NetworkMessage with anonymous object data (will be serialized automatically).
    /// </summary>
    private NetworkMessage CreateNetworkMessage(string type, string playerId, object data)
    {
        var options = MessagePackSerializerOptions.Standard
            .WithResolver(MessagePack.Resolvers.StandardResolverAllowPrivate.Instance);
        return new NetworkMessage
        {
            Type = type,
            PlayerId = playerId,
            Data = MessagePackSerializer.Serialize(data, options),
            Timestamp = DateTime.UtcNow
        };
    }

    // =============================================
    // INCOMING MESSAGE PROCESSING
    // =============================================

    private async Task ListenForMessages()
    {
        _logger.LogInformation("🎧 Started listening for UDP messages");

        while (_isRunning)
        {
            try
            {
                if (_udpServer == null || !_isRunning)
                {
                    _logger.LogDebug("UDP server is null or not running, stopping listener");
                    break;
                }

                var result = await _udpServer.ReceiveAsync();
                Interlocked.Increment(ref _packetsReceived);

                // Process message asynchronously to avoid blocking
                _ = Task.Run(() => ProcessIncomingMessage(result.RemoteEndPoint, result.Buffer));
            }
            catch (ObjectDisposedException)
            {
                _logger.LogInformation("🔌 UDP server disposed, stopping message listener");
                break; // Expected when shutting down
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Error 10054 - Cliente desconectado abruptamente
                _logger.LogDebug("🔌 Client disconnected abruptly (ConnectionReset), continuing to listen...");

                // NO hacer break aquí, continuar escuchando para otros clientes
                await Task.Delay(100); // Pequeña pausa para evitar spam
                continue;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                _logger.LogDebug("🔌 Socket operation interrupted, stopping listener");
                break; // Interrupción intencional
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.NotSocket)
            {
                _logger.LogWarning("⚠️  Socket is not valid, attempting to restart listener");
                await AttemptSocketRestart();
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning("⚠️  Socket error while listening: {ErrorCode} - {Message}",
                    ex.SocketErrorCode, ex.Message);

                // Para otros errores de socket, hacer una pausa y continuar
                await Task.Delay(1000);

                // Si hay muchos errores consecutivos, salir del loop
                if (!_isRunning) break;
                continue;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Invalid operation in UDP listener");
                await Task.Delay(2000);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error while listening for UDP messages");
                await Task.Delay(2000); // Pausa más larga para errores inesperados

                // Si el servidor sigue corriendo, continuar; si no, salir
                if (!_isRunning) break;
                continue;
            }
        }

        _logger.LogInformation("🔇 Stopped listening for UDP messages");
    }

    private async Task AttemptSocketRestart()
    {
        try
        {
            _logger.LogInformation("🔄 Attempting to restart UDP socket...");

            // Cerrar socket actual
            _udpServer?.Close();
            _udpServer?.Dispose();

            // Esperar un momento
            await Task.Delay(1000);

            // Crear nuevo socket
            _udpServer = new UdpClient(_settings.UdpPort)
            {
                Client =
            {
                ReceiveTimeout = 5000,
                SendTimeout = 5000
            }
            };

            _logger.LogInformation("✅ UDP socket restarted successfully on port {Port}", _settings.UdpPort);

            // Reiniciar el listener
            _ = Task.Run(ListenForMessages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to restart UDP socket");
            _isRunning = false; // Parar el servicio si no podemos reiniciar
        }
    }

    private async Task ProcessIncomingMessage(IPEndPoint clientEndPoint, byte[] messageData)
    {
        try
        {
            // Rate limiting check
            if (!_rateLimitingService.IsAllowed(clientEndPoint, "general"))
            {
                _logger.LogWarning("Rate limit exceeded for {EndPoint}", clientEndPoint);
                return;
            }

            // ⭐ MESSAGEPACK: Deserialize incoming messages with MessagePack for consistency
            var networkMessage = MessagePackSerializer.Deserialize<NetworkMessage>(messageData);
            if (networkMessage == null)
            {
                _logger.LogWarning("Failed to deserialize message from {EndPoint}", clientEndPoint);
                await SendErrorToClient(clientEndPoint, "Invalid message format");
                return;
            }

            _logger.LogDebug("Received {Type} from {EndPoint} (PlayerId: {PlayerId})",
                networkMessage.Type, clientEndPoint, networkMessage.PlayerId);

            // Validate message size
            if (messageData.Length > _settings.NetworkSettings.MaxPacketSize)
            {
                _logger.LogWarning("Message too large from {EndPoint}: {Size} bytes",
                    clientEndPoint, messageData.Length);
                await SendErrorToClient(clientEndPoint, "Message too large");
                return;
            }

            // Update client activity
            UpdateClientActivity(clientEndPoint, networkMessage.PlayerId);

            // Additional rate limiting per message type
            if (!_rateLimitingService.IsAllowed(clientEndPoint, networkMessage.Type))
            {
                _logger.LogWarning("Rate limit exceeded for message type {Type} from {EndPoint}",
                    networkMessage.Type, clientEndPoint);
                return;
            }

            // Route message based on type
            switch (networkMessage.Type.ToLower())
            {
                case "heartbeat":
                    await HandleHeartbeat(clientEndPoint, networkMessage);
                    break;
                case "connect":
                    await HandleClientConnect(clientEndPoint, networkMessage);
                    break;

                case "reconnect":
                    await HandleClientReconnect(clientEndPoint, networkMessage);
                    break;

                case "player_input":
                    await HandlePlayerInput(clientEndPoint, networkMessage);
                    break;

                case "loot_grab":
                    await HandleLootGrab(clientEndPoint, networkMessage);
                    break;

                case "chat":
                    await HandleChat(clientEndPoint, networkMessage);
                    break;

                case "use_item":
                    await HandleUseItem(clientEndPoint, networkMessage);
                    break;

                case "extraction":
                    await HandleExtraction(clientEndPoint, networkMessage);
                    break;

                // REMOVED: trade_request (incomplete feature - add in future version)

                case "ping":
                    await HandlePing(clientEndPoint, networkMessage);
                    break;

                case "message_ack":
                    await HandleMessageAck(clientEndPoint, networkMessage);
                    break;

                case "disconnect":
                    await HandleClientDisconnect(clientEndPoint, networkMessage);
                    break;

                default:
                    _logger.LogWarning("Unknown message type '{Type}' from {EndPoint}",
                        networkMessage.Type, clientEndPoint);
                    await SendErrorToClient(clientEndPoint, $"Unknown message type: {networkMessage.Type}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error from {EndPoint}", clientEndPoint);
            await SendErrorToClient(clientEndPoint, "Invalid message format");
            _rateLimitingService.RecordViolation(clientEndPoint, "JSON parse error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {EndPoint}", clientEndPoint);
            await SendErrorToClient(clientEndPoint, "Internal server error");
        }
    }

    // =============================================
    // SPECIFIC MESSAGE HANDLERS
    // =============================================
    private async Task HandleHeartbeat(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        if (!_connectedClients.TryGetValue(clientEndPoint, out var client))
        {
            return;
        }

        try
        {
            // Actualizar actividad del cliente
            client.LastActivity = DateTime.UtcNow;

            // Log solo en modo debug para evitar spam
            _logger.LogDebug("Heartbeat received from {PlayerName} ({PlayerId})",
                client.Player.PlayerName, client.Player.PlayerId);

            // Responder con heartbeat_ack para confirmar recepción
            await SendAsync(clientEndPoint, CreateNetworkMessage("heartbeat_ack", client.Player.PlayerId, new
            {
                ServerTime = DateTime.UtcNow,
                ClientLastActivity = client.LastActivity
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling heartbeat from {EndPoint}", clientEndPoint);
        }
    }
    private async Task HandleClientConnect(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        try
        {
            // Convert message.Data to ClientConnectData
            var connectData = ConvertMessageData<ClientConnectData>(message.Data);
            if (connectData == null)
            {
                await SendErrorToClient(clientEndPoint, "Invalid connect data");
                return;
            }

            // Enhanced validation (mantener validaciones existentes)
            if (string.IsNullOrWhiteSpace(connectData.PlayerName) ||
                connectData.PlayerName.Length > 20 ||
                connectData.PlayerName.Length < 3)
            {
                await SendErrorToClient(clientEndPoint, "Player name must be 3-20 characters");
                return;
            }

            var validClasses = new[] { "scout", "tank", "support" };
            if (!validClasses.Contains(connectData.PlayerClass.ToLower()))
            {
                await SendErrorToClient(clientEndPoint, "Invalid player class. Must be: scout, tank, or support");
                return;
            }

            if (string.IsNullOrWhiteSpace(connectData.TeamId) ||
                !connectData.TeamId.StartsWith("team"))
            {
                await SendErrorToClient(clientEndPoint, "Invalid team ID format. Must start with 'team'");
                return;
            }

            // Check for duplicate names
            if (_connectedClients.Values.Any(c => c.Player.PlayerName.Equals(connectData.PlayerName, StringComparison.OrdinalIgnoreCase)))
            {
                await SendErrorToClient(clientEndPoint, "Player name already in use");
                return;
            }

            if (_connectedClients.ContainsKey(clientEndPoint))
            {
                await SendErrorToClient(clientEndPoint, "Already connected");
                return;
            }

            // Create enhanced player
            var player = CreateEnhancedPlayer(connectData, clientEndPoint);

            // NUEVO: Usar el sistema de matchmaking en lugar de FindBalancedWorld
            var worldId = _gameEngine.FindOrCreateWorld(connectData.TeamId);
            if (!_gameEngine.AddPlayerToWorld(worldId, player))
            {
                await SendErrorToClient(clientEndPoint, "Failed to join world - server full or lobby unavailable");
                return;
            }

            // ⭐ RECONNECTION: Create session for player
            var isInLobby = IsLobby(worldId);
            var sessionToken = _sessionManager.CreateSession(player, worldId, isInLobby);

            // Add to connected clients
            var connectedClient = new ConnectedClient
            {
                EndPoint = clientEndPoint,
                Player = player,
                WorldId = worldId,
                LastActivity = DateTime.UtcNow,
                SessionToken = sessionToken
            };

            _connectedClients[clientEndPoint] = connectedClient;

            _logger.LogInformation("Player '{PlayerName}' ({Class}) connected to world {WorldId} on team {TeamId} [Session: {SessionToken}]",
                player.PlayerName, player.PlayerClass, worldId, player.TeamId, sessionToken.Substring(0, 8) + "...");

            // NUEVO: Enviar respuesta de conexión con información de lobby/mundo y session token
            await SendConnectionResponse(clientEndPoint, player, worldId, sessionToken);

            // Notify other players in the world/lobby
            BroadcastToWorld(worldId, CreateNetworkMessage("player_joined", player.PlayerId, new
            {
                player.PlayerName,
                player.PlayerClass,
                player.TeamId,
                IsLobby = IsLobby(worldId) // Nuevo campo para indicar si es lobby
            }), excludeEndPoint: clientEndPoint);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client connect from {EndPoint}", clientEndPoint);
            await SendErrorToClient(clientEndPoint, "Connection failed");
        }
    }

    /// <summary>
    /// ⭐ RECONNECTION: Handle client reconnection using session token
    /// </summary>
    private async Task HandleClientReconnect(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        try
        {
            // Convert message.Data to ReconnectRequestData
            var reconnectData = ConvertMessageData<ReconnectRequestData>(message.Data);
            if (reconnectData == null)
            {
                await SendErrorToClient(clientEndPoint, "Invalid reconnect data");
                return;
            }

            _logger.LogInformation("Reconnection attempt from {EndPoint} with session {SessionToken}",
                clientEndPoint, reconnectData.SessionToken.Substring(0, 8) + "...");

            // Validate reconnection
            var (success, session, errorMessage) = _sessionManager.ValidateReconnection(reconnectData.SessionToken);

            if (!success || session == null)
            {
                _logger.LogWarning("Reconnection failed for {EndPoint}: {Error}", clientEndPoint, errorMessage);
                await SendAsync(clientEndPoint, CreateNetworkMessage("reconnect_response", "", new ReconnectResponseData
                {
                    Success = false,
                    ErrorMessage = errorMessage
                }));
                return;
            }

            // Verify player name matches
            if (!session.PlayerName.Equals(reconnectData.PlayerName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Reconnection failed: player name mismatch. Expected {Expected}, got {Got}",
                    session.PlayerName, reconnectData.PlayerName);
                await SendAsync(clientEndPoint, CreateNetworkMessage("reconnect_response", "", new ReconnectResponseData
                {
                    Success = false,
                    ErrorMessage = "Player name does not match session"
                }));
                return;
            }

            // Restore player from saved state
            var restoredPlayer = RestorePlayerFromSession(session, clientEndPoint);

            // Add player back to the world
            if (!_gameEngine.AddPlayerToWorld(session.WorldId, restoredPlayer))
            {
                _logger.LogError("Failed to add reconnected player {PlayerName} back to world {WorldId}",
                    session.PlayerName, session.WorldId);
                await SendAsync(clientEndPoint, CreateNetworkMessage("reconnect_response", "", new ReconnectResponseData
                {
                    Success = false,
                    ErrorMessage = "Failed to rejoin world - world may have ended"
                }));
                return;
            }

            // Re-add to connected clients
            var connectedClient = new ConnectedClient
            {
                EndPoint = clientEndPoint,
                Player = restoredPlayer,
                WorldId = session.WorldId,
                LastActivity = DateTime.UtcNow,
                SessionToken = session.SessionToken
            };

            _connectedClients[clientEndPoint] = connectedClient;

            // Activate session
            _sessionManager.ActivateSession(session.SessionToken);

            var timeSinceDisconnect = (float)(DateTime.UtcNow - session.PlayerState!.SavedAt).TotalSeconds;

            _logger.LogInformation("Player '{PlayerName}' reconnected successfully to world {WorldId}. Offline for {Time}s",
                session.PlayerName, session.WorldId, timeSinceDisconnect);

            // Send success response with restored state
            await SendAsync(clientEndPoint, CreateNetworkMessage("reconnect_response", restoredPlayer.PlayerId, new ReconnectResponseData
            {
                Success = true,
                ErrorMessage = string.Empty,
                PlayerId = restoredPlayer.PlayerId,
                WorldId = session.WorldId,
                IsInLobby = session.IsInLobby,

                // Restored state
                Position = restoredPlayer.Position,
                Velocity = restoredPlayer.Velocity,
                Direction = restoredPlayer.Direction,
                CurrentRoomId = restoredPlayer.CurrentRoomId,

                Health = restoredPlayer.Health,
                MaxHealth = restoredPlayer.MaxHealth,
                Mana = restoredPlayer.Mana,
                MaxMana = restoredPlayer.MaxMana,
                Shield = restoredPlayer.Shield,
                Level = restoredPlayer.Level,
                ExperiencePoints = restoredPlayer.ExperiencePoints,
                IsAlive = restoredPlayer.IsAlive,

                TeamId = restoredPlayer.TeamId,
                PlayerClass = restoredPlayer.PlayerClass,

                InventoryCount = restoredPlayer.Inventory.Count,
                ActiveEffectsCount = restoredPlayer.StatusEffects.Count,

                ServerTime = (float)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds,
                TimeSinceDisconnect = timeSinceDisconnect
            }));

            // Notify other players
            BroadcastToWorld(session.WorldId, CreateNetworkMessage("player_reconnected", restoredPlayer.PlayerId, new
            {
                restoredPlayer.PlayerName,
                restoredPlayer.PlayerClass,
                restoredPlayer.TeamId
            }), excludeEndPoint: clientEndPoint);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client reconnect from {EndPoint}", clientEndPoint);
            await SendErrorToClient(clientEndPoint, "Reconnection failed");
        }
    }

    /// <summary>
    /// ⭐ RECONNECTION: Restore RealTimePlayer from saved session state
    /// </summary>
    private RealTimePlayer RestorePlayerFromSession(PlayerSession session, IPEndPoint endPoint)
    {
        var savedState = session.PlayerState!;

        var player = new RealTimePlayer
        {
            // Identity
            PlayerId = savedState.PlayerId,
            PlayerName = savedState.PlayerName,
            TeamId = savedState.TeamId,
            PlayerClass = savedState.PlayerClass,
            EndPoint = endPoint,

            // Position & Movement
            Position = savedState.Position,
            Velocity = savedState.Velocity,
            Direction = savedState.Direction,
            CurrentRoomId = savedState.CurrentRoomId,

            // Combat & Stats
            IsAlive = savedState.IsAlive,
            Health = savedState.Health,
            MaxHealth = savedState.MaxHealth,
            Mana = savedState.Mana,
            MaxMana = savedState.MaxMana,
            Shield = savedState.Shield,
            MaxShield = savedState.MaxShield,

            // Progression
            Level = savedState.Level,
            ExperiencePoints = savedState.ExperiencePoints,
            Stats = new Dictionary<string, int>(savedState.Stats),

            // Inventory (deep copy restored)
            Inventory = savedState.Inventory.Select(item => new LootItem
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                ItemType = item.ItemType,
                Rarity = item.Rarity,
                Stats = new Dictionary<string, int>(item.Stats),
                Value = item.Value
            }).ToList(),

            // Status Effects (restored)
            StatusEffects = savedState.StatusEffects.Select(effect => new StatusEffect
            {
                EffectType = effect.EffectType,
                Value = effect.Value,
                AppliedAt = effect.AppliedAt,
                ExpiresAt = effect.AppliedAt.AddSeconds(effect.Duration),
                SourcePlayerId = effect.SourcePlayerId
            }).ToList(),

            // Reset network/activity fields
            LastActivity = DateTime.UtcNow,
            AbilityCooldowns = new Dictionary<string, DateTime>(),
            MovementSpeedModifier = 1.0f,
            DamageReduction = 0.0f
        };

        // ⭐ DELTA COMPRESSION: Force next update for reconnected player
        player.ForceNextUpdate();

        return player;
    }

    private async Task SendConnectionResponse(IPEndPoint clientEndPoint, RealTimePlayer player, string worldId, string sessionToken)
    {
        var isLobby = IsLobby(worldId);
        var lobbyStats = _gameEngine.GetLobbyStats();

        await SendAsync(clientEndPoint, CreateNetworkMessage("connected", player.PlayerId, new
        {
            player.PlayerId,
            WorldId = worldId,
            IsLobby = isLobby,
            // ⭐ RECONNECTION: Session token for client to save
            SessionToken = sessionToken,
            SpawnPosition = player.Position,
            PlayerStats = new
            {
                player.Level,
                player.Health,
                player.MaxHealth,
                player.Mana,
                player.MaxMana,
                player.ExperiencePoints
            },
            ServerInfo = new
            {
                TickRate = _settings.TargetFPS,
                WorldBounds = new { X = 240, Y = 240 },
                _settings.MaxPlayersPerWorld,
                _settings.LobbySettings.MinPlayersToStart
            },
            LobbyInfo = isLobby ? new
            {
                Status = "waiting_for_players",
                CurrentPlayers = GetPlayersInWorld(worldId),
                MaxPlayers = _settings.MaxPlayersPerWorld,
                _settings.LobbySettings.MinPlayersToStart,
                EstimatedStartTime = GetEstimatedStartTime(worldId)
            } : null
        }));
    }

    private bool IsLobby(string worldId)
    {
        return _gameEngine.IsWorldLobby(worldId);
    }

    private int GetPlayersInWorld(string worldId)
    {
        return _connectedClients.Values.Count(c => c.WorldId == worldId);
    }

    private DateTime? GetEstimatedStartTime(string worldId)
    {
        var lobbyInfo = _gameEngine.GetLobbyInfo(worldId);
        if (lobbyInfo == null) return null;

        var playersInWorld = GetPlayersInWorld(worldId);
        if (playersInWorld >= lobbyInfo.MinPlayersToStart)
        {
            var timeSinceLastJoin = DateTime.UtcNow - lobbyInfo.LastPlayerJoined;
            var remainingWaitTime = Math.Max(0, _settings.LobbySettings.MaxWaitTimeSeconds - timeSinceLastJoin.TotalSeconds);
            return DateTime.UtcNow.AddSeconds(remainingWaitTime);
        }

        return null;
    }

    public void NotifyGameStarted(string worldId)
    {
        BroadcastToWorld(worldId, CreateNetworkMessage("game_started", string.Empty, new
        {
            WorldId = worldId,
            Message = "The game has started! Good luck!",
            Timestamp = DateTime.UtcNow
        }));

        _logger.LogInformation("Notified all players in world {WorldId} that the game has started", worldId);
    }

    private void SendLobbyUpdates()
    {
        var lobbyStats = _gameEngine.GetLobbyStats();

        var lobbyGroups = _connectedClients.Values
            .Where(c => IsLobby(c.WorldId))
            .GroupBy(c => c.WorldId);

        foreach (var lobbyGroup in lobbyGroups)
        {
            var worldId = lobbyGroup.Key;
            var playersInLobby = lobbyGroup.Count();

            var lobbyUpdate = CreateNetworkMessage("lobby_update", string.Empty, new
            {
                WorldId = worldId,
                CurrentPlayers = playersInLobby,
                MaxPlayers = _settings.MaxPlayersPerWorld,
                _settings.LobbySettings.MinPlayersToStart,
                TeamCounts = lobbyGroup
                    .GroupBy(c => c.Player.TeamId)
                    .ToDictionary(g => g.Key, g => g.Count()),
                EstimatedStartTime = GetEstimatedStartTime(worldId),
                Status = playersInLobby >= _settings.LobbySettings.MinPlayersToStart ? "ready" : "waiting"
            });

            BroadcastToWorld(worldId, lobbyUpdate);
        }
    }
    private async Task HandlePlayerInput(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        if (!_connectedClients.TryGetValue(clientEndPoint, out var client))
        {
            _logger.LogWarning("Received input from unknown client {EndPoint}", clientEndPoint);
            return;
        }

        try
        {
            // Convert message.Data to PlayerInputMessage
            var inputData = ConvertMessageData<PlayerInputMessage>(message.Data);
            if (inputData == null) return;

            if (inputData.MoveInput.Magnitude > 1.1f)
            {
                _logger.LogWarning("Invalid move input magnitude from {PlayerId}: {Magnitude}",
                    client.Player.PlayerId, inputData.MoveInput.Magnitude);
                _rateLimitingService.RecordViolation(clientEndPoint, "Invalid input magnitude");
                return;
            }

            message.PlayerId = client.Player.PlayerId;
            // Data is already deserialized as object by MessagePack
            _gameEngine.QueueInput(message);

            client.LastActivity = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing player input from {PlayerId}", client.Player.PlayerId);
        }
    }

    private async Task HandleLootGrab(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        if (!_connectedClients.TryGetValue(clientEndPoint, out var client))
            return;

        try
        {
            // Convert message.Data to LootGrabMessage
            var lootGrab = ConvertMessageData<LootGrabMessage>(message.Data);
            if (lootGrab == null || string.IsNullOrWhiteSpace(lootGrab.LootId))
            {
                await SendErrorToClient(clientEndPoint, "Invalid loot grab data");
                return;
            }

            message.PlayerId = client.Player.PlayerId;
            // Data is already deserialized as object by MessagePack
            _gameEngine.QueueInput(message);
            client.LastActivity = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing loot grab from {PlayerId}", client.Player.PlayerId);
        }
    }

    private async Task HandleChat(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        if (!_connectedClients.TryGetValue(clientEndPoint, out var client))
            return;

        try
        {
            // Convert message.Data to ChatMessage
            var chatData = ConvertMessageData<ChatMessage>(message.Data);
            if (chatData == null || string.IsNullOrWhiteSpace(chatData.Message))
                return;

            if (chatData.Message.Length > 200)
            {
                await SendErrorToClient(clientEndPoint, "Chat message too long (max 200 characters)");
                return;
            }

            if (IsSpamMessage(chatData.Message))
            {
                _logger.LogWarning("Spam message detected from {PlayerName}: {Message}",
                    client.Player.PlayerName, chatData.Message);
                _rateLimitingService.RecordViolation(clientEndPoint, "Spam message");
                return;
            }

            var sanitizedMessage = SanitizeMessage(chatData.Message);

            _logger.LogInformation("Chat from {PlayerName} [{ChatType}]: {Message}",
                client.Player.PlayerName, chatData.ChatType, sanitizedMessage);

            await BroadcastChatMessage(client, sanitizedMessage, chatData.ChatType);

            client.LastActivity = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat from {PlayerId}", client.Player.PlayerId);
        }
    }

    private async Task HandleUseItem(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        if (!_connectedClients.TryGetValue(clientEndPoint, out var client))
            return;

        try
        {
            // Convert message.Data to UseItemMessage
            var useItemData = ConvertMessageData<UseItemMessage>(message.Data);
            if (useItemData == null || string.IsNullOrWhiteSpace(useItemData.ItemId))
            {
                await SendErrorToClient(clientEndPoint, "Invalid use item data");
                return;
            }

            message.PlayerId = client.Player.PlayerId;
            // Data is already deserialized as object by MessagePack
            _gameEngine.QueueInput(message);

            client.LastActivity = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing use item from {PlayerId}", client.Player.PlayerId);
        }
    }

    private async Task HandleExtraction(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        if (!_connectedClients.TryGetValue(clientEndPoint, out var client))
            return;

        try
        {
            // Convert message.Data to ExtractionMessage
            var extractionData = ConvertMessageData<ExtractionMessage>(message.Data);
            if (extractionData == null || string.IsNullOrWhiteSpace(extractionData.Action))
            {
                await SendErrorToClient(clientEndPoint, "Invalid extraction data");
                return;
            }

            // Validate action
            if (!new[] { "start", "cancel" }.Contains(extractionData.Action.ToLower()))
            {
                await SendErrorToClient(clientEndPoint, "Invalid extraction action");
                return;
            }

            message.PlayerId = client.Player.PlayerId;
            // Data is already deserialized as object by MessagePack
            _gameEngine.QueueInput(message);

            client.LastActivity = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing extraction from {PlayerId}", client.Player.PlayerId);
        }
    }

    // REMOVED: HandleTradeRequest (incomplete feature - will implement in future version)

    private async Task HandlePing(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        var timestamp = DateTime.UtcNow;

        // Simply echo back the client's ping data along with server timestamp
        await SendAsync(clientEndPoint, CreateNetworkMessage("pong", message.PlayerId, new
        {
            ClientData = message.Data, // Echo back client's ping data
            ServerTime = timestamp
        }));
    }

    private async Task HandleMessageAck(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        try
        {
            var ack = ConvertMessageData<MessageAcknowledgement>(message.Data);
            if (ack == null || string.IsNullOrWhiteSpace(ack.MessageId)) return;

            if (_pendingAcks.TryRemove(ack.MessageId, out var originalMessage))
            {
                _logger.LogDebug("Received ACK for message {MessageId}", ack.MessageId);
            }

            if (!ack.Success && !string.IsNullOrWhiteSpace(ack.ErrorMessage))
            {
                _logger.LogWarning("Client {EndPoint} rejected message {MessageId}: {Error}",
                    clientEndPoint, ack.MessageId, ack.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message acknowledgement from {EndPoint}", clientEndPoint);
        }
    }

    private async Task HandleClientDisconnect(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        await RemoveClient(clientEndPoint, "Player disconnected gracefully");
    }

    // =============================================
    // OPTIMIZED OUTGOING MESSAGE PROCESSING
    // =============================================

    private void SendUpdatesToClients(object? state)
    {
        try
        {
            if (!_isRunning || _connectedClients.IsEmpty) return;

            _worldUpdateCounter++;

            // Enviar lobby updates cada 5 segundos
            if (_worldUpdateCounter % (30 * 5) == 0)
            {
                SendLobbyUpdates();
            }

            // Enviar world updates solo para mundos activos (no lobbies)
            if (_worldUpdateCounter % 3 == 0)
            {
                SendOptimizedWorldUpdates();
            }

            // Player state updates cada 2 frames (15 FPS)
            if (_worldUpdateCounter % 2 == 0)
            {
                SendOptimizedPlayerStateUpdates();
            }

            // World state updates cada 10 segundos
            if (_worldUpdateCounter % (30 * 10) == 0)
            {
                SendOptimizedWorldStateUpdates();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending updates to clients");
        }
    }

    private void SendOptimizedWorldUpdates()
    {
        var worldUpdates = _gameEngine.GetWorldUpdates();

        foreach (var (worldId, update) in worldUpdates)
        {
            var clientsInWorld = _connectedClients.Values
                .Where(c => c.WorldId == worldId)
                .ToList();

            // Send to all clients in world
            foreach (var client in clientsInWorld)
            {
                _ = Task.Run(() => SendSplitWorldUpdate(client.EndPoint, update));
            }

            // ⭐ PERF: Return update to pool after sending to all clients
            // Note: This assumes serialization happens quickly (which it does)
            // For more complex scenarios, implement reference counting
            Task.Run(async () =>
            {
                // Wait a bit for sends to complete
                await Task.Delay(100);
                _gameEngine.ReturnWorldUpdateToPool(update);
            });
        }
    }

    private async Task SendSplitWorldUpdate(IPEndPoint clientEndPoint, WorldUpdateMessage update)
    {
        try
        {
            // Solo enviar combat events si existen
            if (update.CombatEvents?.Any() == true)
            {
                await SendIfSmallEnough(clientEndPoint, CreateNetworkMessage("combat_events", string.Empty, new { update.CombatEvents }));
            }

            // Solo enviar loot updates si existen
            if (update.LootUpdates?.Any() == true)
            {
                await SendIfSmallEnough(clientEndPoint, CreateNetworkMessage("loot_updates", string.Empty, new { update.LootUpdates }));
            }

            // OPTIMIZACIÓN CLAVE: Solo enviar mob updates si hay mobs que realmente cambiaron
            if (update.MobUpdates?.Any() == true)
            {
                const int maxMobsPerChunk = 5;
                var mobChunks = update.MobUpdates
                    .Select((mob, index) => new { mob, index })
                    .GroupBy(x => x.index / maxMobsPerChunk)
                    .Select(g => g.Select(x => x.mob).ToList())
                    .ToList();

                foreach (var chunk in mobChunks)
                {
                    await SendIfSmallEnough(clientEndPoint, CreateNetworkMessage("mob_updates_chunk", string.Empty, new
                    {
                        MobUpdates = chunk,
                        ChunkIndex = mobChunks.IndexOf(chunk),
                        TotalChunks = mobChunks.Count
                    }));
                }
            }

            if (update.CombatEvents?.Any() == true ||
                update.LootUpdates?.Any() == true ||
                update.MobUpdates?.Any() == true)
            {
                await SendIfSmallEnough(clientEndPoint, CreateNetworkMessage("frame_update", string.Empty, new { update.FrameNumber }));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending split world update to {EndPoint}", clientEndPoint);
        }
    }

    private void SendOptimizedPlayerStateUpdates()
    {
        var groupedClients = _connectedClients.Values.GroupBy(c => c.WorldId);

        foreach (var worldGroup in groupedClients)
        {
            var worldId = worldGroup.Key;
            var players = worldGroup.Select(c => c.Player).ToList();

            // Split players into smaller batches to avoid large messages
            const int maxPlayersPerBatch = 8;

            for (int i = 0; i < players.Count; i += maxPlayersPerBatch)
            {
                var batch = players.Skip(i).Take(maxPlayersPerBatch).ToList();

                var playerUpdates = batch.Select(p => new
                {
                    p.PlayerId,
                    Position = new { X = Math.Round(p.Position.X, 1), Y = Math.Round(p.Position.Y, 1) },
                    Velocity = new { X = Math.Round(p.Velocity.X, 1), Y = Math.Round(p.Velocity.Y, 1) },
                    Direction = Math.Round(p.Direction, 2),
                    p.Health,
                    p.MaxHealth,
                    p.Mana,
                    p.MaxMana,
                    p.Level,
                    p.IsAlive,
                    p.IsMoving,
                    p.IsCasting,
                    p.CurrentRoomId
                }).ToList();

                var message = CreateNetworkMessage("player_states_batch", string.Empty, new
                {
                    Players = playerUpdates,
                    BatchIndex = i / maxPlayersPerBatch,
                    TotalBatches = (players.Count + maxPlayersPerBatch - 1) / maxPlayersPerBatch
                });

                foreach (var client in worldGroup)
                {
                    _ = Task.Run(() => SendIfSmallEnough(client.EndPoint, message));
                }
            }
        }
    }

    private void SendOptimizedWorldStateUpdates()
    {
        var worldStates = _gameEngine.GetWorldStates();

        foreach (var (worldId, worldState) in worldStates)
        {
            var clientsInWorld = _connectedClients.Values
                .Where(c => c.WorldId == worldId)
                .ToList();

            // Send only essential world state info
            var essentialWorldState = CreateNetworkMessage("world_state_essential", string.Empty, new
            {
                WorldInfo = new
                {
                    worldState.WorldInfo?.WorldId,
                    IsCompleted = worldState.WorldInfo?.IsCompleted ?? false,
                    worldState.WorldInfo?.WinningTeam,
                    CompletedRooms = worldState.WorldInfo?.CompletedRooms ?? 0,
                    TotalRooms = worldState.WorldInfo?.TotalRooms ?? 0,
                    TotalLoot = worldState.WorldInfo?.TotalLoot ?? 0
                }
            });

            foreach (var client in clientsInWorld)
            {
                _ = Task.Run(() => SendIfSmallEnough(client.EndPoint, essentialWorldState));
            }
        }
    }

    private async Task SendIfSmallEnough(IPEndPoint clientEndPoint, NetworkMessage message)
    {
        // ⭐ MESSAGEPACK: 10x faster serialization, 60% smaller payload
        var data = MessagePackSerializer.Serialize(message);

        if (data.Length <= _maxUdpPacketSize)
        {
            if (_udpServer != null)
            {
                await _udpServer.SendAsync(data, clientEndPoint);
                Interlocked.Increment(ref _packetsSent);
            }
        }
        else
        {
            _logger.LogWarning("Message {Type} still too large ({Size} bytes), dropping",
                message.Type, data.Length);
        }
    }

    public async Task SendAsync(IPEndPoint endPoint, NetworkMessage message)
    {
        if (!_isRunning || _udpServer == null) return;

        try
        {
            // ⭐ MESSAGEPACK: 10x faster serialization, 60% smaller payload
            var data = MessagePackSerializer.Serialize(message);

            // Comprimir si el mensaje es grande
            if (data.Length > 1200) // Umbral para compresión
            {
                var compressed = Compress(data);
                if (compressed.Length < data.Length)
                {
                    var compressedPacket = new byte[compressed.Length + 1];
                    compressedPacket[0] = 0x1; // Flag de compresión
                    Buffer.BlockCopy(compressed, 0, compressedPacket, 1, compressed.Length);
                    data = compressedPacket;
                }
            }

            if (data.Length > 1472) // MTU IPv4 estándar
            {
                _logger.LogWarning("📦 Message too large ({Length} bytes) to {EndPoint}, dropping",
                    data.Length, endPoint);
                return;
            }

            await _udpServer.SendAsync(data, data.Length, endPoint);
            Interlocked.Increment(ref _packetsSent);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
        {
            _logger.LogDebug("🔌 Client {EndPoint} disconnected while sending message", endPoint);

            // Remover cliente de la lista de conectados
            if (_connectedClients.TryGetValue(endPoint, out var client))
            {
                _ = Task.Run(() => RemoveClient(endPoint, "Connection reset during send"));
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostUnreachable)
        {
            _logger.LogDebug("🚫 Host unreachable: {EndPoint}", endPoint);

            // Marcar cliente para remoción
            if (_connectedClients.ContainsKey(endPoint))
            {
                _ = Task.Run(() => RemoveClient(endPoint, "Host unreachable"));
            }
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("⚠️  Socket error sending to {EndPoint}: {ErrorCode} - {Message}",
                endPoint, ex.SocketErrorCode, ex.Message);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("🔌 UDP server disposed while sending message");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Unexpected error sending message to {EndPoint}", endPoint);
        }
    }

    private byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Fastest))
        {
            compressor.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private byte[] Decompress(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private void HandleGameStarted(string worldId, List<string> playerIds)
    {
        try
        {
            _logger.LogInformation("🎮 Notifying {Count} players that game {WorldId} has started",
                playerIds.Count, worldId);

            // Crear mensaje de juego iniciado
            var gameStartedMessage = CreateNetworkMessage("game_started", string.Empty, new
            {
                WorldId = worldId,
                Message = "The game has started! Good luck!",
                Timestamp = DateTime.UtcNow,
                GameMode = "extraction_battle_royale",
                Instructions = new[]
                {
                    "Explore the world and collect loot",
                    "Fight other teams for survival",
                    "Complete rooms to unlock extraction",
                    "Extract safely to win the round"
                }
            });

            // Enviar a todos los jugadores del mundo
            var clientsInWorld = _connectedClients.Values
                .Where(c => playerIds.Contains(c.Player.PlayerId))
                .ToList();

            _logger.LogDebug("Found {ClientCount} connected clients for world {WorldId}",
                clientsInWorld.Count, worldId);

            foreach (var client in clientsInWorld)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendAsync(client.EndPoint, gameStartedMessage);
                        _logger.LogDebug("Sent game_started to {PlayerName}", client.Player.PlayerName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send game_started to {PlayerName}",
                            client.Player.PlayerName);
                    }
                });
            }

            _logger.LogInformation("✅ Game started notifications sent to all players in world {WorldId}", worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling game started for world {WorldId}", worldId);
        }
    }
    private async Task HandleLargeMessage(IPEndPoint clientEndPoint, NetworkMessage message, byte[] data)
    {
        switch (message.Type.ToLower())
        {
            case "connected":
                // Already handled with simplified connection message
                _logger.LogWarning("Connection message too large, should not happen with simplified version");
                break;

            default:
                _logger.LogWarning("Dropping large message {Type} ({Size} bytes) to {EndPoint}",
                    message.Type, data.Length, clientEndPoint);
                break;
        }
    }

    private async Task SendErrorToClient(IPEndPoint clientEndPoint, string errorMessage)
    {
        await SendAsync(clientEndPoint, CreateNetworkMessage("error", string.Empty, new
        {
            Message = errorMessage,
            Timestamp = DateTime.UtcNow
        }));
    }// =============================================
     // BROADCASTING AND MESSAGING
     // =============================================

    private void BroadcastToWorld(string worldId, NetworkMessage message, IPEndPoint? excludeEndPoint = null)
{
    var clientsInWorld = _connectedClients.Values
        .Where(c => c.WorldId == worldId && (excludeEndPoint == null || !c.EndPoint.Equals(excludeEndPoint)))
        .ToList();

    var tasks = clientsInWorld.Select(client => SendAsync(client.EndPoint, message));

    // Fire and forget - don't await to maintain performance
    _ = Task.WhenAll(tasks);
}

private async Task BroadcastChatMessage(ConnectedClient sender, string message, string chatType)
{
    var chatMessage = CreateNetworkMessage("chat_received", sender.Player.PlayerId, new ChatReceivedData
    {
        PlayerName = sender.Player.PlayerName,
        Message = message,
        ChatType = chatType,
        Timestamp = DateTime.UtcNow
    });

    switch (chatType.ToLower())
    {
        case "team":
            // Broadcast to team members only
            var teamMembers = _connectedClients.Values
                .Where(c => c.Player.TeamId == sender.Player.TeamId && c.WorldId == sender.WorldId)
                .ToList();

            foreach (var member in teamMembers)
            {
                await SendAsync(member.EndPoint, chatMessage);
            }
            break;

        case "all":
        default:
            // Broadcast to all players in world
            BroadcastToWorld(sender.WorldId, chatMessage);
            break;
    }
}

// =============================================
// RELIABLE MESSAGING SYSTEM
// =============================================

private async Task SendReliableMessage(IPEndPoint clientEndPoint, ReliableMessage message)
{
    if (message.RequiresAck)
    {
        _pendingAcks[message.MessageId] = message;
    }

    var networkMessage = CreateNetworkMessage("reliable", string.Empty, message);

    await SendAsync(clientEndPoint, networkMessage);
}

private void ProcessReliableMessages(object? state)
{
    try
    {
        var currentTime = DateTime.UtcNow;
        var messagesToRetry = _pendingAcks.Values
            .Where(m => (currentTime - m.Timestamp).TotalSeconds > 5.0 && m.RetryCount < _settings.NetworkSettings.ReliableMessageRetries)
            .ToList();

        foreach (var message in messagesToRetry)
        {
            message.RetryCount++;
            message.Timestamp = currentTime;

            // Find client and retry
            var client = _connectedClients.Values
                .FirstOrDefault(c => c.Player.PlayerId == message.Data.ToString());

            if (client != null)
            {
                _ = Task.Run(() => SendReliableMessage(client.EndPoint, message));
                _logger.LogDebug("Retrying reliable message {MessageId} (attempt {Retry})",
                    message.MessageId, message.RetryCount);
            }
            else
            {
                // Remove message if client not found
                _pendingAcks.TryRemove(message.MessageId, out _);
            }
        }

        // Remove messages that have exceeded retry limit
        var expiredMessages = _pendingAcks.Values
            .Where(m => m.RetryCount >= _settings.NetworkSettings.ReliableMessageRetries)
            .ToList();

        foreach (var expired in expiredMessages)
        {
            _pendingAcks.TryRemove(expired.MessageId, out _);
            _logger.LogWarning("Reliable message {MessageId} expired after {Retries} retries",
                expired.MessageId, expired.RetryCount);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing reliable messages");
    }
}

/// <summary>
/// ⭐ RECONNECTION: Cleanup expired sessions periodically
/// </summary>
private void CleanupExpiredSessions(object? state)
{
    try
    {
        var removedCount = _sessionManager.CleanupExpiredSessions();

        if (removedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired reconnection sessions", removedCount);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error cleaning up expired sessions");
    }
}

    // =============================================
    // CLIENT MANAGEMENT
    // =============================================

    private void UpdateClientActivity(IPEndPoint clientEndPoint, string playerId)
    {
        if (_connectedClients.TryGetValue(clientEndPoint, out var client))
        {
            var oldActivity = client.LastActivity;
            client.LastActivity = DateTime.UtcNow;

            if ((DateTime.UtcNow - oldActivity).TotalSeconds > 30)
            {
                _logger.LogDebug("Client activity updated for {PlayerName} after {Seconds}s",
                    client.Player.PlayerName, (DateTime.UtcNow - oldActivity).TotalSeconds);
            }
        }
    }

    private string? FindBalancedWorld(string teamId)
    {
        // Find world with available space and balanced teams
        var availableWorlds = _gameEngine.GetAvailableWorlds(_settings.MaxPlayersPerWorld);

        foreach (var worldId in availableWorlds)
        {
            var teamCount = _connectedClients.Values
                .Where(c => c.WorldId == worldId && c.Player.TeamId == teamId)
                .Count();

            // Limit team size to prevent imbalance
            if (teamCount < _settings.GameBalance.MaxTeamSize)
            {
                return worldId;
            }
        }

        return null; // No balanced world found, will create new one
    }

private RealTimePlayer CreateEnhancedPlayer(ClientConnectData connectData, IPEndPoint endPoint)
{
    var baseStats = connectData.PlayerClass.ToLower() switch
    {
        "scout" => new { Health = 80, Mana = 120, Speed = 6.0f, Strength = 10, Agility = 15, Intelligence = 10 },
        "tank" => new { Health = 150, Mana = 80, Speed = 4.0f, Strength = 15, Agility = 8, Intelligence = 8 },
        "support" => new { Health = 100, Mana = 150, Speed = 5.0f, Strength = 8, Agility = 10, Intelligence = 15 },
        _ => new { Health = 100, Mana = 100, Speed = 5.0f, Strength = 10, Agility = 10, Intelligence = 10 }
    };

    var player = new RealTimePlayer
    {
        PlayerId = Guid.NewGuid().ToString(),
        PlayerName = connectData.PlayerName,
        PlayerClass = connectData.PlayerClass.ToLower(),
        TeamId = connectData.TeamId,
        EndPoint = endPoint,
        Position = GetSpawnPosition(connectData.TeamId),
        Health = baseStats.Health,
        MaxHealth = baseStats.Health,
        Mana = baseStats.Mana,
        MaxMana = baseStats.Mana,
        Level = 1,
        ExperiencePoints = 0,
        CurrentRoomId = "room_1_1", // Start in center room
        Stats = new Dictionary<string, int>
        {
            ["strength"] = baseStats.Strength,
            ["agility"] = baseStats.Agility,
            ["intelligence"] = baseStats.Intelligence,
            ["vitality"] = 10,
            ["luck"] = 10
        },
        Inventory = new List<LootItem>(),
        AbilityCooldowns = new Dictionary<string, DateTime>(),
        StatusEffects = new List<StatusEffect>(),
        MovementSpeedModifier = 1.0f,
        DamageReduction = 0.0f
    };

    // ⭐ DELTA COMPRESSION: Force first update for new player
    player.ForceNextUpdate();

    return player;
}

private Vector2 GetSpawnPosition(string teamId)
{
    // Spawn teams in different corners to promote early game safety
    return teamId.ToLower() switch
    {
        "team1" => new Vector2(30, 30),   // Top-left area
        "team2" => new Vector2(210, 30),  // Top-right area  
        "team3" => new Vector2(30, 210),  // Bottom-left area
        "team4" => new Vector2(210, 210), // Bottom-right area
        _ => new Vector2(120, 120)        // Center for unknown teams
    };
}

    private void CleanupDisconnectedClients(object? state)
    {
        try
        {
            var timeoutThreshold = DateTime.UtcNow.AddSeconds(-_settings.NetworkSettings.ClientTimeoutSeconds);
            var timedOutClients = _connectedClients.Values
                .Where(c => c.LastActivity < timeoutThreshold)
                .ToList();

            if (timedOutClients.Count > 0)
            {
                _logger.LogWarning("Found {Count} clients that timed out:", timedOutClients.Count);
                foreach (var client in timedOutClients)
                {
                    var secondsAgo = (DateTime.UtcNow - client.LastActivity).TotalSeconds;
                    var isLobby = IsLobby(client.WorldId);
                    _logger.LogWarning("TIMEOUT: Player {PlayerName} in {WorldType} {WorldId} - Last activity: {SecondsAgo:F0}s ago",
                        client.Player.PlayerName,
                        isLobby ? "LOBBY" : "GAME",
                        client.WorldId.Substring(Math.Max(0, client.WorldId.Length - 8)),
                        secondsAgo);
                }
            }

            foreach (var client in timedOutClients)
            {
                _ = Task.Run(() => RemoveClient(client.EndPoint, "Connection timeout"));
            }

            // Modificar el log final para ser más informativo
            if (timedOutClients.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} timed out clients", timedOutClients.Count);
            }
            else
            {
                _logger.LogDebug("Client cleanup check completed - no timeouts found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during client cleanup");
        }
    }

    private async Task RemoveClient(IPEndPoint clientEndPoint, string reason)
{
    if (_connectedClients.TryRemove(clientEndPoint, out var client))
    {
        _logger.LogInformation("Removing client {PlayerName} ({PlayerId}): {Reason}",
            client.Player.PlayerName, client.Player.PlayerId, reason);

        // ⭐ RECONNECTION: Determine if we should save state for reconnection
        var isGracefulDisconnect = reason.Contains("gracefully") || reason.Contains("Kicked by admin");
        var isInLobby = IsLobby(client.WorldId);

        if (!isGracefulDisconnect && !string.IsNullOrEmpty(client.SessionToken))
        {
            // Save player state for potential reconnection
            _sessionManager.SavePlayerState(client.SessionToken, client.Player, client.WorldId, isInLobby);

            _logger.LogInformation("Saved state for player {PlayerName}. Can reconnect within 5 minutes using session token.",
                client.Player.PlayerName);
        }
        else if (!string.IsNullOrEmpty(client.SessionToken))
        {
            // Graceful disconnect or kick - invalidate session
            _sessionManager.InvalidateSession(client.SessionToken, reason);

            _logger.LogInformation("Invalidated session for player {PlayerName} due to: {Reason}",
                client.Player.PlayerName, reason);
        }

        // Remove from game engine
        _gameEngine.RemovePlayerFromWorld(client.WorldId, client.Player.PlayerId);

        // Notify other players
        BroadcastToWorld(client.WorldId, CreateNetworkMessage("player_disconnected", client.Player.PlayerId, new
        {
            client.Player.PlayerName,
            Reason = reason,
            Timestamp = DateTime.UtcNow,
            // ⭐ RECONNECTION: Tell clients if player can reconnect
            CanReconnect = !isGracefulDisconnect
        }));

        // Remove any pending reliable messages for this client
        var pendingMessages = _pendingAcks.Values
            .Where(m => m.Data.ToString() == client.Player.PlayerId)
            .ToList();

        foreach (var message in pendingMessages)
        {
            _pendingAcks.TryRemove(message.MessageId, out _);
        }
    }
}

// =============================================
// ADMIN METHODS (called from AdminController)
// =============================================

    public void KickPlayer(string playerId, string reason)
    {
        var client = _connectedClients.Values
            .FirstOrDefault(c => c.Player.PlayerId == playerId);

        if (client != null)
        {
            _ = Task.Run(() => RemoveClient(client.EndPoint, $"Kicked by admin: {reason}"));
        }
        else
        {
            throw new InvalidOperationException($"Player {playerId} not found");
        }
    }

    public void BroadcastAdminMessage(string message, string? worldId = null)
    {
        var adminMessage = CreateNetworkMessage("admin_message", string.Empty, new
        {
            Message = message,
            Timestamp = DateTime.UtcNow,
            IsSystemMessage = true
        });

        if (!string.IsNullOrEmpty(worldId))
        {
            // Broadcast to specific world
            BroadcastToWorld(worldId, adminMessage);
        }
        else
        {
            // Broadcast to all connected clients
            var tasks = _connectedClients.Values
                .Select(client => SendAsync(client.EndPoint, adminMessage));

            _ = Task.WhenAll(tasks);
        }
    }

    public List<ConnectedClientInfo> GetConnectedClients()
    {
        return _connectedClients.Values
            .Select(c => new ConnectedClientInfo
            {
                PlayerId = c.Player.PlayerId,
                PlayerName = c.Player.PlayerName,
                TeamId = c.Player.TeamId,
                PlayerClass = c.Player.PlayerClass,
                WorldId = c.WorldId,
                EndPoint = c.EndPoint.ToString(),
                LastActivity = c.LastActivity,
                IsAlive = c.Player.IsAlive,
                Level = c.Player.Level,
                CurrentRoomId = c.Player.CurrentRoomId
            })
            .OrderBy(c => c.PlayerName)
            .ToList();
    }

// =============================================
// UTILITY METHODS
// =============================================

    private string? GetPlayerName(string playerId)
    {
        return _connectedClients.Values
            .FirstOrDefault(c => c.Player.PlayerId == playerId)
            ?.Player.PlayerName;
    }

    private bool IsSpamMessage(string message)
    {
        // Basic spam detection
        var lowerMessage = message.ToLower();

        // Check for excessive caps
        var capsCount = message.Count(char.IsUpper);
        if (capsCount > message.Length * 0.7 && message.Length > 10)
            return true;

        // Check for repeated characters
        var repeatedChars = 0;
        for (int i = 1; i < message.Length; i++)
        {
            if (message[i] == message[i - 1])
                repeatedChars++;
        }
        if (repeatedChars > message.Length * 0.5)
            return true;

        // Check for common spam words/patterns
        var spamPatterns = new[] { "buy", "sell", "cheap", "www.", "http", "discord.gg", "hack", "cheat" };
        if (spamPatterns.Any(pattern => lowerMessage.Contains(pattern)))
            return true;

        return false;
    }

private string SanitizeMessage(string message)
{
    return message
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("&", "&amp;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#x27;")
        .Trim();
}

// =============================================
// STATISTICS AND MONITORING
// =============================================

public NetworkStats GetNetworkStats()
{
    return new NetworkStats
    {
        ConnectedClients = _connectedClients.Count,
        PacketsSent = _packetsSent,
        PacketsReceived = _packetsReceived,
        IsRunning = _isRunning,
        Port = _settings.UdpPort
    };
}

public Dictionary<string, object> GetDetailedNetworkStats()
{
    var clientsByWorld = _connectedClients.Values
        .GroupBy(c => c.WorldId)
        .ToDictionary(g => g.Key, g => g.Count());

    var clientsByTeam = _connectedClients.Values
        .GroupBy(c => c.Player.TeamId)
        .ToDictionary(g => g.Key, g => g.Count());

    var clientsByClass = _connectedClients.Values
        .GroupBy(c => c.Player.PlayerClass)
        .ToDictionary(g => g.Key, g => g.Count());

    return new Dictionary<string, object>
    {
        ["TotalConnectedClients"] = _connectedClients.Count,
        ["PacketsSent"] = _packetsSent,
        ["PacketsReceived"] = _packetsReceived,
        ["PacketsPerSecond"] = CalculatePacketsPerSecond(),
        ["IsRunning"] = _isRunning,
        ["Port"] = _settings.UdpPort,
        ["PendingAcks"] = _pendingAcks.Count,
        ["ClientsByWorld"] = clientsByWorld,
        ["ClientsByTeam"] = clientsByTeam,
        ["ClientsByClass"] = clientsByClass,
        ["AverageLatency"] = CalculateAverageLatency(),
        ["ActiveWorlds"] = clientsByWorld.Keys.Count,
        ["MaxPlayersPerWorld"] = _settings.MaxPlayersPerWorld,
        ["WorldUpdateRate"] = _worldUpdateRate,
        ["PlayerUpdateRate"] = 30 // Reduced from _settings.TargetFPS
    };
}

private double CalculatePacketsPerSecond()
{
    // Simple calculation - in production you'd want a rolling window
    var uptimeSeconds = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime).TotalSeconds;
    return uptimeSeconds > 0 ? (_packetsSent + _packetsReceived) / uptimeSeconds : 0;
}

private double CalculateAverageLatency()
{
    // Placeholder - in production you'd track actual latency measurements
    return Random.Shared.NextDouble() * 50 + 10; // Simulated 10-60ms
}

// =============================================
// DISPOSE PATTERN
// =============================================

public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}

protected virtual void Dispose(bool disposing)
{
    if (disposing)
    {
        try
        {
            _logger.LogInformation("🔄 Disposing UDP Network Service...");

            _isRunning = false;

            // Desuscribirse del evento
            if (_gameEngine != null)
            {
                _gameEngine.OnGameStarted -= HandleGameStarted;
            }

            // Dispose timers
            try { _networkSendTimer?.Dispose(); } catch { }
            try { _clientTimeoutTimer?.Dispose(); } catch { }
            try { _reliabilityTimer?.Dispose(); } catch { }

            // ⭐ MEJORAR: Cerrar UDP server de forma segura
            try
            {
                if (_udpServer != null)
                {
                    _udpServer.Close();
                    _udpServer.Dispose();
                    _udpServer = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️  Error disposing UDP server: {Message}", ex.Message);
            }

            // Clear collections safely
            try
            {
                _connectedClients.Clear();
                _pendingAcks.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️  Error clearing collections: {Message}", ex.Message);
            }

            _logger.LogInformation("✅ UdpNetworkService disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during UdpNetworkService disposal");
        }
    }
}

~UdpNetworkService()
{
    Dispose(false);
}
}
