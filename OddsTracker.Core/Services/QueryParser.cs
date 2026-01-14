using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Text.RegularExpressions;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Local regex-based query parser for common patterns.
    /// Uses ISportsDataService for team aliases.
    /// Falls back to Claude for complex/ambiguous queries.
    /// </summary>
    public partial class LocalQueryParser : IQueryParser
    {
        private readonly ISportsDataService _sportsDataService;
        private readonly ILogger<LocalQueryParser> _logger;

        private static readonly string[] SpreadKeywords = ["spread", "line", "point spread", "points", "ats", "against the spread"];
        private static readonly string[] MoneylineKeywords = ["moneyline", "money line", "ml", "to win", "outright"];
        private static readonly string[] TotalKeywords = ["total", "over", "under", "o/u", "over/under", "points total"];

        public LocalQueryParser(ISportsDataService sportsDataService, ILogger<LocalQueryParser> logger)
        {
            _sportsDataService = sportsDataService;
            _logger = logger;
        }

        public async Task<OddsQuery?> TryParseAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return null;

            var input = userMessage.ToLowerInvariant().Trim();
            var teamAliases = await _sportsDataService.GetTeamAliasesAsync();

            // Extract teams
            var (homeTeam, awayTeam) = ExtractTeams(input, teamAliases);
            if (homeTeam is null)
            {
                _logger.LogDebug("No team found in query: {Query}", userMessage);
                return null;
            }

            // Extract market type
            var marketType = ExtractMarketType(input);

            // Extract days back
            var daysBack = ExtractDaysBack(input);

            _logger.LogDebug("Parsed locally: Home={Home}, Away={Away}, Market={Market}, Days={Days}",
                homeTeam, awayTeam ?? "none", marketType, daysBack);

            return new OddsQuery
            {
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                MarketType = marketType,
                DaysBack = daysBack
            };
        }

        private static (string? homeTeam, string? awayTeam) ExtractTeams(string input, Dictionary<string, string> teamAliases)
        {
            // Pattern: "team1 at/vs/@ team2" or "team1 versus team2"
            var vsMatch = VsPattern().Match(input);
            if (vsMatch.Success)
            {
                var team1 = ResolveTeam(vsMatch.Groups[1].Value.Trim(), teamAliases);
                var team2 = ResolveTeam(vsMatch.Groups[2].Value.Trim(), teamAliases);

                if (team1 is not null && team2 is not null)
                {
                    // "away at home" or "away vs home" - first team is typically away
                    return (team2, team1); // home, away
                }
            }

            // Single team pattern - find any team mention
            foreach (var (alias, fullName) in teamAliases)
            {
                // Check for whole word match to avoid partial matches
                if (Regex.IsMatch(input, $@"\b{Regex.Escape(alias)}\b", RegexOptions.IgnoreCase))
                {
                    return (fullName, null);
                }
            }

            return (null, null);
        }

        private static string? ResolveTeam(string input, Dictionary<string, string> teamAliases)
        {
            // Direct alias match
            if (teamAliases.TryGetValue(input, out var team))
                return team;

            // Check if input contains any alias (whole word)
            foreach (var (alias, fullName) in teamAliases)
            {
                if (Regex.IsMatch(input, $@"\b{Regex.Escape(alias)}\b", RegexOptions.IgnoreCase))
                    return fullName;
            }

            return null;
        }

        private static MarketType ExtractMarketType(string input)
        {
            foreach (var keyword in TotalKeywords)
            {
                if (input.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return MarketType.Total;
            }

            foreach (var keyword in MoneylineKeywords)
            {
                if (input.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return MarketType.Moneyline;
            }

            foreach (var keyword in SpreadKeywords)
            {
                if (input.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return MarketType.Spread;
            }

            // Default to spread (most common)
            return MarketType.Spread;
        }

        private static int ExtractDaysBack(string input)
        {
            // "last X days" pattern
            var daysMatch = DaysPattern().Match(input);
            if (daysMatch.Success && int.TryParse(daysMatch.Groups[1].Value, out var days))
            {
                return Math.Clamp(days, 1, 14);
            }

            // "past week" = 7 days
            if (input.Contains("week", StringComparison.OrdinalIgnoreCase))
                return 7;

            // "past month" = 14 days (cap)
            if (input.Contains("month", StringComparison.OrdinalIgnoreCase))
                return 14;

            // Default
            return 3;
        }

        [GeneratedRegex(@"(\w+(?:\s+\w+)?)\s+(?:at|vs\.?|@|versus|against)\s+(\w+(?:\s+\w+)?)", RegexOptions.IgnoreCase)]
        private static partial Regex VsPattern();

        [GeneratedRegex(@"(?:last|past)\s+(\d+)\s*days?", RegexOptions.IgnoreCase)]
        private static partial Regex DaysPattern();
    }
}