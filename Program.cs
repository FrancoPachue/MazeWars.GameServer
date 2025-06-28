using MazeWars.GameServer.Engine;
using MazeWars.GameServer.Configuration;
using MazeWars.GameServer.Monitoring;
using MazeWars.GameServer.Security;
using MazeWars.GameServer.Admin;
using Serilog;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MazeWars.GameServer.Network.Services;
using MazeWars.GameServer.Engine.Combat.Interface;
using MazeWars.GameServer.Services.Combat;
using MazeWars.GameServer.Engine.Loot.Interface;
using MazeWars.GameServer.Services.Loot;
using MazeWars.GameServer.Engine.Movement.Interface;
using MazeWars.GameServer.Services.Movement;
using MazeWars.GameServer.Engine.AI.Interface;
using MazeWars.GameServer.Services.AI;

var builder = WebApplication.CreateBuilder(args);

// =============================================
// LOGGING CONFIGURATION
// =============================================

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "MazeWars.GameServer")
    .Enrich.WithProperty("Version", "1.0.0")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/mazewars-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    Log.Information("Starting MazeWars Game Server...");

    // =============================================
    // CONFIGURATION
    // =============================================

    // Load configuration from appsettings.json
    builder.Services.Configure<GameServerSettings>(
        builder.Configuration.GetSection("GameServer"));

    // Add configuration validation - ACTUALIZADO para incluir validación de lobby
    builder.Services.AddOptions<GameServerSettings>()
        .Bind(builder.Configuration.GetSection("GameServer"))
        .ValidateDataAnnotations()
        .Validate(config =>
        {
            // Validaciones personalizadas para el sistema de lobby
            if (config.LobbySettings == null)
            {
                Log.Warning("LobbySettings not found in configuration, using defaults");
                return true; // Permitir, usará valores por defecto
            }

            // Validar coherencia de configuración de lobby
            if (config.LobbySettings.MinPlayersToStart > config.MaxPlayersPerWorld)
            {
                Log.Error("LobbySettings.MinPlayersToStart ({MinPlayers}) cannot be greater than MaxPlayersPerWorld ({MaxPlayers})",
                    config.LobbySettings.MinPlayersToStart, config.MaxPlayersPerWorld);
                return false;
            }

            if (config.LobbySettings.MaxWaitTimeSeconds > config.LobbySettings.AbsoluteMaxWaitTimeSeconds)
            {
                Log.Error("LobbySettings.MaxWaitTimeSeconds ({MaxWait}) cannot be greater than AbsoluteMaxWaitTimeSeconds ({AbsoluteMaxWait})",
                    config.LobbySettings.MaxWaitTimeSeconds, config.LobbySettings.AbsoluteMaxWaitTimeSeconds);
                return false;
            }

            if (config.GameBalance.MaxTeamSize > config.MaxPlayersPerWorld)
            {
                Log.Error("GameBalance.MaxTeamSize ({MaxTeamSize}) cannot be greater than MaxPlayersPerWorld ({MaxPlayers})",
                    config.GameBalance.MaxTeamSize, config.MaxPlayersPerWorld);
                return false;
            }

            return true;
        }, "Lobby system configuration validation failed")
        .ValidateOnStart();

    // =============================================
    // CORE SERVICES
    // =============================================

    builder.Services.AddSingleton<ICombatSystem,CombatSystem>();

    builder.Services.AddSingleton<IMovementSystem,MovementSystem>();

    builder.Services.AddSingleton<ILootSystem,LootSystem>();

    builder.Services.AddSingleton<IMobAISystem, MobAISystem>();
    // Game Engine (Singleton - maintains game state)
    builder.Services.AddSingleton<RealTimeGameEngine>();

    // Network Service (Singleton - manages UDP connections)
    builder.Services.AddSingleton<UdpNetworkService>();

    // Security Services
    builder.Services.AddSingleton<RateLimitingService>();

    // System Metrics (choose implementation based on platform)
    builder.Services.AddSingleton<ISystemMetrics, SystemMetrics>();

    // =============================================
    // HOSTED SERVICES (Background Services)
    // =============================================

    // Main game server service
    builder.Services.AddHostedService<GameServerHostedService>();

    // Metrics collection service
    builder.Services.AddHostedService<MetricsService>();

    // =============================================
    // WEB API SERVICES
    // =============================================

    // Controllers for admin endpoints
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Configure JSON serialization
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.WriteIndented = true;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

    // API Documentation
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "MazeWars Game Server API",
            Version = "v1",
            Description = "Real-time multiplayer extraction RPG server with lobby system",
            Contact = new Microsoft.OpenApi.Models.OpenApiContact
            {
                Name = "MazeWars Team",
                Email = "admin@mazewars.com"
            }
        });

        // Include XML comments for API documentation
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }
    });

    // CORS for web clients (if needed)
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("GameClientPolicy", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // Health Checks
    builder.Services.AddHealthChecks()
        .AddCheck<GameServerHealthCheck>("game_server")
        .AddCheck<NetworkServiceHealthCheck>("network_service");

    // =============================================
    // ADDITIONAL SERVICES
    // =============================================

    // HTTP Client for external API calls (if needed)
    builder.Services.AddHttpClient();

    // Memory Cache (if needed for caching)
    builder.Services.AddMemoryCache();

    // =============================================
    // BUILD APPLICATION
    // =============================================

    var app = builder.Build();

    // =============================================
    // MIDDLEWARE PIPELINE
    // =============================================

    // Global exception handling
    app.UseMiddleware<GlobalExceptionMiddleware>();

    // Development-specific middleware
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "MazeWars Game Server API v1");
            c.RoutePrefix = "docs"; // Swagger UI at /docs
        });
    }

    // Security middleware
    app.UseMiddleware<SecurityHeadersMiddleware>();

    // CORS
    app.UseCors("GameClientPolicy");

    // Request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].FirstOrDefault());
        };
    });

    // =============================================
    // ENDPOINT MAPPING
    // =============================================

    // Map controllers (admin endpoints)
    app.MapControllers();

    // Health check endpoints
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var response = new
            {
                Status = report.Status.ToString(),
                Service = "MazeWars.GameServer",
                Timestamp = DateTime.UtcNow,
                Duration = report.TotalDuration,
                Checks = report.Entries.Select(x => new
                {
                    Name = x.Key,
                    Status = x.Value.Status.ToString(),
                    Duration = x.Value.Duration,
                    Description = x.Value.Description,
                    Data = x.Value.Data
                })
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    });

    // Simple health endpoint
    app.MapGet("/health/simple", () => Results.Ok(new
    {
        Status = "Healthy",
        Service = "MazeWars.GameServer",
        Timestamp = DateTime.UtcNow,
        Version = "1.0.0"
    }));

    // Server information endpoint - ACTUALIZADO para incluir información de lobby
    app.MapGet("/info", (IServiceProvider services) =>
    {
        var gameEngine = services.GetRequiredService<RealTimeGameEngine>();
        var networkService = services.GetRequiredService<UdpNetworkService>();
        var systemMetrics = services.GetRequiredService<ISystemMetrics>();
        var gameConfig = services.GetRequiredService<IOptions<GameServerSettings>>().Value;

        var gameStats = gameEngine.GetServerStats();
        var networkStats = networkService.GetNetworkStats();
        var lobbyStats = gameEngine.GetLobbyStats(); // NUEVO

        return Results.Ok(new
        {
            Server = new
            {
                Name = "MazeWars Game Server",
                Version = "1.0.0",
                Environment = app.Environment.EnvironmentName,
                StartTime = System.Diagnostics.Process.GetCurrentProcess().StartTime,
                Uptime = systemMetrics.GetUptime(),
                ServerId = gameConfig.ServerId
            },
            Game = gameStats,
            Lobby = lobbyStats, // NUEVO
            Network = networkStats,
            System = new
            {
                CPUUsage = systemMetrics.GetCpuUsage(),
                MemoryUsageMB = systemMetrics.GetMemoryUsageMB(),
                ThreadCount = systemMetrics.GetThreadCount(),
                Platform = Environment.OSVersion.Platform.ToString(),
                ProcessorCount = Environment.ProcessorCount
            },
            Configuration = new // NUEVO
            {
                MaxPlayersPerWorld = gameConfig.MaxPlayersPerWorld,
                MaxWorldInstances = gameConfig.MaxWorldInstances,
                TargetFPS = gameConfig.TargetFPS,
                LobbyMinPlayers = gameConfig.LobbySettings?.MinPlayersToStart ?? 2,
                LobbyMaxWaitTime = gameConfig.LobbySettings?.MaxWaitTimeSeconds ?? 30,
                MaxTeamSize = gameConfig.GameBalance.MaxTeamSize
            }
        });
    });

    // NUEVO: Endpoint específico para información de lobbies
    app.MapGet("/lobbies", (IServiceProvider services) =>
    {
        var gameEngine = services.GetRequiredService<RealTimeGameEngine>();
        var lobbyStats = gameEngine.GetLobbyStats();

        return Results.Ok(new
        {
            Timestamp = DateTime.UtcNow,
            Lobbies = lobbyStats,
            Summary = new
            {
                TotalLobbies = lobbyStats.TryGetValue("TotalLobbies", out var totalLobbies) ? totalLobbies : 0,
                LobbyPlayers = lobbyStats.TryGetValue("LobbyPlayers", out var lobbyPlayers) ? lobbyPlayers : 0,
                ActiveWorlds = lobbyStats.TryGetValue("ActiveWorlds", out var activeWorlds) ? activeWorlds : 0,
                ActivePlayers = lobbyStats.TryGetValue("ActivePlayers", out var activePlayers) ? activePlayers : 0
            }
        });
    });

    // Metrics endpoint (for monitoring systems) - ACTUALIZADO con métricas de lobby
    app.MapGet("/metrics", (IServiceProvider services) =>
    {
        var gameEngine = services.GetRequiredService<RealTimeGameEngine>();
        var networkService = services.GetRequiredService<UdpNetworkService>();
        var systemMetrics = services.GetRequiredService<ISystemMetrics>();

        var gameStats = gameEngine.GetServerStats();
        var networkStats = networkService.GetDetailedNetworkStats();
        var lobbyStats = gameEngine.GetLobbyStats();

        // Prometheus-style metrics format
        var metrics = new List<string>
        {
            $"# HELP mazewars_players_total Total number of connected players",
            $"# TYPE mazewars_players_total gauge",
            $"mazewars_players_total {gameStats.GetValueOrDefault("TotalPlayers", 0)}",

            $"# HELP mazewars_worlds_total Total number of active worlds",
            $"# TYPE mazewars_worlds_total gauge",
            $"mazewars_worlds_total {gameStats.GetValueOrDefault("WorldCount", 0)}",

            $"# HELP mazewars_lobbies_total Total number of active lobbies",
            $"# TYPE mazewars_lobbies_total gauge",
            $"mazewars_lobbies_total {lobbyStats.GetValueOrDefault("TotalLobbies", 0)}",

            $"# HELP mazewars_lobby_players_total Total players waiting in lobbies",
            $"# TYPE mazewars_lobby_players_total gauge",
            $"mazewars_lobby_players_total {lobbyStats.GetValueOrDefault("LobbyPlayers", 0)}",

            $"# HELP mazewars_cpu_usage_percent CPU usage percentage",
            $"# TYPE mazewars_cpu_usage_percent gauge",
            $"mazewars_cpu_usage_percent {systemMetrics.GetCpuUsage()}",

            $"# HELP mazewars_memory_usage_mb Memory usage in megabytes",
            $"# TYPE mazewars_memory_usage_mb gauge",
            $"mazewars_memory_usage_mb {systemMetrics.GetMemoryUsageMB()}",

            $"# HELP mazewars_packets_sent_total Total packets sent",
            $"# TYPE mazewars_packets_sent_total counter",
            $"mazewars_packets_sent_total {networkStats.GetValueOrDefault("PacketsSent", 0)}",

            $"# HELP mazewars_packets_received_total Total packets received",
            $"# TYPE mazewars_packets_received_total counter",
            $"mazewars_packets_received_total {networkStats.GetValueOrDefault("PacketsReceived", 0)}"
        };

        return Results.Text(string.Join("\n", metrics), "text/plain");
    });

    // Root endpoint
    app.MapGet("/", () => Results.Redirect("/docs"));

    // =============================================
    // STARTUP VALIDATION
    // =============================================

    // Validate configuration - ACTUALIZADO para mostrar configuración de lobby
    var gameConfig = app.Services.GetRequiredService<IOptions<GameServerSettings>>().Value;

    Log.Information("Game Server Configuration: {@Config}", new
    {
        gameConfig.UdpPort,
        gameConfig.MaxPlayersPerWorld,
        gameConfig.TargetFPS,
        gameConfig.MaxWorldInstances,
        gameConfig.ServerId,
        LobbySettings = new
        {
            gameConfig.LobbySettings?.MinPlayersToStart,
            gameConfig.LobbySettings?.MaxWaitTimeSeconds,
            gameConfig.LobbySettings?.MinTeamsToStart,
            gameConfig.LobbySettings?.AutoBalanceTeams
        }
    });

    // Validar que la configuración de lobby existe
    if (gameConfig.LobbySettings == null)
    {
        Log.Warning("⚠️  LobbySettings not found in configuration. Lobby system may not work properly!");
        Log.Information("Please add LobbySettings section to your appsettings.json");
    }
    else
    {
        Log.Information("✅ Lobby system configured: Min {MinPlayers} players, Max wait {MaxWait}s",
            gameConfig.LobbySettings.MinPlayersToStart,
            gameConfig.LobbySettings.MaxWaitTimeSeconds);
    }

    // Ensure log directory exists
    Directory.CreateDirectory("logs");

    // =============================================
    // GRACEFUL SHUTDOWN
    // =============================================

    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

    lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information("🚀 MazeWars Game Server started successfully!");
        Log.Information("🎮 UDP Game Port: {UdpPort}", gameConfig.UdpPort);
        Log.Information("🌐 HTTP Admin API: {Url}", app.Urls.FirstOrDefault() ?? "http://localhost:5000");
        Log.Information("📊 Health Check: {Url}/health", app.Urls.FirstOrDefault() ?? "http://localhost:5000");
        Log.Information("📚 API Docs: {Url}/docs", app.Urls.FirstOrDefault() ?? "http://localhost:5000");
        Log.Information("🏛️  Lobby Info: {Url}/lobbies", app.Urls.FirstOrDefault() ?? "http://localhost:5000");

        // Log lobby configuration
        if (gameConfig.LobbySettings != null)
        {
            Log.Information("🎯 Lobby System: {MinPlayers}-{MaxPlayers} players, {MaxWait}s max wait",
                gameConfig.LobbySettings.MinPlayersToStart,
                gameConfig.MaxPlayersPerWorld,
                gameConfig.LobbySettings.MaxWaitTimeSeconds);
        }
    });

    lifetime.ApplicationStopping.Register(() =>
    {
        Log.Information("🛑 MazeWars Game Server is shutting down...");
    });

    lifetime.ApplicationStopped.Register(() =>
    {
        Log.Information("✅ MazeWars Game Server stopped gracefully");
        Log.CloseAndFlush();
    });

    // =============================================
    // RUN APPLICATION
    // =============================================

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "💥 MazeWars Game Server failed to start");
}
finally
{
    Log.CloseAndFlush();
}

// =============================================
// EXTENSION METHODS PARA MANEJO SEGURO DE DICCIONARIOS
// =============================================

public static class DictionaryExtensions
{
    public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, object> dictionary, TKey key, TValue defaultValue)
        where TKey : notnull
    {
        if (dictionary.TryGetValue(key, out var value) && value is TValue typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }
}