using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Clients;
using OddsTracker.Core.Data;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using OddsTracker.Core.Services;

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

            services.AddScoped<IHistoricalRepository, HistoricalRepository>();
            services.AddScoped<IMarketRepository, MarketRepository>();
        }
        else
        {
            services.AddSingleton<IHistoricalRepository, InMemoryHistoricalRepository>();
        }

        // Cache services
        services.AddSingleton<EnhancedCacheService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());
        services.AddSingleton<IEnhancedCacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());

        services.AddSingleton<IMarketRepository, MarketRepository>();

        // The Odds API Client - now requires ILookupService and IMarketAccessService
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

        // Configure options
        services.Configure<CacheOptions>(configuration.GetSection("Cache"));
        services.Configure<AlertEngineOptions>(configuration.GetSection("AlertEngine"));
        services.Configure<ConfidenceScoringOptions>(configuration.GetSection("ConfidenceScoring"));
        services.Configure<HistoricalTrackerOptions>(configuration.GetSection("HistoricalTracker"));

        // Core services
        services.AddMemoryCache();
        services.AddSingleton<ISportsDataService, SportsDataService>();
        services.AddSingleton<IMovementFingerprintService, MovementFingerprintService>();
        services.AddSingleton<IConfidenceScoringEngine, ConfidenceScoringEngine>();
        services.AddSingleton<IAlertEngine, AlertEngine>();
        services.AddSingleton<IHistoricalTracker, HistoricalTracker>();
        services.AddSingleton<IDiscordAlertService, WebhookDiscordAlertService>();

        // Logging
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
    })
    .Build();

host.Run();