# Guía de Implementación del Cliente

## ⚠️ CAMBIOS CRÍTICOS EN EL PROTOCOLO DE RED

El servidor ahora implementa **Sequence Numbers** y **Input Buffering** para garantizar sincronización confiable.

---

## 📋 Cambios en Mensajes

### 1. PlayerInputMessage (Cliente → Servidor)

**ANTES**:
```json
{
  "type": "player_input",
  "playerId": "player_123",
  "data": {
    "moveInput": { "x": 0.5, "y": 0.0 },
    "isSprinting": false,
    "aimDirection": 45.0,
    "isAttacking": false,
    "abilityType": "",
    "abilityTarget": { "x": 0, "y": 0 }
  }
}
```

**AHORA** (⭐ CAMPOS NUEVOS REQUERIDOS):
```json
{
  "type": "player_input",
  "playerId": "player_123",
  "data": {
    "sequenceNumber": 42,           // ⭐ NUEVO: Número secuencial incremental
    "ackSequenceNumber": 18,        // ⭐ NUEVO: Último update del servidor recibido
    "clientTimestamp": 1234567.89,  // ⭐ NUEVO: Timestamp del cliente (en segundos)

    "moveInput": { "x": 0.5, "y": 0.0 },
    "isSprinting": false,
    "aimDirection": 45.0,
    "isAttacking": false,
    "abilityType": "",
    "abilityTarget": { "x": 0, "y": 0 }
  }
}
```

### 2. WorldUpdateMessage (Servidor → Cliente)

**ANTES**:
```json
{
  "players": [...],
  "combatEvents": [...],
  "lootUpdates": [...],
  "mobUpdates": [...],
  "frameNumber": 3600
}
```

**AHORA** (⭐ CAMPOS NUEVOS):
```json
{
  "acknowledgedInputs": {           // ⭐ NUEVO: Últimos inputs procesados por jugador
    "player_123": 42,
    "player_456": 39
  },
  "serverTime": 1234567.89,         // ⭐ NUEVO: Timestamp del servidor (Unix epoch)
  "frameNumber": 3600,

  "players": [...],
  "combatEvents": [...],
  "lootUpdates": [...],
  "mobUpdates": [...]
}
```

---

## 🎮 Implementación en Cliente (Unity Ejemplo)

### 1. Tracking de Sequence Numbers

```csharp
public class NetworkClient : MonoBehaviour
{
    // Sequence tracking
    private uint _currentInputSequence = 0;
    private uint _lastServerUpdate = 0;

    // Client timestamp (Time.time en Unity)
    private float ClientTime => Time.time;

    void SendInput(Vector2 movement, bool sprint, bool attack)
    {
        _currentInputSequence++; // Incrementar SIEMPRE antes de enviar

        var inputMessage = new
        {
            type = "player_input",
            playerId = _playerId,
            data = new
            {
                // ⭐ CRÍTICO: Campos de sincronización
                sequenceNumber = _currentInputSequence,
                ackSequenceNumber = _lastServerUpdate,
                clientTimestamp = ClientTime,

                // Input data
                moveInput = new { x = movement.x, y = movement.y },
                isSprinting = sprint,
                aimDirection = _aimAngle,
                isAttacking = attack,
                abilityType = _currentAbility ?? "",
                abilityTarget = new { x = _abilityTarget.x, y = _abilityTarget.y }
            }
        };

        SendToServer(JsonUtility.ToJson(inputMessage));
    }
}
```

### 2. Client Prediction (Opcional pero Recomendado)

```csharp
public class PredictedPlayer : MonoBehaviour
{
    // Prediction state
    private struct PredictedInput
    {
        public uint sequenceNumber;
        public Vector2 movement;
        public Vector2 predictedPosition;
        public float timestamp;
    }

    private Queue<PredictedInput> _pendingInputs = new Queue<PredictedInput>();
    private Vector2 _lastConfirmedPosition;

    void Update()
    {
        // 1. Leer input del jugador
        var movement = new Vector2(
            Input.GetAxis("Horizontal"),
            Input.GetAxis("Vertical")
        );

        if (movement.magnitude > 0.01f)
        {
            // 2. Aplicar INMEDIATAMENTE (predicción)
            var predictedPos = transform.position + (Vector3)movement * _moveSpeed * Time.deltaTime;
            transform.position = predictedPos;

            // 3. Guardar para reconciliación futura
            _currentInputSequence++;
            _pendingInputs.Enqueue(new PredictedInput
            {
                sequenceNumber = _currentInputSequence,
                movement = movement,
                predictedPosition = predictedPos,
                timestamp = Time.time
            });

            // 4. Enviar al servidor
            SendInput(movement, Input.GetKey(KeyCode.LeftShift), Input.GetMouseButton(0));

            // 5. Limitar cola (prevenir memory leak)
            while (_pendingInputs.Count > 100)
            {
                _pendingInputs.Dequeue();
            }
        }
    }

    void OnServerUpdate(WorldUpdateMessage update)
    {
        // Actualizar último update recibido
        _lastServerUpdate = update.frameNumber;

        // Buscar nuestro player en el update
        var myState = update.players.Find(p => p.playerId == _playerId);
        if (myState == null) return;

        // Obtener sequence acknowledgment
        if (update.acknowledgedInputs.TryGetValue(_playerId, out uint ackedSeq))
        {
            // ⭐ RECONCILIACIÓN: Corregir predicción si es necesario
            ReconcilePosition(myState.position, ackedSeq);
        }
    }

    void ReconcilePosition(Vector2 serverPosition, uint acknowledgedSequence)
    {
        // 1. Confirmar posición del servidor
        _lastConfirmedPosition = serverPosition;

        // 2. Eliminar inputs ya procesados
        while (_pendingInputs.Count > 0 && _pendingInputs.Peek().sequenceNumber <= acknowledgedSequence)
        {
            _pendingInputs.Dequeue();
        }

        // 3. Re-simular inputs pendientes sobre la posición confirmada
        var reconciledPosition = serverPosition;
        foreach (var input in _pendingInputs)
        {
            var deltaTime = Time.time - input.timestamp;
            reconciledPosition += input.movement * _moveSpeed * deltaTime;
        }

        // 4. Solo corregir si hay diferencia significativa (evitar jitter)
        var positionError = Vector2.Distance(transform.position, reconciledPosition);
        if (positionError > 0.5f)
        {
            Debug.LogWarning($"Position misprediction: {positionError:F2} units, correcting");

            // Corrección suave (lerp) vs snap
            if (positionError < 2.0f)
            {
                transform.position = Vector2.Lerp(transform.position, reconciledPosition, 0.5f);
            }
            else
            {
                // Gran diferencia = snap inmediato
                transform.position = reconciledPosition;
            }
        }
    }
}
```

### 3. Interpolación para Otros Jugadores

```csharp
public class InterpolatedPlayer : MonoBehaviour
{
    // State buffering para interpolación
    private struct StateSnapshot
    {
        public Vector2 position;
        public float serverTime;
    }

    private Queue<StateSnapshot> _stateBuffer = new Queue<StateSnapshot>();

    // Delay de interpolación (100ms buffer)
    private const float INTERPOLATION_DELAY = 0.1f;

    void OnServerUpdate(PlayerStateUpdate state, float serverTime)
    {
        // No interpolar nuestro propio jugador (usar prediction)
        if (state.playerId == _localPlayerId) return;

        // Buffer state para interpolación
        _stateBuffer.Enqueue(new StateSnapshot
        {
            position = state.position,
            serverTime = serverTime
        });

        // Mantener solo últimos 500ms
        while (_stateBuffer.Count > 0 &&
               Time.time - _stateBuffer.Peek().serverTime > 0.5f)
        {
            _stateBuffer.Dequeue();
        }
    }

    void Update()
    {
        if (_stateBuffer.Count < 2) return;

        // Renderizar en el "pasado" (100ms atrás)
        var renderTime = Time.time - INTERPOLATION_DELAY;

        // Encontrar 2 snapshots para interpolar
        StateSnapshot? from = null, to = null;

        foreach (var snapshot in _stateBuffer)
        {
            if (snapshot.serverTime <= renderTime)
            {
                from = snapshot;
            }
            else
            {
                to = snapshot;
                break;
            }
        }

        // Interpolar entre from y to
        if (from.HasValue && to.HasValue)
        {
            var t = (renderTime - from.Value.serverTime) /
                    (to.Value.serverTime - from.Value.serverTime);

            transform.position = Vector2.Lerp(
                from.Value.position,
                to.Value.position,
                Mathf.Clamp01(t)
            );
        }
    }
}
```

---

## ⚠️ Problemas Comunes y Soluciones

### Problema 1: "Inputs se ignoran"

**Causa**: SequenceNumber duplicado o fuera de orden.

**Solución**:
```csharp
// ✅ CORRECTO: Incrementar ANTES de enviar
_currentInputSequence++;
SendInput(_currentInputSequence, ...);

// ❌ INCORRECTO: Usar mismo sequence
SendInput(_currentInputSequence, ...);
SendInput(_currentInputSequence, ...); // Duplicado!
```

### Problema 2: "Jugador 'teleportea' constantemente"

**Causa**: No implementaste client prediction, o reconciliación muy agresiva.

**Solución**:
```csharp
// Usar threshold para evitar correcciones pequeñas
if (positionError > 0.5f) // Solo corregir si >0.5 unidades
{
    transform.position = reconciledPosition;
}
```

### Problema 3: "Otros jugadores se ven con lag"

**Causa**: No implementaste interpolación.

**Solución**: Implementa state buffering + interpolación (ver código arriba).

---

## 🔍 Debugging

### Ver Estadísticas del InputBuffer

El servidor ahora expone métricas de sincronización. Puedes consultarlas vía endpoint (si implementas):

```csharp
GET /api/debug/input-stats?playerId=player_123

Response:
{
  "totalInputs": 1000,
  "inOrderInputs": 980,
  "outOfOrderInputs": 15,
  "duplicateInputs": 5,
  "totalPacketsLost": 8,
  "packetLossRate": 0.008,  // 0.8% packet loss
  "inOrderRate": 0.98       // 98% in order
}
```

### Logs del Servidor

El servidor ahora logea problemas de sincronización:

```
[WARN] Input gap detected for player_123: expected 42, got 45 (gap: 3)
[WARN] Input timeout for player_123, skipping gap
[DEBUG] Duplicate/old input seq 40 from player_123, last processed 42
```

Monitorea estos logs para detectar problemas de red.

---

## 🚀 Checklist de Implementación

### Cliente Unity (Obligatorio)

- [ ] Agregar `sequenceNumber` a todos los inputs (incremental)
- [ ] Agregar `ackSequenceNumber` a todos los inputs
- [ ] Agregar `clientTimestamp` a todos los inputs
- [ ] Parsear `acknowledgedInputs` del WorldUpdateMessage
- [ ] Parsear `serverTime` del WorldUpdateMessage

### Cliente Unity (Recomendado)

- [ ] Implementar Client Prediction para jugador local
- [ ] Implementar Reconciliation cuando se recibe update
- [ ] Implementar Interpolation para otros jugadores
- [ ] Agregar métricas de sincronización (UI debug)

### Testing

- [ ] Probar con latency artificial (50-200ms)
- [ ] Probar con packet loss (1-5%)
- [ ] Verificar que no hay "teleports"
- [ ] Verificar que movimiento se siente responsive
- [ ] Verificar que otros jugadores se ven suaves

---

## 📚 Referencias

- [Gabriel Gambetta - Fast-Paced Multiplayer](https://www.gabrielgambetta.com/client-server-game-architecture.html)
- [Valve - Source Multiplayer Networking](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)
- [Gaffer On Games - Networking for Physics](https://gafferongames.com/post/introduction_to_networked_physics/)

---

## 🆘 Soporte

Si encuentras problemas:

1. Verifica logs del servidor (`/logs/mazewars-.log`)
2. Revisa métricas de InputBuffer
3. Confirma que sequence numbers están incrementando
4. Usa Wireshark para inspeccionar paquetes UDP

**NOTA**: Estos cambios son **backward incompatible**. Clientes antiguos que no envíen `sequenceNumber` tendrán inputs ignorados.

---

**Última actualización**: 2025-11-19
**Versión del servidor**: 1.1.0 (con Sequence Numbers)
