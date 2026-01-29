using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace OddsTracker.Core.Services;

public class OddsOrchestrator(
    IServiceScopeFactory scopeFactory,
    IQueryParser localParser,
    IClaudeService claudeService,
    IOddsService oddsService,
    IChartService chartService,
    IEnhancedCacheService cacheService,
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
                "I couldn't understand that query. Try something like:\n" +
                "• 'Show me Chiefs spread movement'\n" +
                "• 'Bills at Chiefs moneyline'\n" +
                "• 'Eagles vs Cowboys total over the past week'",
                null,
                null
            );
        }

        var market = query.MarketDefinition;

        LogQueryDetails(query);

        // Step 2: Fetch odds (OddsService handles market access check internally)
        List<OddsBase> odds;
        try
        {
            odds = await oddsService.GetOddsAsync(query, userTier);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Error fetching odds from API");
            return new OddsQueryResult(false, null, "Error fetching odds data. Please try again later.", query, null);
        }

        if (odds.Count == 0)
        {
            var desc = GetQueryDescription(query);

            // Check if this might be a tier access issue
            if (market.RequiredTier > userTier)
            {
                return new OddsQueryResult(
                    false, null,
                    $"The **{market.DisplayName}** market requires **{market.RequiredTier}** tier or higher. " +
                    $"You're currently on **{userTier}** tier. Upgrade to access this market!",
                    query, null);
            }

            return new OddsQueryResult(false, null, $"No upcoming data found for {desc}.", query, null);
        }

        var oddsData = odds[0];
        var gameDesc = oddsData.Description;

        // Step 3: Generate charts
        var charts = new List<ChartResult>();

        try
        {
            charts = await GenerateChartsAsync(odds, query, oddsData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating chart");
            return new OddsQueryResult(false, null, "Error generating chart. Please try again.", query, null);
        }

        return new OddsQueryResult(true, charts, null, query, gameDesc);
    }

    private async Task<List<ChartResult>> GenerateChartsAsync(List<OddsBase> odds, OddsQueryBase query, OddsBase oddsData)
    {
        var charts = new List<ChartResult>();
        var market = query.MarketDefinition;

        switch (oddsData)
        {
            case PlayerOdds player:
                var playerChart = await GetOrCreateChartAsync(odds, query, TeamSide.Home);
                var playerAnalysis = await GetOrCreateAnalysisAsync(player, query, TeamSide.Home);
                charts.Add(new ChartResult(playerChart, player.Description, player.Description, playerAnalysis));
                break;

            case GameOdds game when query is GameOddsQuery gameQuery:
                var isTotal = market.OutcomeType == OutcomeType.OverUnder && !market.IsPlayerProp;
                var generateBothTeams = gameQuery.AwayTeam is not null && !isTotal;

                if (generateBothTeams)
                {
                    var homeChart = await GetOrCreateChartAsync(odds, query, TeamSide.Home);
                    var homeAnalysis = await GetOrCreateAnalysisAsync(game, query, TeamSide.Home);
                    charts.Add(new ChartResult(
                        homeChart,
                        $"{game.HomeTeam} {market.DisplayName}",
                        $"{game.HomeTeam} (Home)",
                        homeAnalysis));

                    var awayChart = await GetOrCreateChartAsync(odds, query, TeamSide.Away);
                    var awayAnalysis = await GetOrCreateAnalysisAsync(game, query, TeamSide.Away);
                    charts.Add(new ChartResult(
                        awayChart,
                        $"{game.AwayTeam} {market.DisplayName}",
                        $"{game.AwayTeam} (Away)",
                        awayAnalysis));
                }
                else
                {
                    var side = isTotal
                        ? TeamSide.Home
                        : IsTeamMatch(game.HomeTeam, gameQuery.HomeTeam) ? TeamSide.Home : TeamSide.Away;

                    var chartImage = await GetOrCreateChartAsync(odds, query, side);
                    var analysis = await GetOrCreateAnalysisAsync(game, query, side);
                    charts.Add(new ChartResult(chartImage, game.Description, game.Description, analysis));
                }
                break;
        }

        return charts;
    }

    private async Task<List<SignalSnapshot>> GetSignalsCachedAsync(string eventId, string marketKey)
    {
        var cacheKey = $"signals:{eventId}:{marketKey}";

        var cached = await cacheService.GetAsync<List<SignalSnapshot>>(cacheKey);
        if (cached is not null)
            return cached;

        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IHistoricalRepository>();

        var signals = await repo.GetSignalsForEventAsync(eventId, marketKey);

        if (signals.Count > 0)
            await cacheService.SetAsync(cacheKey, signals, TimeSpan.FromMinutes(15));

        return signals;
    }

    private async Task SaveSignalAndInvalidateCacheAsync(SignalSnapshot signal)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IHistoricalRepository>();

        await repo.SaveSignalAsync(signal);

        await cacheService.RemoveAsync($"signals:{signal.EventId}:{signal.MarketKey}");
    }

    private void LogQueryDetails(OddsQueryBase query)
    {
        var market = query.MarketDefinition;

        switch (query)
        {
            case GameOddsQuery gameQuery:
                logger.LogInformation(
                    "Parsed game query - Home: {Home}, Away: {Away}, Market: {Market}, Sport: {Sport}, Days: {Days}",
                    gameQuery.HomeTeam,
                    gameQuery.AwayTeam ?? "any",
                    market.Key,
                    query.Sport,
                    query.DaysBack);
                break;

            case PlayerOddsQuery playerQuery:
                logger.LogInformation(
                    "Parsed player query - Player: {Player}, Team: {Team}, Market: {Market}, Sport: {Sport}, Days: {Days}",
                    playerQuery.PlayerName,
                    playerQuery.Team ?? "unknown",
                    market.Key,
                    query.Sport,
                    query.DaysBack);
                break;
        }
    }

    private static string GetQueryDescription(OddsQueryBase query) => query switch
    {
        GameOddsQuery g => g.AwayTeam is not null ? $"{g.AwayTeam} at {g.HomeTeam}" : g.HomeTeam,
        PlayerOddsQuery p => $"{p.PlayerName} {p.Team ?? ""}".Trim(),
        _ => "query"
    };

    private async Task<byte[]> GetOrCreateChartAsync(List<OddsBase> odds, OddsQueryBase query, TeamSide side)
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

    private async Task<string?> GetOrCreateAnalysisAsync(OddsBase odds, OddsQueryBase query, TeamSide side)
    {
        var promptKey = $"{odds.EventId}:{query.MarketDefinition.Key}:{side}:{odds.Snapshots.Count}";
        var promptHash = ComputeHash(promptKey);

        var cached = await cacheService.GetAIExplanationAsync(promptHash);
        if (cached is not null)
        {
            logger.LogDebug("Analysis cache hit: {Key}", promptHash);
            return cached;
        }

        logger.LogDebug("Analysis cache miss: {Key}", promptHash);
        var analysis = await claudeService.AnalyzeOddsMovementAsync(odds, query, side);

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