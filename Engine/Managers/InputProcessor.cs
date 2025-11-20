using MazeWars.GameServer.Engine.Network;
using MazeWars.GameServer.Models;
using MazeWars.GameServer.Network.Models;
using MessagePack;
using System.Collections.Concurrent;

namespace MazeWars.GameServer.Engine.Managers;

/// <summary>
/// ⭐ REFACTORED: Manages input queue, UDP packet reordering, and input dispatching.
/// Extracted from GameEngine to reduce complexity and improve maintainability.
/// </summary>
public class InputProcessor
{
    private readonly ILogger<InputProcessor> _logger;
    private readonly ConcurrentQueue<NetworkMessage> _inputQueue = new();
    private readonly InputBuffer _inputBuffer;

    /// <summary>
    /// Event triggered when a player input needs processing.
    /// Handler signature: (player, inputData) => void
    /// </summary>
    public event Action<RealTimePlayer, PlayerInputMessage>? OnPlayerInput;

    /// <summary>
    /// Event triggered when a loot grab input is received.
    /// Handler signature: (player, lootGrabData) => void
    /// </summary>
    public event Action<RealTimePlayer, LootGrabMessage>? OnLootGrab;

    /// <summary>
    /// Event triggered when a chat input is received.
    /// Handler signature: (player, chatData) => void
    /// </summary>
    public event Action<RealTimePlayer, ChatMessage>? OnChat;

    /// <summary>
    /// Event triggered when a use item input is received.
    /// Handler signature: (player, useItemData) => void
    /// </summary>
    public event Action<RealTimePlayer, UseItemMessage>? OnUseItem;

    /// <summary>
    /// Event triggered when an extraction input is received.
    /// Handler signature: (player, extractionData) => void
    /// </summary>
    public event Action<RealTimePlayer, ExtractionMessage>? OnExtraction;

    /// <summary>
    /// Event triggered when a trade request input is received.
    /// Handler signature: (player, tradeRequestData) => void
    /// </summary>
    public event Action<RealTimePlayer, TradeRequestMessage>? OnTradeRequest;

    /// <summary>
    /// Delegate for finding players by ID.
    /// Set by GameEngine to allow input processor to resolve player references.
    /// </summary>
    public Func<string, RealTimePlayer?>? PlayerLookup { get; set; }

    public InputProcessor(ILogger<InputProcessor> logger)
    {
        _logger = logger;
        _inputBuffer = new InputBuffer(logger);

        _logger.LogInformation("InputProcessor initialized with InputBuffer for UDP packet reordering");
    }

    // =============================================
    // MESSAGE DATA DESERIALIZATION HELPER
    // =============================================

    /// <summary>
    /// Converts message.Data (deserialized as generic object) to specific type T.
    /// MessagePack deserializes Data as object array, so we need to manually map to the target type.
    /// </summary>
    private T? ConvertMessageData<T>(object data) where T : class
    {
        try
        {
            // MessagePack deserializes Data as Object[] for MessagePackObject types with [Key] attributes
            if (data is not object[] array)
            {
                _logger.LogWarning("Expected Object[] but got {Type}", data?.GetType().Name ?? "null");
                return null;
            }

            var targetType = typeof(T);

            // Manual mapping from array indices to properties based on [Key(n)] attributes
            if (targetType == typeof(PlayerInputMessage))
            {
                return new PlayerInputMessage
                {
                    SequenceNumber = array.Length > 0 && array[0] is uint u0 ? u0 : Convert.ToUInt32(array[0]),
                    AckSequenceNumber = array.Length > 1 && array[1] is uint u1 ? u1 : Convert.ToUInt32(array[1]),
                    ClientTimestamp = array.Length > 2 && array[2] is float f2 ? f2 : Convert.ToSingle(array[2]),
                    MoveInput = array.Length > 3 ? ParseVector2(array[3]) : new Vector2(),
                    IsSprinting = array.Length > 4 && array[4] is bool b4 && b4,
                    AimDirection = array.Length > 5 && array[5] is float f5 ? f5 : Convert.ToSingle(array[5]),
                    IsAttacking = array.Length > 6 && array[6] is bool b6 && b6,
                    AbilityType = array.Length > 7 ? array[7]?.ToString() ?? string.Empty : string.Empty,
                    AbilityTarget = array.Length > 8 ? ParseVector2(array[8]) : new Vector2()
                } as T;
            }
            else if (targetType == typeof(LootGrabMessage))
            {
                return new LootGrabMessage
                {
                    LootId = array.Length > 0 ? array[0]?.ToString() ?? string.Empty : string.Empty
                } as T;
            }
            else if (targetType == typeof(ChatMessage))
            {
                return new ChatMessage
                {
                    Message = array.Length > 0 ? array[0]?.ToString() ?? string.Empty : string.Empty,
                    ChatType = array.Length > 1 ? array[1]?.ToString() ?? "team" : "team"
                } as T;
            }
            else if (targetType == typeof(UseItemMessage))
            {
                return new UseItemMessage
                {
                    ItemId = array.Length > 0 ? array[0]?.ToString() ?? string.Empty : string.Empty,
                    ItemType = array.Length > 1 ? array[1]?.ToString() ?? string.Empty : string.Empty,
                    TargetPosition = array.Length > 2 ? ParseVector2(array[2]) : new Vector2()
                } as T;
            }
            else if (targetType == typeof(ExtractionMessage))
            {
                return new ExtractionMessage
                {
                    Action = array.Length > 0 ? array[0]?.ToString() ?? string.Empty : string.Empty,
                    ExtractionId = array.Length > 1 ? array[1]?.ToString() ?? string.Empty : string.Empty
                } as T;
            }
            else if (targetType == typeof(TradeRequestMessage))
            {
                return new TradeRequestMessage
                {
                    TargetPlayerId = array.Length > 0 ? array[0]?.ToString() ?? string.Empty : string.Empty,
                    OfferedItemIds = array.Length > 1 && array[1] is object[] offered
                        ? offered.Select(o => o?.ToString() ?? string.Empty).ToList()
                        : new List<string>(),
                    RequestedItemIds = array.Length > 2 && array[2] is object[] requested
                        ? requested.Select(r => r?.ToString() ?? string.Empty).ToList()
                        : new List<string>()
                } as T;
            }

            _logger.LogWarning("No manual mapping defined for type {Type}", targetType.Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert message data to type {Type}. Data type: {DataType}",
                typeof(T).Name, data?.GetType().Name ?? "null");
            return null;
        }
    }

    /// <summary>
    /// Helper method to parse Vector2 from object array [x, y]
    /// </summary>
    private Vector2 ParseVector2(object data)
    {
        if (data is object[] arr && arr.Length >= 2)
        {
            float x = arr[0] is float fx ? fx : Convert.ToSingle(arr[0]);
            float y = arr[1] is float fy ? fy : Convert.ToSingle(arr[1]);
            return new Vector2 { X = x, Y = y };
        }
        return new Vector2();
    }

    // =============================================
    // INPUT QUEUE MANAGEMENT
    // =============================================

    /// <summary>
    /// Queue an input for processing.
    /// Called by NetworkService when input is received.
    /// </summary>
    public void QueueInput(NetworkMessage input)
    {
        _inputQueue.Enqueue(input);
    }

    /// <summary>
    /// Get the current size of the input queue.
    /// </summary>
    public int GetQueueSize()
    {
        return _inputQueue.Count;
    }

    /// <summary>
    /// Check if the input queue is empty.
    /// </summary>
    public bool IsQueueEmpty()
    {
        return _inputQueue.IsEmpty;
    }

    // =============================================
    // INPUT PROCESSING
    // =============================================

    /// <summary>
    /// Process queued inputs. Should be called from game loop.
    /// </summary>
    public void ProcessInputQueue()
    {
        var processedCount = 0;
        var maxProcessPerFrame = 1000;

        while (_inputQueue.TryDequeue(out var input) && processedCount < maxProcessPerFrame)
        {
            try
            {
                ProcessInput(input);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing input {InputType} from player {PlayerId}",
                    input.Type, input.PlayerId);
            }
        }

        if (processedCount == maxProcessPerFrame && !_inputQueue.IsEmpty)
        {
            _logger.LogWarning("Input queue processing limit reached, {QueueSize} inputs remaining",
                _inputQueue.Count);
        }
    }

    /// <summary>
    /// Process a single input message.
    /// </summary>
    private void ProcessInput(NetworkMessage input)
    {
        // Resolve player
        if (PlayerLookup == null)
        {
            _logger.LogError("PlayerLookup not set, cannot process input");
            return;
        }

        var player = PlayerLookup(input.PlayerId);
        if (player == null)
        {
            _logger.LogDebug("Received input for unknown player {PlayerId}", input.PlayerId);
            return;
        }

        // Dispatch to appropriate handler based on input type
        switch (input.Type.ToLower())
        {
            case "player_input":
                HandlePlayerInput(player, input);
                break;

            case "loot_grab":
                HandleLootGrab(player, input);
                break;

            case "chat":
                HandleChat(player, input);
                break;

            case "use_item":
                HandleUseItem(player, input);
                break;

            case "extraction":
                HandleExtraction(player, input);
                break;

            case "trade_request":
                HandleTradeRequest(player, input);
                break;

            default:
                _logger.LogWarning("Unknown input type '{Type}' from player {PlayerId}",
                    input.Type, input.PlayerId);
                break;
        }
    }

    // =============================================
    // INPUT TYPE HANDLERS
    // =============================================

    /// <summary>
    /// Handle player movement/action input with UDP packet reordering.
    /// </summary>
    private void HandlePlayerInput(RealTimePlayer player, NetworkMessage input)
    {
        // Convert message.Data to PlayerInputMessage
        var playerInput = ConvertMessageData<PlayerInputMessage>(input.Data);
        if (playerInput == null)
        {
            _logger.LogWarning("Invalid player input data from {PlayerId}", player.PlayerId);
            return;
        }

        // ⭐ SYNC: Use InputBuffer to handle UDP packet reordering
        var orderedInputs = _inputBuffer.ProcessInput(player.PlayerId, playerInput);

        // Process inputs in correct order and trigger event for each
        foreach (var orderedInput in orderedInputs)
        {
            OnPlayerInput?.Invoke(player, orderedInput);
        }
    }

    /// <summary>
    /// Handle loot grab input.
    /// </summary>
    private void HandleLootGrab(RealTimePlayer player, NetworkMessage input)
    {
        // Convert message.Data to LootGrabMessage
        var lootGrab = ConvertMessageData<LootGrabMessage>(input.Data);
        if (lootGrab == null)
        {
            _logger.LogWarning("Invalid loot grab data from {PlayerId}", player.PlayerId);
            return;
        }

        OnLootGrab?.Invoke(player, lootGrab);
    }

    /// <summary>
    /// Handle chat input.
    /// </summary>
    private void HandleChat(RealTimePlayer player, NetworkMessage input)
    {
        // Convert message.Data to ChatMessage
        var chat = ConvertMessageData<ChatMessage>(input.Data);
        if (chat == null)
        {
            _logger.LogWarning("Invalid chat data from {PlayerId}", player.PlayerId);
            return;
        }

        OnChat?.Invoke(player, chat);
    }

    /// <summary>
    /// Handle use item input.
    /// </summary>
    private void HandleUseItem(RealTimePlayer player, NetworkMessage input)
    {
        // Convert message.Data to UseItemMessage
        var useItem = ConvertMessageData<UseItemMessage>(input.Data);
        if (useItem == null)
        {
            _logger.LogWarning("Invalid use item data from {PlayerId}", player.PlayerId);
            return;
        }

        OnUseItem?.Invoke(player, useItem);
    }

    /// <summary>
    /// Handle extraction input.
    /// </summary>
    private void HandleExtraction(RealTimePlayer player, NetworkMessage input)
    {
        // Convert message.Data to ExtractionMessage
        var extraction = ConvertMessageData<ExtractionMessage>(input.Data);
        if (extraction == null)
        {
            _logger.LogWarning("Invalid extraction data from {PlayerId}", player.PlayerId);
            return;
        }

        OnExtraction?.Invoke(player, extraction);
    }

    /// <summary>
    /// Handle trade request input.
    /// </summary>
    private void HandleTradeRequest(RealTimePlayer player, NetworkMessage input)
    {
        // Convert message.Data to TradeRequestMessage
        var tradeRequest = ConvertMessageData<TradeRequestMessage>(input.Data);
        if (tradeRequest == null)
        {
            _logger.LogWarning("Invalid trade request data from {PlayerId}", player.PlayerId);
            return;
        }

        OnTradeRequest?.Invoke(player, tradeRequest);
    }

    // =============================================
    // INPUT BUFFER SYNCHRONIZATION
    // =============================================

    /// <summary>
    /// Get the last acknowledged sequence number for a player.
    /// Used for client-side prediction reconciliation.
    /// </summary>
    public uint GetLastAcknowledgedSequence(string playerId)
    {
        return _inputBuffer.GetLastAcknowledgedSequence(playerId);
    }

    /// <summary>
    /// Clear input buffer for a disconnected player.
    /// </summary>
    public void ClearPlayerInputBuffer(string playerId)
    {
        _inputBuffer.CleanupPlayer(playerId);
        _logger.LogDebug("Cleared input buffer for player {PlayerId}", playerId);
    }

    // =============================================
    // STATISTICS AND DIAGNOSTICS
    // =============================================

    /// <summary>
    /// Get input processing statistics.
    /// </summary>
    public Dictionary<string, object> GetInputStats()
    {
        return new Dictionary<string, object>
        {
            ["QueueSize"] = _inputQueue.Count,
            ["IsQueueEmpty"] = _inputQueue.IsEmpty,
            ["InputBufferPlayerCount"] = _inputBuffer.GetAllStats().Count
        };
    }

    /// <summary>
    /// Get detailed input buffer statistics for a specific player.
    /// </summary>
    public Dictionary<string, object>? GetPlayerInputStats(string playerId)
    {
        var lastSeq = _inputBuffer.GetLastAcknowledgedSequence(playerId);

        if (lastSeq == 0)
            return null; // Player not tracked

        return new Dictionary<string, object>
        {
            ["PlayerId"] = playerId,
            ["LastAcknowledgedSequence"] = lastSeq
        };
    }

    // =============================================
    // DISPOSAL
    // =============================================

    public void Dispose()
    {
        _inputQueue.Clear();
        _logger.LogInformation("InputProcessor disposed");
    }
}
