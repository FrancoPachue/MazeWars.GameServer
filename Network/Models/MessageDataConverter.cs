using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Network.Models;

/// <summary>
/// Shared utility for converting MessagePack-deserialized object arrays to strongly-typed message objects.
/// MessagePack deserializes NetworkMessage.Data as object[] when using array format.
/// </summary>
public static class MessageDataConverter
{
    public static T? Convert<T>(object data) where T : class
    {
        if (data == null) return null;
        if (data is not object[] array) return null;
        return MapArrayToType<T>(array);
    }

    private static T? MapArrayToType<T>(object[] array) where T : class
    {
        var type = typeof(T);

        if (type == typeof(ClientConnectData) && array.Length >= 4)
        {
            return new ClientConnectData
            {
                PlayerName = array[0]?.ToString() ?? string.Empty,
                PlayerClass = array[1]?.ToString() ?? "scout",
                TeamId = array[2]?.ToString() ?? string.Empty,
                AuthToken = array[3]?.ToString() ?? string.Empty
            } as T;
        }

        if (type == typeof(ReconnectRequestData) && array.Length >= 3)
        {
            return new ReconnectRequestData
            {
                SessionToken = array[0]?.ToString() ?? string.Empty,
                PlayerName = array[1]?.ToString() ?? string.Empty,
                ClientTimestamp = System.Convert.ToSingle(array[2])
            } as T;
        }

        if (type == typeof(PlayerInputMessage) && array.Length >= 9)
        {
            var inputMsg = new PlayerInputMessage
            {
                SequenceNumber = System.Convert.ToUInt32(array[0]),
                AckSequenceNumber = System.Convert.ToUInt32(array[1]),
                ClientTimestamp = System.Convert.ToSingle(array[2]),
                MoveInput = ParseVector2(array[3]),
                IsSprinting = System.Convert.ToBoolean(array[4]),
                AimDirection = System.Convert.ToSingle(array[5]),
                IsAttacking = System.Convert.ToBoolean(array[6]),
                AbilityType = array[7]?.ToString() ?? string.Empty,
                AbilityTarget = ParseVector2(array[8])
            };

            // Click-to-move fields (optional)
            if (array.Length > 9) inputMsg.MoveTarget = ParseVector2(array[9]);
            if (array.Length > 10) inputMsg.HasMoveTarget = System.Convert.ToBoolean(array[10]);
            if (array.Length > 11) inputMsg.StopMovement = System.Convert.ToBoolean(array[11]);
            if (array.Length > 12) inputMsg.TargetEntityId = array[12]?.ToString() ?? string.Empty;

            return inputMsg as T;
        }

        if (type == typeof(LootGrabMessage) && array.Length >= 1)
        {
            return new LootGrabMessage
            {
                LootId = array[0]?.ToString() ?? string.Empty
            } as T;
        }

        if (type == typeof(ChatMessage) && array.Length >= 2)
        {
            return new ChatMessage
            {
                Message = array[0]?.ToString() ?? string.Empty,
                ChatType = array[1]?.ToString() ?? "team"
            } as T;
        }

        if (type == typeof(PingMarkerMessage) && array.Length >= 2)
        {
            return new PingMarkerMessage
            {
                X = System.Convert.ToSingle(array[0]),
                Y = System.Convert.ToSingle(array[1])
            } as T;
        }

        if (type == typeof(UseItemMessage) && array.Length >= 3)
        {
            return new UseItemMessage
            {
                ItemId = array[0]?.ToString() ?? string.Empty,
                ItemType = array[1]?.ToString() ?? string.Empty,
                TargetPosition = ParseVector2(array[2])
            } as T;
        }

        if (type == typeof(ExtractionMessage) && array.Length >= 2)
        {
            return new ExtractionMessage
            {
                Action = array[0]?.ToString() ?? string.Empty,
                ExtractionId = array[1]?.ToString() ?? string.Empty
            } as T;
        }

        if (type == typeof(MessageAcknowledgement) && array.Length >= 3)
        {
            return new MessageAcknowledgement
            {
                MessageId = array[0]?.ToString() ?? string.Empty,
                Success = System.Convert.ToBoolean(array[1]),
                ErrorMessage = array[2]?.ToString() ?? string.Empty
            } as T;
        }

        if (type == typeof(ServerPingData) && array.Length >= 2)
        {
            return new ServerPingData
            {
                PingId = System.Convert.ToUInt32(array[0]),
                ServerTime = System.Convert.ToSingle(array[1])
            } as T;
        }

        if (type == typeof(EquipItemMessage) && array.Length >= 3)
        {
            return new EquipItemMessage
            {
                ItemId = array[0]?.ToString() ?? string.Empty,
                Unequip = System.Convert.ToBoolean(array[1]),
                SlotIndex = System.Convert.ToInt32(array[2])
            } as T;
        }

        if (type == typeof(TradeRequestMessage) && array.Length >= 3)
        {
            return new TradeRequestMessage
            {
                TargetPlayerId = array[0]?.ToString() ?? string.Empty,
                OfferedItemIds = array[1] is object[] offered
                    ? offered.Select(o => o?.ToString() ?? string.Empty).ToList()
                    : new List<string>(),
                RequestedItemIds = array[2] is object[] requested
                    ? requested.Select(r => r?.ToString() ?? string.Empty).ToList()
                    : new List<string>()
            } as T;
        }

        if (type == typeof(ContainerGrabMessage) && array.Length >= 2)
        {
            return new ContainerGrabMessage
            {
                ContainerId = array[0]?.ToString() ?? string.Empty,
                LootId = array[1]?.ToString() ?? string.Empty
            } as T;
        }

        if (type == typeof(DoorInteractMessage) && array.Length >= 1)
        {
            return new DoorInteractMessage
            {
                DoorId = array[0]?.ToString() ?? string.Empty
            } as T;
        }

        return null;
    }

    private static Vector2 ParseVector2(object data)
    {
        if (data is object[] arr && arr.Length >= 2)
        {
            return new Vector2
            {
                X = System.Convert.ToSingle(arr[0]),
                Y = System.Convert.ToSingle(arr[1])
            };
        }
        return new Vector2();
    }
}
