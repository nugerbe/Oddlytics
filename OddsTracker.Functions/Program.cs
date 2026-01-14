using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Clients;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        var env = context.HostingEnvironment;

        // Load shared appsettings from main OddsTracker project
        var basePath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "OddsTracker"));
        if (Directory.Exists(basePath))
        {
            config.SetBasePath(basePath);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
        }

        // Also check current directory (for deployed scenarios)
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        // Environment variables override file settings
        config.AddEnvironmentVariables();

        // Add Key Vault configuration for Azure deployment
        var builtConfig = config.Build();
        var keyVaultUrl = builtConfig["Azure:KeyVaultUrl"];
        if (!string.IsNullOrEmpty(keyVaultUrl))
            config.AddAzureKeyVault(new Uri(keyVaultUrl), new DefaultAzureCredential());
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Redis Cache
        if (configuration.GetValue<bool>("Cache:Enabled"))
        {
            bool useExternalCache = configuration.GetValue<bool>("Cache:UseExternalCache");
            if (!useExternalCache) services.AddHostedService<RedisDockerHostedService>();

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = useExternalCache ? configuration["AppSettings:RedisConnection"] : "localhost:6379";
                options.InstanceName = "OddsTracker:";
            });
        }


        // Cache Service
        services.AddSingleton<IEnhancedCacheService, EnhancedCacheService>();

        // HTTP Client for Odds API
        services.AddHttpClient("OddsApi", client =>
        {
            client.BaseAddress = new Uri("https://api.the-odds-api.com/v4/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // The Odds API Client
        services.AddSingleton<IOddsApiClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("OddsApi");
            var apiKey = configuration["AppSettings:TheOddsApiKey"] ?? throw new InvalidOperationException("AppSettings:TheOddsApiKey not configured");

            return new OddsApiClient(httpClient, apiKey);
        });

        // Platform Services
        services.AddSingleton<IMovementFingerprintService, MovementFingerprintService>();
        services.AddSingleton<IConfidenceScoringEngine, ConfidenceScoringEngine>();
        services.AddSingleton<IAlertEngine, AlertEngine>();
        services.AddSingleton<IHistoricalTracker, HistoricalTracker>();

        // Discord Webhook Alert Service
        services.AddHttpClient("Discord", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IDiscordAlertService, WebhookDiscordAlertService>();
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.AddConsole();

        // Set log levels
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
        logging.AddFilter("OddsTracker", LogLevel.Debug);
    })
    .Build();

host.Run();