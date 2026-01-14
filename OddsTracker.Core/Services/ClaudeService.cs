using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OddsTracker.Core.Services
{
    public class ClaudeService(AnthropicClient client, ILogger<ClaudeService> logger) : IClaudeService
    {
        private readonly AnthropicClient _client = client;
        private readonly ILogger<ClaudeService> _logger = logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
        private const string IntentParserPrompt = """
            You are a parser that extracts structured data from natural language queries about NFL odds.
            
            Extract the following from the user's message:
            - home_team: The home NFL team name (normalize to full name, e.g., "Chiefs" -> "Kansas City Chiefs"). Can be null if not specified.
            - away_team: The away NFL team name (normalize to full name, e.g., "Bills" -> "Buffalo Bills"). Can be null if not specified.
            - market_type: One of "spread", "total", or "moneyline"
            - days_back: Number of days of historical data requested (default to 3 if not specified)
            
            If the user only mentions one team, put it in home_team and leave away_team null.
            If the user mentions two teams, determine which is home/away from context (e.g., "Bills at Chiefs" means Chiefs are home, Bills are away).
            If home/away isn't clear, put the first team mentioned in home_team and second in away_team.
            
            Respond ONLY with valid JSON in this exact format:
            {"home_team": "Team Name", "away_team": "Team Name or null", "market_type": "spread|total|moneyline", "days_back": 3}
            
            If you cannot parse the query, respond with:
            {"error": "reason"}
            
            Examples:
            - "Show me Chiefs spread movement" -> {"home_team": "Kansas City Chiefs", "away_team": null, "market_type": "spread", "days_back": 3}
            - "Eagles moneyline over the past week" -> {"home_team": "Philadelphia Eagles", "away_team": null, "market_type": "moneyline", "days_back": 7}
            - "How has the over/under moved for the Bills game" -> {"home_team": "Buffalo Bills", "away_team": null, "market_type": "total", "days_back": 3}
            - "Chiefs vs Bills spread" -> {"home_team": "Kansas City Chiefs", "away_team": "Buffalo Bills", "market_type": "spread", "days_back": 3}
            - "Bills at Chiefs moneyline" -> {"home_team": "Kansas City Chiefs", "away_team": "Buffalo Bills", "market_type": "moneyline", "days_back": 3}
            - "Eagles hosting the Cowboys total" -> {"home_team": "Philadelphia Eagles", "away_team": "Dallas Cowboys", "market_type": "total", "days_back": 3}
            """;

        public async Task<OddsQuery?> ParseQueryAsync(string userMessage)
        {
            try
            {
                var response = await CallClaudeAsync(IntentParserPrompt, userMessage, 150);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("Empty response from Claude for intent parsing");
                    return null;
                }

                _logger.LogDebug("Claude intent response: {Response}", response);

                var json = ExtractJson(response);
                if (json is null)
                {
                    _logger.LogWarning("Could not extract JSON from response: {Response}", response);
                    return null;
                }

                var parsed = JsonSerializer.Deserialize<ParsedIntent>(json, JsonOptions);

                if (parsed?.Error is not null)
                {
                    _logger.LogWarning("Parse error: {Error}", parsed.Error);
                    return null;
                }

                if (parsed?.HomeTeam is null || parsed.MarketType is null)
                {
                    return null;
                }

                var marketType = parsed.MarketType.ToLowerInvariant() switch
                {
                    "spread" => MarketType.Spread,
                    "total" => MarketType.Total,
                    "moneyline" => MarketType.Moneyline,
                    _ => MarketType.Spread
                };

                return new OddsQuery
                {
                    HomeTeam = parsed.HomeTeam,
                    AwayTeam = parsed.AwayTeam,
                    MarketType = marketType,
                    DaysBack = parsed.DaysBack ?? 3
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing intent");
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
            [property: JsonPropertyName("home_team")] string? HomeTeam,
            [property: JsonPropertyName("away_team")] string? AwayTeam,
            [property: JsonPropertyName("market_type")] string? MarketType,
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

        public async Task<string> AnalyzeOddsMovementAsync(NormalizedOdds odds, OddsQuery query, TeamSide side)
        {
            try
            {
                var dataDescription = BuildOddsDataDescription(odds, query, side);
                _logger.LogDebug("Sending odds data to Claude for analysis: {Data}", dataDescription);

                var response = await CallClaudeAsync(OddsAnalysisPrompt, dataDescription, 200);

                _logger.LogInformation("Claude analysis response: {Response}", response ?? "null");

                return response ?? "Unable to generate analysis.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating odds analysis");
                return "Analysis unavailable.";
            }
        }

        private static string BuildOddsDataDescription(NormalizedOdds odds, OddsQuery query, TeamSide side)
        {
            var sb = new StringBuilder();

            var teamName = side == TeamSide.Home ? odds.HomeTeam : odds.AwayTeam;
            sb.AppendLine($"Game: {odds.AwayTeam} @ {odds.HomeTeam}");
            sb.AppendLine($"Market: {query.MarketType} for {teamName}");
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

                var firstValue = GetOddsValue(first, query.MarketType, side);
                var lastValue = GetOddsValue(last, query.MarketType, side);

                if (firstValue.HasValue && lastValue.HasValue)
                {
                    var change = lastValue.Value - firstValue.Value;
                    var changeStr = change >= 0 ? $"+{change}" : change.ToString();
                    sb.AppendLine($"  {bookGroup.Key}: {firstValue} -> {lastValue} ({changeStr})");
                }
            }

            // Calculate overall movement
            var allFirstValues = snapshotsByBook
                .Select(g => g.OrderBy(s => s.Timestamp).FirstOrDefault())
                .Where(s => s is not null)
                .Select(s => GetOddsValue(s!, query.MarketType, side))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            var allLastValues = snapshotsByBook
                .Select(g => g.OrderBy(s => s.Timestamp).LastOrDefault())
                .Where(s => s is not null)
                .Select(s => GetOddsValue(s!, query.MarketType, side))
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

        private static decimal? GetOddsValue(BookSnapshot snapshot, MarketType marketType, TeamSide side)
        {
            return marketType switch
            {
                MarketType.Spread => side == TeamSide.Home
                    ? snapshot.Line
                    : -snapshot.Line,
                MarketType.Total => snapshot.Line,
                MarketType.Moneyline => side == TeamSide.Home
                    ? snapshot.HomeOdds
                    : snapshot.AwayOdds,
                _ => snapshot.Line
            };
        }

        private async Task<string?> CallClaudeAsync(string systemPrompt, string userMessage, int maxTokens)
        {
            var parameters = new MessageCreateParams
            {
                Model = Model.ClaudeOpus4_5,
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

            var response = await _client.Messages.Create(parameters);
            var message = string.Join("", response.Content.Where(message => message.Value is TextBlock).Select(message => message.Value as TextBlock).Select((textBlock) => textBlock?.Text));

            return message;
        }
    }
}