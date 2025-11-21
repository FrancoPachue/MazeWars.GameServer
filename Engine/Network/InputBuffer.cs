using MazeWars.GameServer.Network.Models;
using Microsoft.Extensions.Logging;

namespace MazeWars.GameServer.Engine.Network;

/// <summary>
/// Manages input buffering and ordering to handle UDP packet reordering and loss.
/// This is CRITICAL for network synchronization.
/// </summary>
public class InputBuffer
{
    private class BufferedInput
    {
        public uint SequenceNumber { get; set; }
        public PlayerInputMessage Input { get; set; } = null!;
        public DateTime ReceivedAt { get; set; }
    }

    private readonly Dictionary<string, uint> _lastProcessedSequence = new();
    private readonly Dictionary<string, SortedDictionary<uint, BufferedInput>> _inputBuffers = new();
    private readonly ILogger _logger;

    // Configuration
    private const int MAX_BUFFER_SIZE = 100; // Max inputs to buffer per player
    private const int INPUT_TIMEOUT_MS = 100; // Time to wait for missing inputs

    // Statistics
    private readonly Dictionary<string, InputStats> _stats = new();

    public InputBuffer(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Process an incoming input, handling ordering and packet loss.
    /// Returns list of inputs ready to be processed (may be empty if buffering).
    /// </summary>
    public List<PlayerInputMessage> ProcessInput(string playerId, PlayerInputMessage input)
    {
        var result = new List<PlayerInputMessage>();

        // Initialize tracking for new player
        if (!_lastProcessedSequence.ContainsKey(playerId))
        {
            _lastProcessedSequence[playerId] = 0;
            _stats[playerId] = new InputStats();
        }

        var lastSeq = _lastProcessedSequence[playerId];
        var stats = _stats[playerId];

        // ⭐ CRITICAL: Ignore duplicate or old inputs
        if (input.SequenceNumber <= lastSeq)
        {
            stats.DuplicateInputs++;

            _logger.LogDebug("Duplicate/old input seq {Seq} from {PlayerId}, last processed {LastSeq}",
                input.SequenceNumber, playerId, lastSeq);

            return result; // Empty list
        }

        stats.TotalInputs++;

        // ⭐ PERFECT: Input is the next expected sequence
        if (input.SequenceNumber == lastSeq + 1)
        {
            result.Add(input);
            _lastProcessedSequence[playerId] = input.SequenceNumber;
            stats.InOrderInputs++;

            _logger.LogDebug("✅ ACK: Input seq {Seq} processed for {PlayerId}", input.SequenceNumber, playerId);

            // ⭐ Process any buffered inputs that are now in order
            result.AddRange(ProcessBufferedInputs(playerId));

            return result;
        }

        // ⭐ GAP DETECTED: Input arrived out of order
        var gap = input.SequenceNumber - lastSeq - 1;
        stats.OutOfOrderInputs++;
        stats.TotalPacketsLost += (int)gap;

        _logger.LogWarning("Input gap detected for {PlayerId}: expected {Expected}, got {Got} (gap: {Gap})",
            playerId, lastSeq + 1, input.SequenceNumber, gap);

        // Buffer this input until gap is filled or timeout
        BufferInput(playerId, input);

        // Check if we should give up waiting for missing packets
        CheckTimeouts(playerId);

        return result; // May contain inputs if timeout triggered
    }

    /// <summary>
    /// Buffer an out-of-order input for later processing
    /// </summary>
    private void BufferInput(string playerId, PlayerInputMessage input)
    {
        if (!_inputBuffers.ContainsKey(playerId))
        {
            _inputBuffers[playerId] = new SortedDictionary<uint, BufferedInput>();
        }

        var buffer = _inputBuffers[playerId];

        // Prevent buffer overflow (DoS protection)
        if (buffer.Count >= MAX_BUFFER_SIZE)
        {
            _logger.LogWarning("Input buffer overflow for {PlayerId}, forcing processing",
                playerId);

            // Force process oldest inputs to make room
            ForceProcessBuffer(playerId);
        }

        buffer[input.SequenceNumber] = new BufferedInput
        {
            SequenceNumber = input.SequenceNumber,
            Input = input,
            ReceivedAt = DateTime.UtcNow
        };

        _logger.LogDebug("Buffered input seq {Seq} for {PlayerId} (buffer size: {Size})",
            input.SequenceNumber, playerId, buffer.Count);
    }

    /// <summary>
    /// Process buffered inputs that are now in correct order
    /// </summary>
    private List<PlayerInputMessage> ProcessBufferedInputs(string playerId)
    {
        var result = new List<PlayerInputMessage>();

        if (!_inputBuffers.TryGetValue(playerId, out var buffer) || buffer.Count == 0)
        {
            return result;
        }

        var lastSeq = _lastProcessedSequence[playerId];

        // Process consecutive inputs from buffer
        while (buffer.ContainsKey(lastSeq + 1))
        {
            var buffered = buffer[lastSeq + 1];
            result.Add(buffered.Input);
            buffer.Remove(lastSeq + 1);
            lastSeq++;

            _logger.LogDebug("Processed buffered input seq {Seq} for {PlayerId}",
                lastSeq, playerId);
        }

        _lastProcessedSequence[playerId] = lastSeq;

        return result;
    }

    /// <summary>
    /// Check for inputs that have been buffered too long and force process
    /// </summary>
    private void CheckTimeouts(string playerId)
    {
        if (!_inputBuffers.TryGetValue(playerId, out var buffer) || buffer.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(INPUT_TIMEOUT_MS);

        // Check oldest buffered input
        var oldest = buffer.Values.FirstOrDefault();
        if (oldest == null)
        {
            return;
        }

        var age = now - oldest.ReceivedAt;

        if (age > timeout)
        {
            var lastSeq = _lastProcessedSequence[playerId];

            _logger.LogWarning("Input timeout for {PlayerId}: oldest buffered seq {OldestSeq} aged {Age}ms, " +
                "last processed {LastSeq}. Skipping gap and processing buffered inputs.",
                playerId, oldest.SequenceNumber, age.TotalMilliseconds, lastSeq);

            // Skip the gap by advancing to just before the oldest buffered input
            _lastProcessedSequence[playerId] = oldest.SequenceNumber - 1;

            // Now process all buffered inputs
            ProcessBufferedInputs(playerId);
        }
    }

    /// <summary>
    /// Force process buffer when it's getting too full (DoS protection)
    /// </summary>
    private void ForceProcessBuffer(string playerId)
    {
        if (!_inputBuffers.TryGetValue(playerId, out var buffer) || buffer.Count == 0)
        {
            return;
        }

        var oldest = buffer.Values.FirstOrDefault();
        if (oldest == null)
        {
            return;
        }

        _logger.LogWarning("Force processing buffer for {PlayerId}, jumping to seq {Seq}",
            playerId, oldest.SequenceNumber - 1);

        // Skip to oldest buffered input
        _lastProcessedSequence[playerId] = oldest.SequenceNumber - 1;

        // Process all buffered inputs
        ProcessBufferedInputs(playerId);
    }

    /// <summary>
    /// Get the last acknowledged sequence number for a player (for sending to client)
    /// </summary>
    public uint GetLastAcknowledgedSequence(string playerId)
    {
        if (_lastProcessedSequence.TryGetValue(playerId, out var sequence))
            return sequence;
        return 0;
    }

    /// <summary>
    /// Get statistics for a player's inputs
    /// </summary>
    public InputStats GetStats(string playerId)
    {
        return _stats.GetValueOrDefault(playerId, new InputStats());
    }

    /// <summary>
    /// Get all player statistics (for monitoring/debugging)
    /// </summary>
    public Dictionary<string, InputStats> GetAllStats()
    {
        return new Dictionary<string, InputStats>(_stats);
    }

    /// <summary>
    /// Clean up data for disconnected player
    /// </summary>
    public void CleanupPlayer(string playerId)
    {
        _lastProcessedSequence.Remove(playerId);
        _inputBuffers.Remove(playerId);
        _stats.Remove(playerId);

        _logger.LogDebug("Cleaned up input buffer for {PlayerId}", playerId);
    }

    /// <summary>
    /// Reset tracking for a player (useful for reconnects)
    /// </summary>
    public void ResetPlayer(string playerId)
    {
        _lastProcessedSequence[playerId] = 0;

        if (_inputBuffers.ContainsKey(playerId))
        {
            _inputBuffers[playerId].Clear();
        }

        if (_stats.ContainsKey(playerId))
        {
            _stats[playerId] = new InputStats();
        }

        _logger.LogInformation("Reset input buffer for {PlayerId}", playerId);
    }
}

/// <summary>
/// Statistics for input processing (useful for monitoring)
/// </summary>
public class InputStats
{
    public long TotalInputs { get; set; }
    public long InOrderInputs { get; set; }
    public long OutOfOrderInputs { get; set; }
    public long DuplicateInputs { get; set; }
    public long TotalPacketsLost { get; set; }

    public double PacketLossRate => TotalInputs > 0
        ? (double)TotalPacketsLost / TotalInputs
        : 0.0;

    public double InOrderRate => TotalInputs > 0
        ? (double)InOrderInputs / TotalInputs
        : 0.0;
}
