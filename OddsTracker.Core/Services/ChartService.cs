using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Text.Json;
using System.Web;

namespace OddsTracker.Core.Services
{
    public class QuickChartService(HttpClient httpClient, ILogger<QuickChartService> logger) : IChartService
    {
        private const string BaseUrl = "https://quickchart.io/chart";

        // Colors for different bookmakers
        private static readonly Dictionary<string, string> BookmakerColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["draftkings"] = "rgb(0, 102, 0)",
            ["fanduel"] = "rgb(0, 102, 204)",
            ["betmgm"] = "rgb(204, 153, 0)",
            ["williamhill_us"] = "rgb(153, 0, 0)",
            ["betrivers"] = "rgb(0, 153, 153)",
            ["fanatics"] = "rgb(153, 51, 153)",
            ["betonlineag"] = "rgb(255, 102, 0)",
            ["lowvig"] = "rgb(102, 0, 102)",
            ["betus"] = "rgb(0, 100, 0)",
            ["bovada"] = "rgb(0, 128, 0)",
            ["mybookieag"] = "rgb(128, 0, 128)",
            ["pinnacle"] = "rgb(0, 51, 102)",
            ["espnbet"] = "rgb(204, 0, 0)",
            ["pointsbetus"] = "rgb(0, 153, 76)"
        };

        public async Task<byte[]> GenerateChartAsync(List<OddsBase> odds, OddsQueryBase query, TeamSide side = TeamSide.Home)
        {
            if (odds.Count == 0 || !odds.SelectMany(o => o.Snapshots).Any())
            {
                throw new InvalidOperationException("No odds data available to chart");
            }

            var oddsData = odds[0];
            var market = query.MarketDefinition;
            var chartData = BuildChartData(oddsData, market, side);

            // Calculate y-axis bounds based on actual data
            var allValues = oddsData.Snapshots
                .Select(s => GetValueForMarket(s, market, side))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            var yMin = allValues.Count > 0 ? (double)allValues.Min() : 0;
            var yMax = allValues.Count > 0 ? (double)allValues.Max() : 100;

            // Add some padding to the y-axis
            var padding = Math.Max(Math.Abs(yMax - yMin) * 0.1, 5);
            yMin -= padding;
            yMax += padding;

            var title = BuildChartTitle(oddsData, market, side);

            var chartConfig = new
            {
                type = "line",
                data = chartData,
                options = new
                {
                    responsive = true,
                    title = new
                    {
                        display = true,
                        text = title,
                        fontSize = 16
                    },
                    scales = new
                    {
                        xAxes = new[]
                        {
                            new
                            {
                                type = "time",
                                time = new { unit = "day", displayFormats = new { day = "MMM D" } },
                                scaleLabel = new { display = true, labelString = "Date" }
                            }
                        },
                        yAxes = new[]
                        {
                            new
                            {
                                ticks = new
                                {
                                    min = yMin,
                                    max = yMax
                                },
                                scaleLabel = new
                                {
                                    display = true,
                                    labelString = GetYAxisLabel(market, side)
                                }
                            }
                        }
                    },
                    legend = new
                    {
                        display = true,
                        position = "bottom"
                    }
                }
            };

            var json = JsonSerializer.Serialize(chartConfig);
            var encodedChart = HttpUtility.UrlEncode(json);
            var url = $"{BaseUrl}?c={encodedChart}&width=800&height=400&backgroundColor=white";

            logger.LogDebug("Generating chart from QuickChart. Y-axis range: {Min} to {Max}", yMin, yMax);

            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }

        private static string BuildChartTitle(OddsBase odds, MarketDefinition market, TeamSide side)
        {
            return odds switch
            {
                GameOdds game => market.OutcomeType switch
                {
                    // Totals - show O/U
                    OutcomeType.OverUnder when !market.IsPlayerProp =>
                        $"{game.AwayTeam} @ {game.HomeTeam} - {market.DisplayName} (O/U)",

                    // Team-based spreads/moneylines - show team name
                    OutcomeType.TeamBased =>
                        $"{(side == TeamSide.Home ? game.HomeTeam : game.AwayTeam)} {market.DisplayName} Movement",

                    // Default
                    _ => $"{game.AwayTeam} @ {game.HomeTeam} - {market.DisplayName}"
                },

                PlayerOdds player =>
                    $"{player.PlayerName} - {market.DisplayName}",

                _ => "Odds Movement"
            };
        }

        private static object BuildChartData(OddsBase oddsData, MarketDefinition market, TeamSide side)
        {
            var datasets = new List<object>();

            var snapshotsByBook = oddsData.Snapshots
                .GroupBy(s => s.BookmakerName)
                .ToList();

            // Different point styles for each bookmaker
            var pointStyles = new[] { "circle", "rect", "triangle", "rectRot", "cross", "crossRot", "star", "rectRounded" };
            var lineStyles = new int[][] { [], [5, 5], [10, 5], [2, 2] };

            var bookIndex = 0;
            foreach (var bookGroup in snapshotsByBook)
            {
                var bookName = bookGroup.Key;
                var bookKey = bookGroup.FirstOrDefault()?.BookmakerKey ?? bookName;
                var color = BookmakerColors.GetValueOrDefault(bookKey.ToLowerInvariant(), "rgb(128, 128, 128)");

                var dataPoints = bookGroup
                    .OrderBy(s => s.Timestamp)
                    .Select(s => new
                    {
                        x = s.Timestamp.ToString("o"),
                        y = GetValueForMarket(s, market, side)
                    })
                    .Where(p => p.y.HasValue)
                    .ToList();

                if (dataPoints.Count > 0)
                {
                    var pointStyle = pointStyles[bookIndex % pointStyles.Length];
                    var dashStyle = lineStyles[bookIndex % lineStyles.Length];

                    datasets.Add(new
                    {
                        label = bookName,
                        data = dataPoints,
                        borderColor = color,
                        backgroundColor = color,
                        fill = false,
                        tension = 0.1,
                        pointStyle,
                        pointRadius = 5,
                        pointHoverRadius = 8,
                        borderWidth = 2,
                        borderDash = dashStyle
                    });
                }

                bookIndex++;
            }

            return new { datasets };
        }

        private static decimal? GetValueForMarket(BookSnapshotBase snapshot, MarketDefinition market, TeamSide side)
        {
            return snapshot switch
            {
                GameBookSnapshot game => market.OutcomeType switch
                {
                    // Spreads - flip sign for away team
                    OutcomeType.TeamBased when market.Key.Contains("spread", StringComparison.OrdinalIgnoreCase) =>
                        side == TeamSide.Home ? game.Line : -game.Line,

                    // Totals - just use the line
                    OutcomeType.OverUnder =>
                        game.Line,

                    // Moneylines - use the appropriate team's odds
                    OutcomeType.TeamBased when market.Key.Contains("h2h", StringComparison.OrdinalIgnoreCase) =>
                        side == TeamSide.Home ? game.HomeOdds : game.AwayOdds,

                    // Default team-based with point (other spreads)
                    OutcomeType.TeamBased =>
                        side == TeamSide.Home ? game.Line : -game.Line,

                    // Default
                    _ => game.Line
                },

                PlayerBookSnapshot player =>
                    player.Line,

                _ => snapshot.Line
            };
        }

        private static string GetYAxisLabel(MarketDefinition market, TeamSide side)
        {
            // Player props - use display name
            if (market.IsPlayerProp)
            {
                return market.DisplayName;
            }

            var teamLabel = side == TeamSide.Home ? "Home" : "Away";

            return market.OutcomeType switch
            {
                // Totals
                OutcomeType.OverUnder => "Total Points (O/U)",

                // Team-based
                OutcomeType.TeamBased => market.Key switch
                {
                    var k when k.Contains("spread", StringComparison.OrdinalIgnoreCase) =>
                        $"Spread ({teamLabel} Team)",
                    var k when k.Contains("h2h", StringComparison.OrdinalIgnoreCase) =>
                        $"Moneyline ({teamLabel} Team)",
                    _ => $"{market.DisplayName} ({teamLabel} Team)"
                },

                // Default
                _ => market.DisplayName
            };
        }
    }
}