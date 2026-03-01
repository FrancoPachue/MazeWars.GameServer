using System.Collections.Concurrent;
using MazeWars.GameServer.Network.Models;
using Microsoft.Extensions.Logging;

namespace MazeWars.GameServer.Engine.Network;

/// <summary>
/// Manages input buffering and ordering to handle UDP packet reordering and loss.
/// Thread-safe: uses per-player locks to protect SortedDictionary access.
/// </summary>
public class InputBuffer
{
    private class BufferedInput
    {
        public uint SequenceNumber { get; set; }
        public PlayerInputMessage Input { get; set; } = null!;
        public DateTime ReceivedAt { get; set; }
    }

    private class PlayerBuffer
    {
        public readonly object Lock = new();
        public uint LastProcessedSequence;
        public readonly SortedDictionary<uint, BufferedInput> Buffer = new();
        public readonly InputStats Stats = new();
    }

    private readonly ConcurrentDictionary<string, PlayerBuffer> _players = new();
    private readonly ILogger _logger;

    // Configuration
    private const int MAX_BUFFER_SIZE = 100;
    private const int INPUT_TIMEOUT_MS = 100;
    private const uint MAX_ACCEPTABLE_GAP = 200; // If gap > this, just jump ahead

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
        var player = _players.GetOrAdd(playerId, _ => new PlayerBuffer());

        lock (player.Lock)
        {
            try
            {
                return ProcessInputLocked(player, playerId, input);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing input seq {Seq} for {PlayerId}, resetting buffer",
                    input.SequenceNumber, playerId);

                // Recovery: jump to current sequence and clear buffer
                player.Buffer.Clear();
                player.LastProcessedSequence = input.SequenceNumber;
                player.Stats.TotalInputs++;

                return [input];
            }
        }
    }

    private List<PlayerInputMessage> ProcessInputLocked(PlayerBuffer player, string playerId, PlayerInputMessage input)
    {
        var result = new List<PlayerInputMessage>();
        var lastSeq = player.LastProcessedSequence;

        // Ignore duplicate or old inputs
        if (input.SequenceNumber <= lastSeq)
        {
            player.Stats.DuplicateInputs++;
            return result;
        }

        player.Stats.TotalInputs++;

        // Input is the next expected sequence — happy path
        if (input.SequenceNumber == lastSeq + 1)
        {
            result.Add(input);
            player.LastProcessedSequence = input.SequenceNumber;
            player.Stats.InOrderInputs++;

            // Process any buffered inputs that are now in order
            DrainBufferedInputs(player, result);
            return result;
        }

        // GAP DETECTED
        var gap = input.SequenceNumber - lastSeq - 1;

        // If gap is too large, skip ahead entirely — no point buffering
        if (gap > MAX_ACCEPTABLE_GAP)
        {
            _logger.LogWarning(
                "Large input gap for {PlayerId}: expected {Expected}, got {Got} (gap: {Gap}). Jumping ahead.",
                playerId, lastSeq + 1, input.SequenceNumber, gap);

            player.Buffer.Clear();
            player.LastProcessedSequence = input.SequenceNumber;
            player.Stats.OutOfOrderInputs++;
            player.Stats.TotalPacketsLost += (int)gap;

            result.Add(input);
            return result;
        }

        player.Stats.OutOfOrderInputs++;
        player.Stats.TotalPacketsLost += (int)gap;

        // Buffer this input until gap is filled or timeout
        if (player.Buffer.Count >= MAX_BUFFER_SIZE)
        {
            _logger.LogWarning("Input buffer overflow for {PlayerId}, forcing processing", playerId);
            ForceProcessBufferLocked(player, playerId, result);
        }

        player.Buffer[input.SequenceNumber] = new BufferedInput
        {
            SequenceNumber = input.SequenceNumber,
            Input = input,
            ReceivedAt = DateTime.UtcNow
        };

        // Check if we should give up waiting for missing packets
        CheckTimeoutsLocked(player, playerId, result);

        return result;
    }

    /// <summary>
    /// Process consecutive buffered inputs starting from LastProcessedSequence.
    /// </summary>
    private static void DrainBufferedInputs(PlayerBuffer player, List<PlayerInputMessage> result)
    {
        var lastSeq = player.LastProcessedSequence;

        while (player.Buffer.Count > 0 && player.Buffer.TryGetValue(lastSeq + 1, out var buffered))
        {
            result.Add(buffered.Input);
            player.Buffer.Remove(lastSeq + 1);
            lastSeq++;
        }

        player.LastProcessedSequence = lastSeq;
    }

    /// <summary>
    /// Check for inputs that have been buffered too long and force process.
    /// </summary>
    private void CheckTimeoutsLocked(PlayerBuffer player, string playerId, List<PlayerInputMessage> result)
    {
        if (player.Buffer.Count == 0) return;

        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(INPUT_TIMEOUT_MS);

        // Get oldest buffered input
        var oldestEntry = player.Buffer.First();
        var oldest = oldestEntry.Value;
        var age = now - oldest.ReceivedAt;

        if (age > timeout)
        {
            _logger.LogWarning(
                "Input timeout for {PlayerId}: oldest buffered seq {OldestSeq} aged {Age}ms, " +
                "last processed {LastSeq}. Skipping gap.",
                playerId, oldest.SequenceNumber, age.TotalMilliseconds, player.LastProcessedSequence);

            // Skip the gap by advancing to just before the oldest buffered input
            player.LastProcessedSequence = oldest.SequenceNumber - 1;

            // Now drain all consecutive buffered inputs
            DrainBufferedInputs(player, result);
        }
    }

    /// <summary>
    /// Force process buffer when it's getting too full.
    /// </summary>
    private void ForceProcessBufferLocked(PlayerBuffer player, string playerId, List<PlayerInputMessage> result)
    {
        if (player.Buffer.Count == 0) return;

        var oldestEntry = player.Buffer.First();
        var oldest = oldestEntry.Value;

        _logger.LogWarning("Force processing buffer for {PlayerId}, jumping to seq {Seq}",
            playerId, oldest.SequenceNumber - 1);

        // Skip to oldest buffered input
        player.LastProcessedSequence = oldest.SequenceNumber - 1;

        // Process all consecutive buffered inputs
        DrainBufferedInputs(player, result);
    }

    /// <summary>
    /// Get the last acknowledged sequence number for a player (for sending to client).
    /// </summary>
    public uint GetLastAcknowledgedSequence(string playerId)
    {
        if (_players.TryGetValue(playerId, out var player))
        {
            lock (player.Lock)
            {
                return player.LastProcessedSequence;
            }
        }
        return 0u;
    }

    /// <summary>
    /// Get statistics for a player's inputs.
    /// </summary>
    public InputStats GetStats(string playerId)
    {
        if (_players.TryGetValue(playerId, out var player))
        {
            lock (player.Lock)
            {
                return new InputStats
                {
                    TotalInputs = player.Stats.TotalInputs,
                    InOrderInputs = player.Stats.InOrderInputs,
                    OutOfOrderInputs = player.Stats.OutOfOrderInputs,
                    DuplicateInputs = player.Stats.DuplicateInputs,
                    TotalPacketsLost = player.Stats.TotalPacketsLost,
                };
            }
        }
        return new InputStats();
    }

    /// <summary>
    /// Get all player statistics (for monitoring/debugging).
    /// </summary>
    public Dictionary<string, InputStats> GetAllStats()
    {
        var result = new Dictionary<string, InputStats>();
        foreach (var kvp in _players)
        {
            lock (kvp.Value.Lock)
            {
                result[kvp.Key] = new InputStats
                {
                    TotalInputs = kvp.Value.Stats.TotalInputs,
                    InOrderInputs = kvp.Value.Stats.InOrderInputs,
                    OutOfOrderInputs = kvp.Value.Stats.OutOfOrderInputs,
                    DuplicateInputs = kvp.Value.Stats.DuplicateInputs,
                    TotalPacketsLost = kvp.Value.Stats.TotalPacketsLost,
                };
            }
        }
        return result;
    }

    /// <summary>
    /// Clean up data for disconnected player.
    /// </summary>
    public void CleanupPlayer(string playerId)
    {
        _players.TryRemove(playerId, out _);
        _logger.LogDebug("Cleaned up input buffer for {PlayerId}", playerId);
    }

    /// <summary>
    /// Reset tracking for a player (useful for reconnects).
    /// </summary>
    public void ResetPlayer(string playerId)
    {
        var player = _players.GetOrAdd(playerId, _ => new PlayerBuffer());

        lock (player.Lock)
        {
            player.LastProcessedSequence = 0;
            player.Buffer.Clear();
            player.Stats.TotalInputs = 0;
            player.Stats.InOrderInputs = 0;
            player.Stats.OutOfOrderInputs = 0;
            player.Stats.DuplicateInputs = 0;
            player.Stats.TotalPacketsLost = 0;
        }

        _logger.LogInformation("Reset input buffer for {PlayerId}", playerId);
    }
}

/// <summary>
/// Statistics for input processing (useful for monitoring).
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
