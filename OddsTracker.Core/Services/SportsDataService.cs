using FantasyData.Api.Client;
using FantasyData.Api.Client.Model.NFLv3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services
{
    public class SportsDataService : ISportsDataService
    {
        private readonly NFLv3RotoBallerPremiumNewsClient _articlesClient;
        private readonly NFLv3ScoresClient _scoresClient;
        private readonly IEnhancedCacheService _cache;
        private readonly ILogger<SportsDataService> _logger;

        private Dictionary<string, string>? _teamAliases;
        private Dictionary<string, PlayerTeamInfo>? _playerLookup;

        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;

        private const string TeamCacheKey = "sportsdata:teamaliases";
        private const string PlayerCacheKey = "sportsdata:players";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        public SportsDataService(
            IConfiguration config,
            IEnhancedCacheService cache,
            ILogger<SportsDataService> logger)
        {
            var apiKey = config["AppSettings:SportsDataApiKey"]
                ?? throw new InvalidOperationException("SportsDataApiKey not configured");

            _articlesClient = new NFLv3RotoBallerPremiumNewsClient(apiKey);
            _scoresClient = new NFLv3ScoresClient(apiKey);
            _cache = cache;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_initialized)
                    return;

                // Load team aliases
                _teamAliases = await GetTeamAliasesAsync();
                _logger.LogInformation("Loaded {Count} team aliases", _teamAliases.Count);

                // Load all players
                _playerLookup = await LoadAllPlayersAsync();
                _logger.LogInformation("Loaded {Count} player profiles", _playerLookup.Count);

                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<List<Score>> GetSeasonScores()
        {
            var currentSeason = await _scoresClient.GetSeasonCurrentAsync();
            return await _scoresClient.GetGamesBySeasonLiveFinalAsync(currentSeason.ToString());
        }

        public async Task<PlayerTeamInfo?> GetPlayerTeamAsync(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return null;

            // Ensure initialized
            if (_playerLookup is null)
            {
                await InitializeAsync();
            }

            if (_playerLookup is null)
            {
                _logger.LogWarning("Player lookup not initialized");
                return null;
            }

            // Normalize the search name
            var searchKey = NormalizePlayerName(playerName);

            // Direct match
            if (_playerLookup.TryGetValue(searchKey, out var player))
            {
                _logger.LogDebug("Found player {Player} on {Team}", player.PlayerName, player.TeamFullName);
                return player;
            }

            // Try partial match (last name only)
            var lastNameMatch = _playerLookup.Values
                .FirstOrDefault(p => p.PlayerName.Split(' ').LastOrDefault()
                    ?.Equals(playerName, StringComparison.OrdinalIgnoreCase) == true);

            if (lastNameMatch is not null)
            {
                _logger.LogDebug("Found player by last name: {Player} on {Team}",
                    lastNameMatch.PlayerName, lastNameMatch.TeamFullName);
                return lastNameMatch;
            }

            // Try contains match
            var containsMatch = _playerLookup.Values
                .FirstOrDefault(p => p.PlayerName.Contains(playerName, StringComparison.OrdinalIgnoreCase));

            if (containsMatch is not null)
            {
                _logger.LogDebug("Found player by partial match: {Player} on {Team}",
                    containsMatch.PlayerName, containsMatch.TeamFullName);
                return containsMatch;
            }

            _logger.LogWarning("Player not found: {Player}", playerName);
            return null;
        }

        private async Task<Dictionary<string, PlayerTeamInfo>> LoadAllPlayersAsync()
        {
            // Try Redis cache first
            var cached = await _cache.GetAsync<PlayerLookupCache>(PlayerCacheKey);
            if (cached is not null)
            {
                _logger.LogDebug("Player lookup loaded from cache");
                return cached.Players;
            }

            // Fetch from API
            _logger.LogInformation("Fetching all player profiles from SportsData API...");

            List<Player> players;
            try
            {
                players = await _scoresClient.GetPlayerDetailsAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch player profiles");
                return new Dictionary<string, PlayerTeamInfo>(StringComparer.OrdinalIgnoreCase);
            }

            _logger.LogInformation("Retrieved {Count} player profiles from API", players.Count);

            // Get team aliases for full name lookup
            var teamAliases = await GetTeamAliasesAsync();

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

                // Also add by short name if different (e.g., "Josh Allen" for "Joshua Allen")
                if (!string.IsNullOrEmpty(player.ShortName) && player.ShortName != player.Name)
                {
                    var shortKey = NormalizePlayerName(player.ShortName);
                    lookup.TryAdd(shortKey, info);
                }
            }

            // Cache the result
            await _cache.SetAsync(PlayerCacheKey, new PlayerLookupCache { Players = lookup }, CacheTtl);

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

        public async Task<Dictionary<string, string>> GetTeamAliasesAsync()
        {
            // Return cached in-memory if available
            if (_teamAliases is not null)
                return _teamAliases;

            // Try Redis cache
            var cached = await _cache.GetAsync<TeamAliasCache>(TeamCacheKey);
            if (cached is not null)
            {
                _logger.LogDebug("Team aliases loaded from cache");
                _teamAliases = cached.Aliases;
                return _teamAliases;
            }

            // Fetch from API
            _logger.LogInformation("Fetching team profiles from SportsData API");
            var teams = await _scoresClient.GetTeamProfilesAllAsync();

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

                AddCommonAliases(aliases, team, fullName);
            }

            // Cache the result
            await _cache.SetAsync(TeamCacheKey, new TeamAliasCache { Aliases = aliases }, CacheTtl);
            _teamAliases = aliases;

            _logger.LogInformation("Loaded {Count} team aliases from API", aliases.Count);
            return aliases;
        }

        public async Task<List<News>> GetArticlesByTeam(string team)
        {
            return await _articlesClient.GetPremiumNewsByTeamAsync(team);
        }

        public async Task<List<News>> GetArticlesByPlayer(int playerId)
        {
            return await _articlesClient.GetPremiumNewsByPlayerAsync(playerId);
        }

        private static void AddCommonAliases(Dictionary<string, string> aliases, Team team, string fullName)
        {
            var commonAliases = team.Key?.ToUpperInvariant() switch
            {
                "NE" => ["pats", "new england"],
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

            foreach (var alias in commonAliases)
            {
                aliases.TryAdd(alias, fullName);
            }
        }
    }

    public class TeamAliasCache
    {
        public Dictionary<string, string> Aliases { get; set; } = new();
    }

    public class PlayerLookupCache
    {
        public Dictionary<string, PlayerTeamInfo> Players { get; set; } = new();
    }
}