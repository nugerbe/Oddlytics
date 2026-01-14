using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace OddsTracker.Core.Services
{
    public class OddsOrchestrator(
        IQueryParser localParser,
        IClaudeService claudeService,
        IOddsService oddsService,
        IChartService chartService,
        IEnhancedCacheService cacheService,
        IMarketAccessService marketAccessService,
        ILogger<OddsOrchestrator> logger) : IOddsOrchestrator
    {
        public async Task<OddsQueryResult> ProcessQueryAsync(string userMessage, SubscriptionTier userTier)
        {
            logger.LogInformation("Processing query: {Query} for tier {Tier}", userMessage, userTier);

            // Step 1: Try local parser first (no Claude API call)
            var query = await localParser.TryParseAsync(userMessage);

            if (query is not null)
            {
                logger.LogInformation("Query parsed locally - no Claude API call needed");
            }
            else
            {
                // Fall back to Claude for complex queries
                logger.LogInformation("Local parser failed, falling back to Claude");
                query = await claudeService.ParseQueryAsync(userMessage);
            }

            if (query is null)
            {
                return new OddsQueryResult(
                    false,
                    null,
                    "I couldn't understand that query. Try something like:\n• 'Show me Chiefs spread movement'\n• 'Bills at Chiefs moneyline'\n• 'Eagles vs Cowboys total over the past week'",
                    null,
                    null
                );
            }

            // Check if user's tier can access this market
            if (!marketAccessService.CanAccessMarket(userTier, query.MarketType))
            {
                var requiredTier = query.MarketType.RequiredTier();
                return new OddsQueryResult(
                    false,
                    null,
                    $"The **{query.MarketType.ToDisplayName()}** market requires **{requiredTier}** tier or higher. " +
                    $"You're currently on **{userTier}** tier. Upgrade to access this market!",
                    query,
                    null
                );
            }

            logger.LogInformation(
                "Parsed query - HomeTeam: {HomeTeam}, AwayTeam: {AwayTeam}, Market: {Market}, Days: {Days}",
                query.HomeTeam, query.AwayTeam ?? "any", query.MarketType, query.DaysBack);

            // Step 2: Fetch odds data (with tier filtering)
            List<NormalizedOdds> odds;
            try
            {
                odds = await oddsService.GetOddsAsync(query, userTier);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "Error fetching odds from API");
                return new OddsQueryResult(
                    false,
                    null,
                    "Error fetching odds data. Please try again later.",
                    query,
                    null
                );
            }

            if (odds.Count == 0)
            {
                var teamDesc = query.AwayTeam is not null
                    ? $"{query.AwayTeam} at {query.HomeTeam}"
                    : query.HomeTeam;
                return new OddsQueryResult(
                    false,
                    null,
                    $"No upcoming games found for {teamDesc}. The team(s) might not have any scheduled games this week, or check the team name(s).",
                    query,
                    null
                );
            }

            var game = odds[0];
            var gameDesc = $"{game.AwayTeam} @ {game.HomeTeam} ({game.CommenceTime:MMM dd, h:mm tt})";

            // Step 3: Generate charts
            var charts = new List<ChartResult>();
            var generateBothTeams = query.AwayTeam is not null && query.MarketType != MarketType.Total;

            try
            {
                if (generateBothTeams)
                {
                    // Generate home team chart
                    var homeChart = await GetOrCreateChartAsync(odds, query, TeamSide.Home);
                    var homeAnalysis = await GetOrCreateAnalysisAsync(game, query, TeamSide.Home);
                    charts.Add(new ChartResult(
                        homeChart,
                        $"{game.HomeTeam} {query.MarketType}",
                        $"{game.HomeTeam} (Home)",
                        homeAnalysis
                    ));

                    // Generate away team chart
                    var awayChart = await GetOrCreateChartAsync(odds, query, TeamSide.Away);
                    var awayAnalysis = await GetOrCreateAnalysisAsync(game, query, TeamSide.Away);
                    charts.Add(new ChartResult(
                        awayChart,
                        $"{game.AwayTeam} {query.MarketType}",
                        $"{game.AwayTeam} (Away)",
                        awayAnalysis
                    ));
                }
                else
                {
                    // Single chart (totals or single team query)
                    var side = query.MarketType == MarketType.Total
                        ? TeamSide.Home
                        : IsTeamMatch(game.HomeTeam, query.HomeTeam) ? TeamSide.Home : TeamSide.Away;

                    var chartImage = await GetOrCreateChartAsync(odds, query, side);
                    var analysis = await GetOrCreateAnalysisAsync(game, query, side);

                    var title = query.MarketType == MarketType.Total
                        ? "Total (O/U)"
                        : $"{query.HomeTeam} {query.MarketType}";

                    charts.Add(new ChartResult(chartImage, title, gameDesc, analysis));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating chart");
                return new OddsQueryResult(
                    false,
                    null,
                    "Error generating chart. Please try again.",
                    query,
                    null
                );
            }

            return new OddsQueryResult(true, charts, null, query, gameDesc);
        }

        private async Task<byte[]> GetOrCreateChartAsync(List<NormalizedOdds> odds, OddsQuery query, TeamSide side)
        {
            var cacheKey = $"chart:{query.CacheKey}:{side}";
            var cached = await cacheService.GetBytesAsync(cacheKey);

            if (cached is not null)
            {
                logger.LogDebug("Chart cache hit: {Key}", cacheKey);
                return cached;
            }

            logger.LogDebug("Chart cache miss: {Key}", cacheKey);
            var chart = await chartService.GenerateChartAsync(odds, query, side);
            await cacheService.SetBytesAsync(cacheKey, chart, TimeSpan.FromMinutes(5));

            return chart;
        }

        private async Task<string?> GetOrCreateAnalysisAsync(NormalizedOdds game, OddsQuery query, TeamSide side)
        {
            // Create a hash of the analysis prompt for caching
            var promptKey = $"{game.EventId}:{query.MarketType}:{side}:{game.Snapshots.Count}";
            var promptHash = ComputeHash(promptKey);

            var cached = await cacheService.GetAIExplanationAsync(promptHash);
            if (cached is not null)
            {
                logger.LogDebug("Analysis cache hit: {Key}", promptHash);
                return cached;
            }

            logger.LogDebug("Analysis cache miss: {Key}", promptHash);
            var analysis = await claudeService.AnalyzeOddsMovementAsync(game, query, side);

            if (!string.IsNullOrEmpty(analysis))
            {
                await cacheService.SetAIExplanationAsync(promptHash, analysis);
            }

            return analysis;
        }

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes)[..16];
        }

        private static bool IsTeamMatch(string apiTeamName, string queryTeamName)
        {
            var api = apiTeamName.ToLowerInvariant().Trim();
            var query = queryTeamName.ToLowerInvariant().Trim();

            return api == query || api.Contains(query) || query.Contains(api);
        }
    }
}