using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddsTracker.Core.Clients;
using OddsTracker.Core.Data;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using OddsTracker.Core.Services;
using Sportsdata.Core.Extensions;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables();

        // Azure Key Vault
        var builtConfig = config.Build();
        var keyVaultUrl = builtConfig["Azure:KeyVaultUrl"];
        if (!string.IsNullOrEmpty(keyVaultUrl))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                TenantId = builtConfig["Azure:TenantId"]
            });

            config.AddAzureKeyVault(new Uri(keyVaultUrl), credential);
        }
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Redis Cache
        if (configuration.GetValue<bool>("Cache:Enabled"))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = configuration["AppSettings:RedisConnection"] ?? "localhost:6379";
                options.InstanceName = "OddsTracker:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // EF Core - Azure SQL
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddDbContextPool<OddsTrackerDbContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOptions.CommandTimeout(30);
                });

                if (context.HostingEnvironment.IsProduction())
                {
                    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                    {
                        ExcludeEnvironmentCredential = true,
                        ExcludeWorkloadIdentityCredential = true,
                        TenantId = configuration["Azure:TenantId"]
                    });
                    options.AddInterceptors(new AzureSqlAuthInterceptor(credential));
                }
            });

            // Scoped repositories (DbContext-backed)
            services.AddScoped<IHistoricalRepository, HistoricalRepository>();
            services.AddScoped<IMarketRepository, MarketRepository>();
        }
        else
        {
            services.AddSingleton<IHistoricalRepository, InMemoryHistoricalRepository>();
        }

        // Sportsdata EF Core - Azure SQL (for teams, players, stadiums)
        var sportsdataConnectionString = configuration.GetConnectionString("SportsdataConnection");
        if (!string.IsNullOrEmpty(sportsdataConnectionString))
        {
            services.AddSportsdataCoreWithFactory(sportsdataConnectionString);
        }

        // Configure options
        services.Configure<CacheOptions>(configuration.GetSection("Cache"));
        services.Configure<AlertEngineOptions>(configuration.GetSection("AlertEngine"));
        services.Configure<ConfidenceScoringOptions>(configuration.GetSection("ConfidenceScoring"));
        services.Configure<HistoricalTrackerOptions>(configuration.GetSection("HistoricalTracker"));

        // ============================================
        // SERVICE REGISTRATION ORDER (NO CYCLES)
        // ============================================

        // 1. Core infrastructure
        services.AddMemoryCache();
        services.AddSingleton<EnhancedCacheService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());
        services.AddSingleton<IEnhancedCacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());

        // 2. Cached Market Data Service (centralized repository caching)
        services.AddSingleton<ICachedMarketDataService>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var cacheService = sp.GetRequiredService<IEnhancedCacheService>();
            var logger = sp.GetRequiredService<ILogger<CachedMarketDataService>>();
            return new CachedMarketDataService(scopeFactory, cacheService, logger);
        });

        // 3. The Odds API Client
        services.AddHttpClient<IOddsApiClient, OddsApiClient>((sp, client) =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddTypedClient<IOddsApiClient>((httpClient, sp) =>
        {
            var apiKey = configuration["AppSettings:TheOddsApiKey"]
                ?? throw new InvalidOperationException("TheOddsApiKey not configured");
            var logger = sp.GetRequiredService<ILogger<OddsApiClient>>();

            return new OddsApiClient(httpClient, apiKey, logger);
        });

        // 4. Sports Data Service (with sport-specific client factory)
        services.AddSingleton<ISportClientFactory, OddsTracker.Core.Services.SportClients.SportClientFactory>();
        services.AddSingleton<ISportsDataService, SportsDataService>();

        // 5. Movement Fingerprint Service (uses ICachedMarketDataService)
        services.AddSingleton<IMovementFingerprintService>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var cacheService = sp.GetRequiredService<IEnhancedCacheService>();
            var logger = sp.GetRequiredService<ILogger<MovementFingerprintService>>();
            return new MovementFingerprintService(scopeFactory, cacheService, logger);
        });

        // 6. Confidence Scoring Engine
        services.AddSingleton<IConfidenceScoringEngine, ConfidenceScoringEngine>();

        // 7. Historical Tracker (uses IServiceScopeFactory for IHistoricalRepository)
        services.AddSingleton<IHistoricalTracker>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var cacheService = sp.GetRequiredService<IEnhancedCacheService>();
            var logger = sp.GetRequiredService<ILogger<HistoricalTracker>>();
            var options = sp.GetService<IOptions<HistoricalTrackerOptions>>();
            return new HistoricalTracker(scopeFactory, cacheService, logger, options);
        });

        // 8. Alert Engine
        services.AddSingleton<IAlertEngine, AlertEngine>();

        // 9. Discord Alert Service (webhook-based for Functions)
        services.AddSingleton<IDiscordAlertService, WebhookDiscordAlertService>();

        // Logging
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
    })
    .Build();

// Initialize SportClientFactory with sport aliases from database
var sportClientFactory = host.Services.GetRequiredService<ISportClientFactory>();
await sportClientFactory.InitializeAsync();

await host.RunAsync();