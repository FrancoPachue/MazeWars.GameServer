using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MazeWars.GameServer.Monitoring;

// Interfaz para métricas del sistema
public interface ISystemMetrics
{
    double GetCpuUsage();
    double GetMemoryUsageMB();
    double GetAvailableMemoryMB();
    int GetThreadCount();
    TimeSpan GetUptime();
    double GetNetworkBytesPerSecond();
}

// Implementación multiplataforma
public class SystemMetrics : ISystemMetrics, IDisposable
{
    private readonly ILogger<SystemMetrics> _logger;

    // Windows Performance Counters (solo en Windows)
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memoryCounter;

    // Alternativas multiplataforma
    private readonly Process _currentProcess;
    private DateTime _lastCpuTime;
    private TimeSpan _lastProcessorTime;
    private long _lastNetworkBytes;
    private DateTime _lastNetworkCheck;

    // Cache para evitar cálculos excesivos
    private double _cachedCpuUsage;
    private DateTime _lastCpuCheck;
    private readonly TimeSpan _cacheInterval = TimeSpan.FromSeconds(1);

    public SystemMetrics(ILogger<SystemMetrics> logger)
    {
        _logger = logger;
        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = DateTime.UtcNow;
        _lastProcessorTime = _currentProcess.TotalProcessorTime;
        _lastNetworkCheck = DateTime.UtcNow;

        InitializePerformanceCounters();
    }

    private void InitializePerformanceCounters()
    {
        try
        {
            // Solo inicializar en Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");

                // Primera lectura para inicializar
                _cpuCounter.NextValue();

                _logger.LogInformation("Windows Performance Counters initialized");
            }
            else
            {
                _logger.LogInformation("Running on non-Windows platform, using alternative metrics");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Performance Counters, falling back to alternative methods");
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _cpuCounter = null;
            _memoryCounter = null;
        }
    }

    public double GetCpuUsage()
    {
        try
        {
            // Usar cache para evitar lecturas excesivas
            if (DateTime.UtcNow - _lastCpuCheck < _cacheInterval)
            {
                return _cachedCpuUsage;
            }

            if (_cpuCounter != null)
            {
                // Windows: usar PerformanceCounter
                _cachedCpuUsage = Math.Round(_cpuCounter.NextValue(), 2);
            }
            else
            {
                // Multiplataforma: calcular basado en tiempo de procesador
                _cachedCpuUsage = CalculateCpuUsageAlternative();
            }

            _lastCpuCheck = DateTime.UtcNow;
            return _cachedCpuUsage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting CPU usage");
            return 0.0;
        }
    }

    private double CalculateCpuUsageAlternative()
    {
        try
        {
            var currentTime = DateTime.UtcNow;
            var currentProcessorTime = _currentProcess.TotalProcessorTime;

            var timeDiff = currentTime - _lastCpuTime;
            var processorTimeDiff = currentProcessorTime - _lastProcessorTime;

            if (timeDiff.TotalMilliseconds > 0)
            {
                var cpuUsage = (processorTimeDiff.TotalMilliseconds / timeDiff.TotalMilliseconds) * 100.0;
                cpuUsage = cpuUsage / Environment.ProcessorCount; // Normalizar por número de cores

                _lastCpuTime = currentTime;
                _lastProcessorTime = currentProcessorTime;

                return Math.Round(Math.Min(cpuUsage, 100.0), 2);
            }

            return 0.0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating alternative CPU usage");
            return 0.0;
        }
    }

    public double GetMemoryUsageMB()
    {
        try
        {
            // Memoria usada por el proceso actual
            return Math.Round(_currentProcess.WorkingSet64 / 1024.0 / 1024.0, 2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting memory usage");
            return 0.0;
        }
    }

    public double GetAvailableMemoryMB()
    {
        try
        {
            if (_memoryCounter != null)
            {
                // Windows: usar PerformanceCounter
                return Math.Round(_memoryCounter.NextValue(), 2);
            }
            else
            {
                // Alternativa: usar GC para memoria disponible aproximada
                return Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available memory");
            return 0.0;
        }
    }

    public int GetThreadCount()
    {
        try
        {
            return _currentProcess.Threads.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting thread count");
            return 0;
        }
    }

    public TimeSpan GetUptime()
    {
        try
        {
            return DateTime.UtcNow - _currentProcess.StartTime;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting uptime");
            return TimeSpan.Zero;
        }
    }

    public double GetNetworkBytesPerSecond()
    {
        try
        {
            // Simulación básica - en producción usarías NetworkInterface
            var currentTime = DateTime.UtcNow;
            var timeDiff = (currentTime - _lastNetworkCheck).TotalSeconds;

            if (timeDiff >= 1.0)
            {
                // Aquí podrías usar NetworkInterface.GetAllNetworkInterfaces()
                // para obtener estadísticas reales de red
                _lastNetworkCheck = currentTime;
                return Math.Round(Random.Shared.NextDouble() * 1024 * 1024, 2); // Placeholder
            }

            return 0.0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting network usage");
            return 0.0;
        }
    }

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _memoryCounter?.Dispose();
        _currentProcess?.Dispose();
    }
}