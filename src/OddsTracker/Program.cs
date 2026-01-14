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

    // DefaultAzureCredential tries multiple auth methods in order:
    // 1. Environment variables (disabled for local dev)
    // 2. Workload Identity (disabled for local dev)
    // 3. Managed Identity (works in Azure)
    // 4. Azure CLI (works locally after 'az login')
    // 5. Visual Studio / VS Code credentials
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential = true,
        ExcludeWorkloadIdentityCredential = true,
        TenantId = builder.Configuration["Azure:TenantId"] // Optional: specify tenant
    });

    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl), credential);
    Console.WriteLine("Successfully connected to Key Vault");

    // Load environment-specific connection string
    // Key Vault has: ConnectionStrings--DefaultConnection--Dev and ConnectionStrings--DefaultConnection--Prod
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

    // Debug: Check if secrets loaded
    var discordToken = builder.Configuration["AppSettings:DiscordToken"];
    var claudeKey = builder.Configuration["AppSettings:ClaudeApiKey"];
    var oddsKey = builder.Configuration["AppSettings:TheOddsApiKey"];
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
    Console.WriteLine($"DiscordToken loaded: {!string.IsNullOrEmpty(discordToken)}");
    Console.WriteLine($"ClaudeApiKey loaded: {!string.IsNullOrEmpty(claudeKey)}");
    Console.WriteLine($"TheOddsApiKey loaded: {!string.IsNullOrEmpty(oddsKey)}");
    Console.WriteLine($"ConnectionString loaded: {!string.IsNullOrEmpty(connStr)}");
}

// Redis Cache
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

// EF Core - Azure SQL Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContextPool<OddsTrackerDbContext>(options =>
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            // Azure SQL resiliency - retries on transient failures
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);

            // Command timeout for Azure
            sqlOptions.CommandTimeout(30);
        });

        // Use Managed Identity in Production only
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
}
else
{
    // Fallback to in-memory for development without DB
    builder.Services.AddSingleton<IHistoricalRepository, InMemoryHistoricalRepository>();
    Console.WriteLine("No connection string - using in-memory repository");
}

// The Odds API Client
builder.Services.AddHttpClient<IOddsApiClient, OddsApiClient>((sp, client) =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).AddTypedClient<IOddsApiClient>((httpClient, sp) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = config["AppSettings:TheOddsApiKey"]
        ?? throw new InvalidOperationException("TheOddsApiKey not configured");
    return new OddsApiClient(httpClient, apiKey);
});

// Chart Service
builder.Services.AddHttpClient<IChartService, QuickChartService>();

// Anthropic Client - uses ANTHROPIC_API_KEY env var by default, or configure explicitly
builder.Services.AddSingleton(_ =>
{
    var apiKey = builder.Configuration["AppSettings:ClaudeApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);
    }
    return new AnthropicClient();
});

// Configure options
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

// Services - EnhancedCacheService implements both ICacheService and IEnhancedCacheService
builder.Services.AddSingleton<EnhancedCacheService>();
builder.Services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());
builder.Services.AddSingleton<IEnhancedCacheService>(sp => sp.GetRequiredService<EnhancedCacheService>());

builder.Services.AddSingleton<IClaudeService, ClaudeService>();
builder.Services.AddSingleton<ISportsDataService, SportsDataService>();
builder.Services.AddSingleton<IQueryParser, LocalQueryParser>();
builder.Services.AddSingleton<IOddsService, OddsService>();
builder.Services.AddSingleton<IOddsOrchestrator, OddsOrchestrator>();

// New platform services
builder.Services.AddSingleton<IMovementFingerprintService, MovementFingerprintService>();
builder.Services.AddSingleton<IConfidenceScoringEngine, ConfidenceScoringEngine>();
builder.Services.AddSingleton<IAlertEngine, AlertEngine>();
builder.Services.AddSingleton<IHistoricalTracker, HistoricalTracker>();
builder.Services.AddSingleton<ISubscriptionManager, SubscriptionManager>();
builder.Services.AddScoped<ISubscriptionRepository, EfSubscriptionRepository>();
builder.Services.AddSingleton<IMarketAccessService, MarketAccessService>();

// Discord Bot (runs as hosted service for user interaction only)
// Alert sending is now handled by Azure Functions via webhooks
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());

// NOTE: OddsPollerService and OutcomeUpdateService have been moved to Azure Functions
// See: src/OddsTracker.Functions/
// - OddsPollerFunction (Timer: every 60s)
// - OutcomeUpdateFunction (Timer: every 15min)

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

var host = builder.Build();

Console.WriteLine("Starting OddsTracker Bot...");
Console.WriteLine("Press Ctrl+C to stop.");

await host.RunAsync();