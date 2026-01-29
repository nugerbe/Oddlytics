using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services.SportClients
{
    /// <summary>
    /// Factory for creating and caching sport-specific API clients.
    /// Loads sport aliases from OddsTracker db and checks active status from Sportsdata db.
    /// </summary>
    public class SportClientFactory(
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        ILogger<SportClientFactory> logger) : ISportClientFactory
    {
        private readonly string _apiKey = config["AppSettings:SportsDataApiKey"] ?? throw new InvalidOperationException("SportsDataApiKey not configured");
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
        private readonly ILogger<SportClientFactory> _logger = logger;
        private readonly Dictionary<string, ISportClient> _clients = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _lock = new();

        // Cached sport data from database
        private Dictionary<string, string>? _sportAliases; // keyword -> sportKey
        private Dictionary<string, string>? _sportsdataCodes; // sportKey -> SportsdataCode (e.g., "NFL")
        private HashSet<string>? _supportedSportKeys;
        private bool _initialized;

        /// <summary>
        /// Initialize sport mappings from the database.
        /// Call this during application startup.
        /// </summary>
        /// <param name="checkSportsdataActive">Optional callback to check if sport is active in Sportsdata db (by sport code like "NFL")</param>
        public async Task InitializeAsync(Func<string, Task<bool>>? checkSportsdataActive = null)
        {
            if (_initialized)
                return;

            lock (_lock)
            {
                if (_initialized)
                    return;
            }

            try
            {
                _logger.LogInformation("Initializing SportClientFactory from database...");

                // Load sport aliases from OddsTracker db
                using var scope = _scopeFactory.CreateScope();
                var marketRepository = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

                var sports = await marketRepository.GetAllSportsAsync();
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var sportsdataCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var supportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var sport in sports.Where(s => s.IsActive && !string.IsNullOrEmpty(s.SportsdataCode)))
                {
                    // Add the main key
                    aliases[sport.Key] = sport.Key;
                    sportsdataCodes[sport.Key] = sport.SportsdataCode!;
                    supportedKeys.Add(sport.Key);

                    // Add all keywords as aliases
                    foreach (var keyword in sport.Keywords)
                    {
                        aliases.TryAdd(keyword, sport.Key);
                    }

                    _logger.LogDebug("Loaded sport {Key} (SportsdataCode: {Code}) with {Count} keywords",
                        sport.Key, sport.SportsdataCode, sport.Keywords.Count);
                }

                // Optionally check Sportsdata db for active sports
                if (checkSportsdataActive != null)
                {
                    var keysToRemove = new List<string>();
                    foreach (var sportKey in supportedKeys)
                    {
                        if (!sportsdataCodes.TryGetValue(sportKey, out var sportsdataCode))
                            continue;

                        var isActive = await checkSportsdataActive(sportsdataCode);
                        if (!isActive)
                        {
                            _logger.LogInformation("Sport {SportKey} is not active in Sportsdata db", sportKey);
                            keysToRemove.Add(sportKey);
                        }
                    }

                    foreach (var key in keysToRemove)
                    {
                        supportedKeys.Remove(key);
                        sportsdataCodes.Remove(key);
                        // Remove aliases pointing to this sport
                        var aliasesToRemove = aliases.Where(kvp => kvp.Value == key).Select(kvp => kvp.Key).ToList();
                        foreach (var alias in aliasesToRemove)
                            aliases.Remove(alias);
                    }
                }

                lock (_lock)
                {
                    _sportAliases = aliases;
                    _sportsdataCodes = sportsdataCodes;
                    _supportedSportKeys = supportedKeys;
                    _initialized = true;
                }

                _logger.LogInformation(
                    "SportClientFactory initialized with {SportCount} sports and {AliasCount} aliases",
                    supportedKeys.Count, aliases.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize SportClientFactory from database");
                throw;
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException("SportClientFactory has not been initialized. Call InitializeAsync() during application startup.");
        }

        public ISportClient? GetClient(string sportKey)
        {
            if (string.IsNullOrWhiteSpace(sportKey))
                return null;

            EnsureInitialized();

            // Resolve the sport key from alias
            if (!_sportAliases!.TryGetValue(sportKey, out var resolvedKey))
            {
                _logger.LogDebug("Unknown sport key: {SportKey}", sportKey);
                return null;
            }

            // Check if supported
            if (!_supportedSportKeys!.Contains(resolvedKey))
            {
                _logger.LogDebug("Sport not supported: {SportKey}", resolvedKey);
                return null;
            }

            // Get or create client
            lock (_lock)
            {
                if (_clients.TryGetValue(resolvedKey, out var existingClient))
                    return existingClient;

                var client = CreateClient(resolvedKey);
                if (client != null)
                {
                    _clients[resolvedKey] = client;
                }
                return client;
            }
        }

        public IEnumerable<string> GetSupportedSports()
        {
            EnsureInitialized();

            return [.. _supportedSportKeys!];
        }

        public bool IsSupported(string sportKey)
        {
            if (string.IsNullOrWhiteSpace(sportKey))
                return false;

            EnsureInitialized();

            // Check if the key itself or any alias resolves to a supported sport
            if (_sportAliases!.TryGetValue(sportKey, out var resolvedKey))
                return _supportedSportKeys!.Contains(resolvedKey);

            return false;
        }

        /// <summary>
        /// Resolve a keyword/alias to the canonical sport key
        /// </summary>
        public string? ResolveSportKey(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return null;

            EnsureInitialized();

            return _sportAliases!.TryGetValue(keyword, out var resolved) ? resolved : null;
        }

        private ISportClient? CreateClient(string sportKey)
        {
            // Get SportsdataCode from cached mapping
            if (!_sportsdataCodes!.TryGetValue(sportKey, out var sportsdataCode))
            {
                _logger.LogWarning("No SportsdataCode mapping for sport key: {SportKey}", sportKey);
                return null;
            }

            return sportsdataCode.ToUpperInvariant() switch
            {
                "NFL" => new NflClient(_apiKey),
                "NBA" => new NbaClient(_apiKey),
                "MLB" => new MlbClient(_apiKey),
                "NHL" => new NhlClient(_apiKey),
                _ => null
            };
        }
    }
}
