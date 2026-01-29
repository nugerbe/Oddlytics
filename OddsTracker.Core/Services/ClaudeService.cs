using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OddsTracker.Core.Services
{
    public class ClaudeService(
        AnthropicClient client,
        IServiceScopeFactory scopeFactory,
        IEnhancedCacheService cache,
        ILogger<ClaudeService> logger) : IClaudeService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        private const string IntentParserPrompt = """
            You are a parser that extracts structured data from natural language queries about sports odds.
            
            Extract the following from the user's message:
            - query_type: Either "game" or "player"
            - sport: The sport mentioned (e.g., "nfl", "nba", "mlb", "nhl", "college football")
            - home_team: The home team name (for game queries)
            - away_team: The away team name (for game queries)
            - player_name: The player name (for player prop queries)
            - market: The betting market mentioned in natural language (e.g., "spread", "moneyline", "total", 
                      "passing yards", "rushing yards", "receptions", "anytime touchdown", "first half spread")
            - days_back: Number of days of historical data requested (default to 3)
            
            Respond ONLY with valid JSON in this format:
            {"query_type": "game|player", "sport": "...", "home_team": "...", "away_team": "...", "player_name": "...", "market": "...", "days_back": 3}
            
            Examples:
            - "Show me Chiefs spread movement" -> {"query_type": "game", "sport": "nfl", "home_team": "Kansas City Chiefs", "away_team": null, "player_name": null, "market": "spread", "days_back": 3}
            - "Patrick Mahomes passing yards" -> {"query_type": "player", "sport": "nfl", "home_team": null, "away_team": null, "player_name": "Patrick Mahomes", "market": "passing yards", "days_back": 3}
            - "Bills at Chiefs moneyline last 5 days" -> {"query_type": "game", "sport": "nfl", "home_team": "Kansas City Chiefs", "away_team": "Buffalo Bills", "player_name": null, "market": "moneyline", "days_back": 5}
            - "Lakers spread NBA" -> {"query_type": "game", "sport": "nba", "home_team": "Los Angeles Lakers", "away_team": null, "player_name": null, "market": "spread", "days_back": 3}
            - "Josh Allen first half passing" -> {"query_type": "player", "sport": "nfl", "home_team": null, "away_team": null, "player_name": "Josh Allen", "market": "first half passing yards", "days_back": 3}
            """;

        #region Cached Repository Access

        private async Task<MarketDefinition?> GetMarketByKeyAsync(string marketKey)
        {
            var cacheKey = $"market:bykey:{marketKey}";

            var cached = await cache.GetAsync<MarketDefinition>(cacheKey);
            if (cached is not null)
                return cached;

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var market = await repo.GetMarketByKeyAsync(marketKey);

            if (market is not null)
                await cache.SetAsync(cacheKey, market, TimeSpan.FromHours(1));

            return market;
        }

        #endregion

        public async Task<OddsQueryBase?> ParseQueryAsync(string userMessage)
        {
            try
            {
                var response = await CallClaudeAsync(IntentParserPrompt, userMessage, 150);

                if (string.IsNullOrEmpty(response))
                {
                    logger.LogWarning("Empty response from Claude for intent parsing");
                    return null;
                }

                logger.LogDebug("Claude intent response: {Response}", response);

                var json = ExtractJson(response);
                if (json is null)
                {
                    logger.LogWarning("Could not extract JSON from response: {Response}", response);
                    return null;
                }

                var parsed = JsonSerializer.Deserialize<ParsedIntent>(json, JsonOptions);

                if (parsed is null)
                {
                    logger.LogWarning("Failed to deserialize parsed intent from JSON: {Json}", json);
                    return null;
                }

                if (parsed?.Error is not null)
                {
                    logger.LogWarning("Parse error: {Error}", parsed.Error);
                    return null;
                }

                if (parsed?.Sport is null || parsed.Market is null)
                {
                    logger.LogWarning("No sport specified in parsed intent");
                    return null;
                }

                // Resolve market definition from database (cached)
                var marketKey = parsed.Market;
                var market = await GetMarketByKeyAsync(marketKey);

                if (market is null)
                {
                    logger.LogWarning("Unknown market key: {MarketKey}, defaulting to spreads", marketKey);
                    market = await GetMarketByKeyAsync("spreads");

                    if (market is null)
                    {
                        logger.LogError("Could not resolve default market 'spreads'");
                        return null;
                    }
                }

                var daysBack = parsed?.DaysBack ?? 3;

                if (parsed?.QueryType == "player" || !string.IsNullOrEmpty(parsed?.PlayerName))
                {
                    if (string.IsNullOrEmpty(parsed?.PlayerName))
                    {
                        logger.LogWarning("Player query detected but no player name provided");
                        return null;
                    }

                    return new PlayerOddsQuery
                    {
                        MarketDefinition = market,
                        Sport = parsed.Sport,
                        DaysBack = daysBack,
                        PlayerName = parsed.PlayerName,
                        Team = null
                    };
                }

                if (parsed?.HomeTeam is null)
                {
                    logger.LogWarning("Game query detected but no home team provided");
                    return null;
                }

                return new GameOddsQuery
                {
                    MarketDefinition = market,
                    Sport = parsed.Sport,
                    DaysBack = daysBack,
                    HomeTeam = parsed.HomeTeam,
                    AwayTeam = parsed.AwayTeam
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error parsing intent");
                return null;
            }
        }

        private static string? ExtractJson(string response)
        {
            var startIndex = response.IndexOf('{');
            var endIndex = response.LastIndexOf('}');

            if (startIndex >= 0 && endIndex > startIndex)
            {
                return response[startIndex..(endIndex + 1)];
            }

            return null;
        }

        private record ParsedIntent(
            [property: JsonPropertyName("query_type")] string? QueryType,
            [property: JsonPropertyName("sport")] string? Sport,
            [property: JsonPropertyName("home_team")] string? HomeTeam,
            [property: JsonPropertyName("away_team")] string? AwayTeam,
            [property: JsonPropertyName("player_name")] string? PlayerName,
            [property: JsonPropertyName("market")] string? Market,
            [property: JsonPropertyName("days_back")] int? DaysBack,
            [property: JsonPropertyName("error")] string? Error
        );

        private const string OddsAnalysisPrompt = """
            You are an expert sports betting analyst. Analyze the provided odds movement data and give a brief, insightful analysis.
            
            Focus on:
            - Direction of line movement (which way is the line moving?)
            - Magnitude of movement (is it significant?)
            - Consensus across books (are all books moving together or is there disagreement?)
            - What this might indicate about sharp money or public betting
            - Any notable outliers among the sportsbooks
            
            Keep your analysis concise (2-4 sentences). Be direct and actionable.
            Do not use bullet points. Write in a conversational but professional tone.
            Do not repeat the raw numbers - the user can see those in the chart.
            """;

        public async Task<string> AnalyzeOddsMovementAsync(OddsBase odds, OddsQueryBase query, TeamSide side)
        {
            try
            {
                var dataDescription = BuildOddsDataDescription(odds, query, side);
                logger.LogDebug("Sending odds data to Claude for analysis: {Data}", dataDescription);

                var response = await CallClaudeAsync(OddsAnalysisPrompt, dataDescription, 200);

                logger.LogInformation("Claude analysis response: {Response}", response ?? "null");

                return response ?? "Unable to generate analysis.";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating odds analysis");
                return "Analysis unavailable.";
            }
        }

        private static string BuildOddsDataDescription(OddsBase odds, OddsQueryBase query, TeamSide side)
        {
            var sb = new StringBuilder();

            switch (odds)
            {
                case GameOdds game:
                    var teamName = side == TeamSide.Home ? game.HomeTeam : game.AwayTeam;
                    sb.AppendLine($"Game: {game.AwayTeam} @ {game.HomeTeam}");
                    sb.AppendLine($"Market: {query.MarketDefinition.DisplayName} for {teamName}");
                    break;

                case PlayerOdds player:
                    sb.AppendLine($"Player: {player.PlayerName} ({player.Team})");
                    sb.AppendLine($"Market: {query.MarketDefinition.DisplayName}");
                    if (player.Opponent is not null)
                        sb.AppendLine($"Opponent: {player.Opponent}");
                    break;
            }

            sb.AppendLine($"Game Time: {odds.CommenceTime:MMM dd, yyyy h:mm tt}");
            sb.AppendLine();

            if (odds.Snapshots.Count == 0)
            {
                sb.AppendLine("No historical odds data available.");
                return sb.ToString();
            }

            sb.AppendLine("Odds movement data by sportsbook:");

            var snapshotsByBook = odds.Snapshots
                .GroupBy(s => s.BookmakerName)
                .ToList();

            foreach (var bookGroup in snapshotsByBook)
            {
                var orderedSnapshots = bookGroup.OrderBy(s => s.Timestamp).ToList();
                if (orderedSnapshots.Count == 0) continue;

                var first = orderedSnapshots[0];
                var last = orderedSnapshots[^1];

                var firstValue = GetOddsValue(first, query.MarketDefinition, side);
                var lastValue = GetOddsValue(last, query.MarketDefinition, side);

                if (firstValue.HasValue && lastValue.HasValue)
                {
                    var change = lastValue.Value - firstValue.Value;
                    var changeStr = change >= 0 ? $"+{change}" : change.ToString();
                    sb.AppendLine($"  {bookGroup.Key}: {firstValue} -> {lastValue} ({changeStr})");
                }
            }

            var allFirstValues = snapshotsByBook
                .Select(g => g.OrderBy(s => s.Timestamp).FirstOrDefault())
                .Where(s => s is not null)
                .Select(s => GetOddsValue(s!, query.MarketDefinition, side))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            var allLastValues = snapshotsByBook
                .Select(g => g.OrderBy(s => s.Timestamp).LastOrDefault())
                .Where(s => s is not null)
                .Select(s => GetOddsValue(s!, query.MarketDefinition, side))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (allFirstValues.Count > 0 && allLastValues.Count > 0)
            {
                var avgFirst = allFirstValues.Average();
                var avgLast = allLastValues.Average();
                var avgChange = avgLast - avgFirst;

                sb.AppendLine();
                sb.AppendLine($"Average opening: {avgFirst:F1}");
                sb.AppendLine($"Average current: {avgLast:F1}");
                sb.AppendLine($"Average movement: {(avgChange >= 0 ? "+" : "")}{avgChange:F1}");
            }

            return sb.ToString();
        }

        private static decimal? GetOddsValue(BookSnapshotBase snapshot, MarketDefinition market, TeamSide side)
        {
            return snapshot switch
            {
                GameBookSnapshot game => market.OutcomeType switch
                {
                    OutcomeType.TeamBased => market.Key == "h2h"
                        ? (side == TeamSide.Home ? game.HomeOdds : game.AwayOdds)
                        : (side == TeamSide.Home ? game.Line : -game.Line),
                    OutcomeType.OverUnder => game.Line,
                    _ => game.Line
                },
                PlayerBookSnapshot player => player.Line,
                _ => snapshot.Line
            };
        }

        private async Task<string?> CallClaudeAsync(string systemPrompt, string userMessage, int maxTokens)
        {
            var parameters = new MessageCreateParams
            {
                Model = Model.ClaudeSonnet4_5,
                MaxTokens = maxTokens,
                System = systemPrompt,
                Messages = [
                    new()
                    {
                        Role = Role.User,
                        Content = userMessage
                    }
                ]
            };

            var response = await client.Messages.Create(parameters);
            var message = string.Join("", response.Content
                .Where(m => m.Value is TextBlock)
                .Select(m => (m.Value as TextBlock)?.Text));

            return message;
        }
    }
}