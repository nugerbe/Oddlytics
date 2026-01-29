using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Text.RegularExpressions;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Local regex-based query parser for common patterns.
    /// Uses ISportsDataService for team aliases.
    /// Falls back to Claude for complex/ambiguous queries.
    /// </summary>
    public partial class QueryParser(
        IServiceScopeFactory scopeFactory,
        IEnhancedCacheService cache,
        ISportsDataService sportsDataService,
        ILogger<QueryParser> logger) : IQueryParser
    {
        public async Task<OddsQueryBase?> TryParseAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return null;

            var input = userMessage.ToLowerInvariant().Trim();

            // 1. Extract sport (default to NFL)
            var sport = await GetSportByKeywordAsync(input) ?? throw new Exception("Could not parse sport");

            // 2. Extract market (scoped to sport, default to spreads)
            var market = await GetMarketByKeywordAsync(input, sport.Key) ?? throw new Exception("Could not parse market");

            var daysBack = ExtractDaysBack(input);

            logger.LogDebug("Parsed: Sport={Sport}, Market={Market}, Days={Days}",
                sport.Key, market.Key, daysBack);

            // Check if this is a player prop query
            if (market.IsPlayerProp)
            {
                var playerName = ExtractPlayerName(input)
                    ?? throw new Exception("Could not parse player name");

                var playerInfo = await sportsDataService.GetPlayerTeamAsync(sport.Key, playerName);

                logger.LogDebug("Parsed player prop locally: Player={Player}, Market={Market}, Days={Days}",
                    playerName, market.DisplayName, daysBack);

                return new PlayerOddsQuery
                {
                    MarketDefinition = market,
                    Sport = sport.Key,
                    DaysBack = daysBack,
                    PlayerName = playerInfo?.PlayerName ?? playerName,
                    Team = playerInfo?.TeamFullName
                };
            }

            // Game odds query
            var teamAliases = await sportsDataService.GetTeamAliasesAsync(sport.Key);
            var (homeTeam, awayTeam) = ExtractTeams(input, teamAliases);

            if (homeTeam is null)
            {
                logger.LogDebug("No team found in query: {Query}", userMessage);
                return null;
            }

            return new GameOddsQuery
            {
                MarketDefinition = market,
                Sport = sport.Key,
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                DaysBack = daysBack
            };
        }

        private static string? ExtractPlayerName(string input)
        {
            var playerMatch = PlayerNamePattern().Match(input);
            if (playerMatch.Success)
            {
                return playerMatch.Groups[1].Value.Trim();
            }
            return null;
        }

        private static (string? homeTeam, string? awayTeam) ExtractTeams(
            string input,
            Dictionary<string, string> teamAliases)
        {
            var vsMatch = VsPattern().Match(input);
            if (vsMatch.Success)
            {
                var team1 = ResolveTeam(vsMatch.Groups[1].Value.Trim(), teamAliases);
                var team2 = ResolveTeam(vsMatch.Groups[2].Value.Trim(), teamAliases);

                if (team1 is not null && team2 is not null)
                {
                    return (team2, team1); // home, away
                }
            }

            foreach (var (alias, fullName) in teamAliases)
            {
                if (Regex.IsMatch(input, $@"\b{Regex.Escape(alias)}\b", RegexOptions.IgnoreCase))
                {
                    return (fullName, null);
                }
            }

            return (null, null);
        }

        private static string? ResolveTeam(string input, Dictionary<string, string> teamAliases)
        {
            if (teamAliases.TryGetValue(input, out var team))
                return team;

            foreach (var (alias, fullName) in teamAliases)
            {
                if (Regex.IsMatch(input, $@"\b{Regex.Escape(alias)}\b", RegexOptions.IgnoreCase))
                    return fullName;
            }

            return null;
        }

        private static int ExtractDaysBack(string input)
        {
            var daysMatch = DaysPattern().Match(input);
            if (daysMatch.Success && int.TryParse(daysMatch.Groups[1].Value, out var days))
            {
                return Math.Clamp(days, 1, 14);
            }

            if (input.Contains("week", StringComparison.OrdinalIgnoreCase))
                return 7;

            if (input.Contains("month", StringComparison.OrdinalIgnoreCase))
                return 14;

            return 3;
        }

        [GeneratedRegex(@"(\w+(?:\s+\w+)?)\s+(?:at|vs\.?|@|versus|against)\s+(\w+(?:\s+\w+)?)", RegexOptions.IgnoreCase)]
        private static partial Regex VsPattern();

        [GeneratedRegex(@"(?:last|past)\s+(\d+)\s*days?", RegexOptions.IgnoreCase)]
        private static partial Regex DaysPattern();

        [GeneratedRegex(@"([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\s+(?:passing|rushing|receiving|yards|tds|receptions)", RegexOptions.IgnoreCase)]
        private static partial Regex PlayerNamePattern();

        #region Cached Repository Access

        private async Task<Sport?> GetSportByKeywordAsync(string input)
        {
            // Sports list rarely changes — cache it
            const string cacheKey = "sports:all";

            var sports = await cache.GetAsync<List<Sport>>(cacheKey);
            if (sports is null)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

                sports = await repo.GetAllSportsAsync();
                await cache.SetAsync(cacheKey, sports, TimeSpan.FromHours(1));
            }

            // Match keyword in input
            foreach (var sport in sports)
            {
                if (input.Contains(sport.Key, StringComparison.OrdinalIgnoreCase) ||
                    sport.Keywords.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return sport;
                }
            }

            return null;
        }

        private async Task<MarketDefinition?> GetMarketByKeywordAsync(string input, string sportKey)
        {
            var cacheKey = $"markets:bysport:{sportKey}";

            var markets = await cache.GetAsync<List<MarketDefinition>>(cacheKey);
            if (markets is null)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

                markets = await repo.GetMarketsForSportAsync(sportKey);
                await cache.SetAsync(cacheKey, markets, TimeSpan.FromHours(1));
            }

            // Match keyword in input
            foreach (var market in markets)
            {
                if (market.Keywords.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return market;
                }
            }

            // Default to spreads
            return markets.FirstOrDefault(m => m.Key.Contains("spread")) ?? markets.FirstOrDefault();
        }

        #endregion
    }
}