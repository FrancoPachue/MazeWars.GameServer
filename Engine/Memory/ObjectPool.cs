using System.Collections.Concurrent;

namespace MazeWars.GameServer.Engine.Memory;

/// <summary>
/// Thread-safe generic object pool to reduce GC allocations.
/// Used for high-frequency allocations like network messages.
/// </summary>
/// <typeparam name="T">Type of object to pool</typeparam>
public class ObjectPool<T> where T : class, new()
{
    private readonly ConcurrentBag<T> _pool = new();
    private readonly Func<T>? _factory;
    private readonly Action<T>? _resetAction;
    private readonly int _maxPoolSize;

    // Statistics
    private long _rentCount = 0;
    private long _returnCount = 0;
    private long _newAllocations = 0;

    /// <summary>
    /// Create a new object pool
    /// </summary>
    /// <param name="factory">Optional factory function to create new instances</param>
    /// <param name="resetAction">Optional action to reset object state when returned to pool</param>
    /// <param name="maxPoolSize">Maximum objects to keep in pool (prevents memory bloat)</param>
    public ObjectPool(Func<T>? factory = null, Action<T>? resetAction = null, int maxPoolSize = 1000)
    {
        _factory = factory;
        _resetAction = resetAction;
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Get an object from the pool (or create new if pool is empty)
    /// </summary>
    public T Rent()
    {
        Interlocked.Increment(ref _rentCount);

        if (_pool.TryTake(out var item))
        {
            return item;
        }

        // Pool empty, allocate new
        Interlocked.Increment(ref _newAllocations);
        return _factory != null ? _factory() : new T();
    }

    /// <summary>
    /// Return an object to the pool for reuse
    /// </summary>
    public void Return(T item)
    {
        if (item == null)
        {
            return;
        }

        Interlocked.Increment(ref _returnCount);

        // Reset object state
        _resetAction?.Invoke(item);

        // Only add to pool if not at capacity (prevent memory bloat)
        if (_pool.Count < _maxPoolSize)
        {
            _pool.Add(item);
        }
        // else: let GC collect it (pool is full enough)
    }

    /// <summary>
    /// Return multiple objects to the pool
    /// </summary>
    public void ReturnRange(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Return(item);
        }
    }

    /// <summary>
    /// Get current pool size
    /// </summary>
    public int CurrentSize => _pool.Count;

    /// <summary>
    /// Get pool statistics
    /// </summary>
    public PoolStats GetStats()
    {
        return new PoolStats
        {
            RentCount = Interlocked.Read(ref _rentCount),
            ReturnCount = Interlocked.Read(ref _returnCount),
            NewAllocations = Interlocked.Read(ref _newAllocations),
            CurrentPoolSize = _pool.Count,
            MaxPoolSize = _maxPoolSize
        };
    }

    /// <summary>
    /// Clear the pool (for cleanup/shutdown)
    /// </summary>
    public void Clear()
    {
        while (_pool.TryTake(out _))
        {
            // Empty the pool
        }
    }

    /// <summary>
    /// Pre-warm the pool with objects
    /// </summary>
    public void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var item = _factory != null ? _factory() : new T();
            _pool.Add(item);
        }
    }
}

/// <summary>
/// Statistics for an object pool
/// </summary>
public class PoolStats
{
    public long RentCount { get; set; }
    public long ReturnCount { get; set; }
    public long NewAllocations { get; set; }
    public int CurrentPoolSize { get; set; }
    public int MaxPoolSize { get; set; }

    public long OutstandingObjects => RentCount - ReturnCount;
    public double ReuseRate => RentCount > 0 ? (double)(RentCount - NewAllocations) / RentCount : 0.0;
}
