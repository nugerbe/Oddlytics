using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Collections.Concurrent;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Sport-agnostic service for retrieving sports data using the ISportClientFactory
    /// </summary>
    public class SportsDataService(
        ISportClientFactory clientFactory,
        IEnhancedCacheService cache,
        ILogger<SportsDataService> logger) : ISportsDataService
    {
        private readonly ISportClientFactory _clientFactory = clientFactory;
        private readonly IEnhancedCacheService _cache = cache;
        private readonly ILogger<SportsDataService> _logger = logger;

        // Per-sport caches
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _teamAliasesCache = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, PlayerTeamInfo>> _playerLookupCache = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _initLocks = new();
        private readonly ConcurrentDictionary<string, bool> _initialized = new();

        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        public bool IsSportSupported(string sportKey) => _clientFactory.IsSupported(sportKey);

        public IEnumerable<string> GetSupportedSports() => _clientFactory.GetSupportedSports();

        public async Task InitializeAsync(string sportKey)
        {
            var client = GetClientOrThrow(sportKey);
            var normalizedKey = client.SportKey;

            if (_initialized.GetValueOrDefault(normalizedKey))
                return;

            var initLock = _initLocks.GetOrAdd(normalizedKey, _ => new SemaphoreSlim(1, 1));

            await initLock.WaitAsync();
            try
            {
                if (_initialized.GetValueOrDefault(normalizedKey))
                    return;

                _logger.LogInformation("Initializing SportsDataService for {Sport}...", normalizedKey);

                // Load team aliases
                var teamAliases = await GetTeamAliasesAsync(sportKey);
                _logger.LogInformation("Loaded {Count} team aliases for {Sport}", teamAliases.Count, normalizedKey);

                // Load all players
                var playerLookup = await LoadAllPlayersAsync(client);
                _playerLookupCache[normalizedKey] = playerLookup;
                _logger.LogInformation("Loaded {Count} player profiles for {Sport}", playerLookup.Count, normalizedKey);

                _initialized[normalizedKey] = true;
            }
            finally
            {
                initLock.Release();
            }
        }

        public async Task<List<GameInfo>> GetSeasonGamesAsync(string sportKey)
        {
            var client = GetClientOrThrow(sportKey);
            return await client.GetCurrentSeasonGamesAsync();
        }

        public async Task<List<StadiumInfo>> GetStadiumsAsync(string sportKey)
        {
            var client = GetClientOrThrow(sportKey);
            var cacheKey = $"sportsdata:{client.SportKey}:stadiums";

            var cached = await _cache.GetAsync<List<StadiumInfo>>(cacheKey);
            if (cached is not null)
            {
                _logger.LogDebug("Stadium lookup loaded from cache for {Sport}", client.SportKey);
                return cached;
            }

            _logger.LogInformation("Fetching stadiums for {Sport} from SportsData API...", client.SportKey);

            try
            {
                var stadiums = await client.GetStadiumsAsync();
                await _cache.SetAsync(cacheKey, stadiums, CacheTtl);
                _logger.LogInformation("Retrieved {Count} stadiums for {Sport}", stadiums.Count, client.SportKey);
                return stadiums;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch stadiums for {Sport}", client.SportKey);
                return [];
            }
        }

        public async Task<List<TeamInfo>> GetTeamsAsync(string sportKey)
        {
            var client = GetClientOrThrow(sportKey);
            var cacheKey = $"sportsdata:{client.SportKey}:teams";

            var cached = await _cache.GetAsync<List<TeamInfo>>(cacheKey);
            if (cached is not null)
            {
                _logger.LogDebug("Teams loaded from cache for {Sport}", client.SportKey);
                return cached;
            }

            _logger.LogInformation("Fetching teams for {Sport} from SportsData API...", client.SportKey);

            try
            {
                var teams = await client.GetTeamsAsync();
                await _cache.SetAsync(cacheKey, teams, CacheTtl);
                _logger.LogInformation("Retrieved {Count} teams for {Sport}", teams.Count, client.SportKey);
                return teams;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch teams for {Sport}", client.SportKey);
                return [];
            }
        }

        public async Task<List<PlayerInfo>> GetPlayersAsync(string sportKey)
        {
            var client = GetClientOrThrow(sportKey);
            var cacheKey = $"sportsdata:{client.SportKey}:players";

            var cached = await _cache.GetAsync<List<PlayerInfo>>(cacheKey);
            if (cached is not null)
            {
                _logger.LogDebug("Players loaded from cache for {Sport}", client.SportKey);
                return cached;
            }

            _logger.LogInformation("Fetching players for {Sport} from SportsData API...", client.SportKey);

            try
            {
                var players = await client.GetPlayersAsync();
                await _cache.SetAsync(cacheKey, players, CacheTtl);
                _logger.LogInformation("Retrieved {Count} players for {Sport}", players.Count, client.SportKey);
                return players;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch players for {Sport}", client.SportKey);
                return [];
            }
        }

        public async Task<PlayerTeamInfo?> GetPlayerTeamAsync(string sportKey, string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return null;

            var client = GetClientOrThrow(sportKey);
            var normalizedKey = client.SportKey;

            // Ensure initialized
            if (!_playerLookupCache.ContainsKey(normalizedKey))
            {
                await InitializeAsync(sportKey);
            }

            if (!_playerLookupCache.TryGetValue(normalizedKey, out var playerLookup))
            {
                _logger.LogWarning("Player lookup not initialized for {Sport}", normalizedKey);
                return null;
            }

            // Normalize the search name
            var searchKey = NormalizePlayerName(playerName);

            // Direct match
            if (playerLookup.TryGetValue(searchKey, out var player))
            {
                _logger.LogDebug("Found player {Player} on {Team}", player.PlayerName, player.TeamFullName);
                return player;
            }

            // Try partial match (last name only)
            var lastNameMatch = playerLookup.Values
                .FirstOrDefault(p => p.PlayerName.Split(' ').LastOrDefault()
                    ?.Equals(playerName, StringComparison.OrdinalIgnoreCase) == true);

            if (lastNameMatch is not null)
            {
                _logger.LogDebug("Found player by last name: {Player} on {Team}",
                    lastNameMatch.PlayerName, lastNameMatch.TeamFullName);
                return lastNameMatch;
            }

            // Try contains match
            var containsMatch = playerLookup.Values.FirstOrDefault(p => p.PlayerName.Contains(playerName, StringComparison.OrdinalIgnoreCase));

            if (containsMatch is not null)
            {
                _logger.LogDebug("Found player by partial match: {Player} on {Team}",
                    containsMatch.PlayerName, containsMatch.TeamFullName);
                return containsMatch;
            }

            _logger.LogWarning("Player not found: {Player} in {Sport}", playerName, normalizedKey);
            return null;
        }

        public async Task<Dictionary<string, string>> GetTeamAliasesAsync(string sportKey)
        {
            var client = GetClientOrThrow(sportKey);
            var normalizedKey = client.SportKey;

            // Return cached in-memory if available
            if (_teamAliasesCache.TryGetValue(normalizedKey, out var cachedAliases))
                return cachedAliases;

            var cacheKey = $"sportsdata:{normalizedKey}:teamaliases";

            // Try Redis cache
            var cached = await _cache.GetAsync<TeamAliasCache>(cacheKey);
            if (cached is not null)
            {
                _logger.LogDebug("Team aliases loaded from cache for {Sport}", normalizedKey);
                _teamAliasesCache[normalizedKey] = cached.Aliases;
                return cached.Aliases;
            }

            // Fetch from API
            _logger.LogInformation("Fetching team profiles for {Sport} from SportsData API", normalizedKey);
            var teams = await client.GetTeamsAsync();

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var team in teams)
            {
                var fullName = team.FullName;
                if (string.IsNullOrEmpty(fullName))
                    continue;

                // Add various aliases for each team
                aliases.TryAdd(fullName, fullName);

                if (!string.IsNullOrEmpty(team.Key))
                    aliases.TryAdd(team.Key, fullName);

                if (!string.IsNullOrEmpty(team.Name))
                    aliases.TryAdd(team.Name, fullName);

                if (!string.IsNullOrEmpty(team.City))
                    aliases.TryAdd(team.City, fullName);

                // Add sport-specific common aliases
                AddCommonAliases(aliases, team, fullName, normalizedKey);
            }

            // Cache the result
            await _cache.SetAsync(cacheKey, new TeamAliasCache { Aliases = aliases }, CacheTtl);
            _teamAliasesCache[normalizedKey] = aliases;

            _logger.LogInformation("Loaded {Count} team aliases for {Sport}", aliases.Count, normalizedKey);
            return aliases;
        }

        private async Task<Dictionary<string, PlayerTeamInfo>> LoadAllPlayersAsync(ISportClient client)
        {
            var normalizedKey = client.SportKey;
            var cacheKey = $"sportsdata:{normalizedKey}:playerlookup";

            // Try Redis cache first
            var cached = await _cache.GetAsync<PlayerLookupCache>(cacheKey);
            if (cached is not null)
            {
                _logger.LogDebug("Player lookup loaded from cache for {Sport}", normalizedKey);
                return cached.Players;
            }

            // Fetch from API
            _logger.LogInformation("Fetching all player profiles for {Sport} from SportsData API...", normalizedKey);

            List<PlayerInfo> players;
            try
            {
                players = await client.GetPlayersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch player profiles for {Sport}", normalizedKey);
                return new Dictionary<string, PlayerTeamInfo>(StringComparer.OrdinalIgnoreCase);
            }

            _logger.LogInformation("Retrieved {Count} player profiles for {Sport}", players.Count, normalizedKey);

            // Get team aliases for full name lookup
            var teamAliases = await GetTeamAliasesAsync(normalizedKey);

            var lookup = new Dictionary<string, PlayerTeamInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var player in players)
            {
                if (string.IsNullOrEmpty(player.Name) || string.IsNullOrEmpty(player.Team))
                    continue;

                // Skip inactive/retired players
                if (player.Status != "Active")
                    continue;

                var teamFullName = teamAliases.GetValueOrDefault(player.Team, player.Team);

                var info = new PlayerTeamInfo
                {
                    PlayerName = player.Name,
                    TeamFullName = teamFullName,
                    TeamKey = player.Team,
                    Position = player.Position ?? ""
                };

                var key = NormalizePlayerName(player.Name);
                lookup.TryAdd(key, info);

                // Also add by short name if different
                if (!string.IsNullOrEmpty(player.ShortName) && player.ShortName != player.Name)
                {
                    var shortKey = NormalizePlayerName(player.ShortName);
                    lookup.TryAdd(shortKey, info);
                }
            }

            // Cache the result
            await _cache.SetAsync(cacheKey, new PlayerLookupCache { Players = lookup }, CacheTtl);

            return lookup;
        }

        private static string NormalizePlayerName(string name)
        {
            return name.ToLowerInvariant()
                .Replace(".", "")
                .Replace("'", "")
                .Replace("-", " ")
                .Trim();
        }

        private ISportClient GetClientOrThrow(string sportKey)
        {
            var client = _clientFactory.GetClient(sportKey);
            if (client is null)
            {
                throw new NotSupportedException($"Sport '{sportKey}' is not supported. Supported sports: {string.Join(", ", _clientFactory.GetSupportedSports())}");
            }
            return client;
        }

        private static void AddCommonAliases(Dictionary<string, string> aliases, TeamInfo team, string fullName, string sportKey)
        {
            // NFL-specific aliases
            if (sportKey == "americanfootball_nfl")
            {
                var nflAliases = team.Key?.ToUpperInvariant() switch
                {
                    "NE" => new[] { "pats", "new england" },
                    "KC" => new[] { "kc", "kansas city" },
                    "TB" => new[] { "bucs", "tampa", "tampa bay" },
                    "GB" => new[] { "green bay" },
                    "NO" => new[] { "new orleans" },
                    "LV" => new[] { "vegas", "las vegas" },
                    "LAC" => new[] { "la chargers" },
                    "LAR" => new[] { "la rams" },
                    "SF" => new[] { "niners", "sf", "san francisco" },
                    "JAX" => new[] { "jags" },
                    "IND" => new[] { "indy" },
                    "CIN" => new[] { "cincy" },
                    "PHI" => new[] { "philly" },
                    "NYG" => new[] { "ny giants" },
                    "NYJ" => new[] { "ny jets" },
                    _ => Array.Empty<string>()
                };

                foreach (var alias in nflAliases)
                    aliases.TryAdd(alias, fullName);
            }
            // NBA-specific aliases
            else if (sportKey == "basketball_nba")
            {
                var nbaAliases = team.Key?.ToUpperInvariant() switch
                {
                    "LAL" => new[] { "lakers", "la lakers" },
                    "LAC" => new[] { "clippers", "la clippers" },
                    "GSW" => new[] { "warriors", "golden state", "dubs" },
                    "BOS" => new[] { "celtics" },
                    "NYK" => new[] { "knicks" },
                    "BKN" => new[] { "nets", "brooklyn" },
                    "PHI" => new[] { "sixers", "76ers" },
                    "MIA" => new[] { "heat" },
                    "CHI" => new[] { "bulls" },
                    "CLE" => new[] { "cavs", "cavaliers" },
                    "DET" => new[] { "pistons" },
                    "MIL" => new[] { "bucks" },
                    "OKC" => new[] { "thunder" },
                    "DAL" => new[] { "mavs", "mavericks" },
                    "HOU" => new[] { "rockets" },
                    "SAS" => new[] { "spurs" },
                    "PHX" => new[] { "suns" },
                    "DEN" => new[] { "nuggets" },
                    "MIN" => new[] { "wolves", "timberwolves" },
                    _ => Array.Empty<string>()
                };

                foreach (var alias in nbaAliases)
                    aliases.TryAdd(alias, fullName);
            }
            // MLB-specific aliases
            else if (sportKey == "baseball_mlb")
            {
                var mlbAliases = team.Key?.ToUpperInvariant() switch
                {
                    "NYY" => new[] { "yankees", "bronx bombers" },
                    "NYM" => new[] { "mets" },
                    "BOS" => new[] { "red sox", "sox" },
                    "LAD" => new[] { "dodgers" },
                    "LAA" => new[] { "angels" },
                    "CHC" => new[] { "cubs", "cubbies" },
                    "CHW" => new[] { "white sox" },
                    "SF" => new[] { "giants" },
                    "HOU" => new[] { "astros", "stros" },
                    "ATL" => new[] { "braves" },
                    "PHI" => new[] { "phillies" },
                    "SD" => new[] { "padres" },
                    "SEA" => new[] { "mariners" },
                    "TEX" => new[] { "rangers" },
                    "STL" => new[] { "cardinals", "cards" },
                    _ => Array.Empty<string>()
                };

                foreach (var alias in mlbAliases)
                    aliases.TryAdd(alias, fullName);
            }
            // NHL-specific aliases
            else if (sportKey == "icehockey_nhl")
            {
                var nhlAliases = team.Key?.ToUpperInvariant() switch
                {
                    "TOR" => new[] { "leafs", "maple leafs" },
                    "MTL" => new[] { "habs", "canadiens" },
                    "BOS" => new[] { "bruins" },
                    "NYR" => new[] { "rangers" },
                    "NYI" => new[] { "islanders" },
                    "CHI" => new[] { "blackhawks", "hawks" },
                    "DET" => new[] { "red wings", "wings" },
                    "PIT" => new[] { "penguins", "pens" },
                    "PHI" => new[] { "flyers" },
                    "VGK" => new[] { "golden knights", "vegas" },
                    "SEA" => new[] { "kraken" },
                    "COL" => new[] { "avalanche", "avs" },
                    "TB" => new[] { "lightning", "bolts" },
                    "EDM" => new[] { "oilers" },
                    _ => Array.Empty<string>()
                };

                foreach (var alias in nhlAliases)
                    aliases.TryAdd(alias, fullName);
            }
        }
    }

    public class TeamAliasCache
    {
        public Dictionary<string, string> Aliases { get; set; } = [];
    }

    public class PlayerLookupCache
    {
        public Dictionary<string, PlayerTeamInfo> Players { get; set; } = [];
    }
}
