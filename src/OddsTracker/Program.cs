using Anthropic;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OddsTracker;
using OddsTracker.Core.Clients;
using OddsTracker.Core.Data;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using OddsTracker.Core.Services;

var builder = Host.CreateApplicationBuilder(args);

// Set base path to where the exe is located (works for local and Azure)
builder.Configuration.SetBasePath(AppContext.BaseDirectory);

Console.WriteLine($"Loading config from: {AppContext.BaseDirectory}");

// Configuration - order matters (later sources override earlier ones)
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables();

// Azure Key Vault - load if vault URL is configured
var keyVaultUrl = builder.Configuration["Azure:KeyVaultUrl"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    Console.WriteLine($"Loading secrets from Azure Key Vault: {keyVaultUrl}");

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential = true,
        ExcludeWorkloadIdentityCredential = true,
        TenantId = builder.Configuration["Azure:TenantId"]
    });

    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl), credential);
    Console.WriteLine("Successfully connected to Key Vault");

    var envSuffix = builder.Environment.IsProduction() ? "Prod" : "Dev";
    var envConnectionString = builder.Configuration[$"ConnectionStrings:DefaultConnection:{envSuffix}"];
    if (!string.IsNullOrEmpty(envConnectionString))
    {
        builder.Configuration["ConnectionStrings:DefaultConnection"] = envConnectionString;
        Console.WriteLine($"Loaded connection string for environment: {envSuffix}");
    }
    else
    {
        Console.WriteLine($"WARNING: No connection string found for ConnectionStrings:DefaultConnection:{envSuffix}");
    }

    var discordToken = builder.Configuration["AppSettings:DiscordToken"];
    var claudeKey = builder.Configuration["AppSettings:ClaudeApiKey"];
    var oddsKey = builder.Configuration["AppSettings:TheOddsApiKey"];
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
    Console.WriteLine($"DiscordToken loaded: {!string.IsNullOrEmpty(discordToken)}");
    Console.WriteLine($"ClaudeApiKey loaded: {!string.IsNullOrEmpty(claudeKey)}");
    Console.WriteLine($"TheOddsApiKey loaded: {!string.IsNullOrEmpty(oddsKey)}");
    Console.WriteLine($"ConnectionString loaded: {!string.IsNullOrEmpty(connStr)}");
}

// Redis Cache - ALWAYS ensure IDistributedCache is registered
if (builder.Configuration.GetValue<bool>("Cache:Enabled"))
{
    bool useExternalCache = builder.Configuration.GetValue<bool>("Cache:UseExternalCache");
    if (!useExternalCache) builder.Services.AddHostedService<RedisDockerHostedService>();

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = useExternalCache ? builder.Configuration["AppSettings:RedisConnection"] : "localhost:6379";
        options.InstanceName = "OddsTracker:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// EF Core - Azure SQL Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddPooledDbContextFactory<OddsTrackerDbContext>(options =>
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(30);
        });

        if (builder.Environment.IsProduction())
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                TenantId = builder.Configuration["Azure:TenantId"]
            });

            options.AddInterceptors(new AzureSqlAuthInterceptor(credential));
            Console.WriteLine("Using Managed Identity for Azure SQL");
        }
        else
        {
            Console.WriteLine("Using SQL Authentication for Azure SQL");
        }
    });

    builder.Services.AddScoped<IHistoricalRepository, HistoricalRepository>();
    builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
}
else
{
    builder.Services.AddSingleton<IHistoricalRepository, InMemoryHistoricalRepository>();
    Console.WriteLine("No connection string - using in-memory repository");
}

// Configure options FIRST
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection("Cache"));
builder.Services.Configure<AlertEngineOptions>(builder.Configuration.GetSection("AlertEngine"));
builder.Services.Configure<ConfidenceScoringOptions>(builder.Configuration.GetSection("ConfidenceScoring"));
builder.Services.Configure<HistoricalTrackerOptions>(builder.Configuration.GetSection("HistoricalTracker"));
builder.Services.Configure<DiscordBotOptions>(options =>
{
    var discord = builder.Configuration.GetSection("Discord");
    options.Token = builder.Configuration["AppSettings:DiscordToken"] ?? string.Empty;
    options.GuildId = discord.GetValue<ulong>("GuildId");
    options.OddsBotChannelId = discord.GetValue<ulong>("OddsBotChannelId");
    options.OddsAlertsChannelId = discord.GetValue<ulong>("OddsAlertsChannelId");
    options.SharpSignalsChannelId = discord.GetValue<ulong>("SharpSignalsChannelId");
    options.StarterRoleId = discord.GetValue<ulong>("StarterRoleId");
    options.CoreRoleId = discord.GetValue<ulong>("CoreRoleId");
    options.SharpRoleId = discord.GetValue<ulong>("SharpRoleId");
});

// ============================================
// SERVICE REGISTRATION ORDER (NO CYCLES)
// ============================================

// 1. Core infrastructure (no dependencies)
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<EnhancedCacheService>();
builder.Services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());
builder.Services.AddSingleton<IEnhancedCacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());

// 2. Named HttpClients
builder.Services.AddHttpClient("OddsApi", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
builder.Services.AddHttpClient("ChartService");

// 4. OddsApiClient (simple - only HttpClient, apiKey, logger)
builder.Services.AddSingleton<IOddsApiClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("OddsApi");
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = config["AppSettings:TheOddsApiKey"]
        ?? throw new InvalidOperationException("TheOddsApiKey not configured");
    var logger = sp.GetRequiredService<ILogger<OddsApiClient>>();

    return new OddsApiClient(httpClient, apiKey, logger);
});

// 6. Chart Service
builder.Services.AddSingleton<IChartService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("ChartService");
    var logger = sp.GetRequiredService<ILogger<QuickChartService>>();
    return new QuickChartService(httpClient, logger);
});

// 7. Anthropic Client
builder.Services.AddSingleton(_ =>
{
    var apiKey = builder.Configuration["AppSettings:ClaudeApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);
    }
    return new AnthropicClient();
});

// 8. Sports Data Service
builder.Services.AddSingleton<ISportsDataService, SportsDataService>();

// 9. OddsService (depends on OddsApiClient, SportsDataService, MarketAccessService)
builder.Services.AddSingleton<IOddsService>(sp =>
{
    var oddsClient = sp.GetRequiredService<IOddsApiClient>();
    var sportsDataService = sp.GetRequiredService<ISportsDataService>();
    var marketRepository = sp.GetRequiredService<IMarketRepository>();
    var logger = sp.GetRequiredService<ILogger<OddsService>>();

    return new OddsService(oddsClient, sportsDataService, marketRepository, logger);
});

// 10. Claude and Query parsing services
builder.Services.AddSingleton<IClaudeService, ClaudeService>();
builder.Services.AddSingleton<IQueryParser, LocalQueryParser>();

// 11. Orchestrator
builder.Services.AddSingleton<IOddsOrchestrator>(sp =>
{
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var localParser = sp.GetRequiredService<IQueryParser>();
    var claudeService = sp.GetRequiredService<IClaudeService>();
    var oddsService = sp.GetRequiredService<IOddsService>();
    var chartService = sp.GetRequiredService<IChartService>();
    var cacheService = sp.GetRequiredService<IEnhancedCacheService>();
    var marketRepository = sp.GetRequiredService<IMarketRepository>();
    var logger = sp.GetRequiredService<ILogger<OddsOrchestrator>>();
    return new OddsOrchestrator(scopeFactory, localParser, claudeService, oddsService, chartService, cacheService, marketRepository, logger);
});

// 12. Alert and tracking services
builder.Services.AddSingleton<IMovementFingerprintService, MovementFingerprintService>();
builder.Services.AddSingleton<IConfidenceScoringEngine, ConfidenceScoringEngine>();
builder.Services.AddSingleton<IAlertEngine, AlertEngine>();
builder.Services.AddSingleton<IHistoricalTracker, HistoricalTracker>();

// 13. Subscription services
builder.Services.AddSingleton<ISubscriptionManager, SubscriptionManager>();

// 14. Discord Bot
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

var host = builder.Build();

Console.WriteLine("Starting OddsTracker Bot...");
Console.WriteLine("Press Ctrl+C to stop.");

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Host error: {ex}");
    throw;
}