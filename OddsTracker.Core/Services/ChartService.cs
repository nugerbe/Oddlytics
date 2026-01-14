using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Text.Json;
using System.Web;

namespace OddsTracker.Core.Services
{
    public class QuickChartService(HttpClient httpClient, ILogger<QuickChartService> logger) : IChartService
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly ILogger<QuickChartService> _logger = logger;

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
            ["mybookieag"] = "rgb(128, 0, 128)"
        };

        public async Task<byte[]> GenerateChartAsync(List<NormalizedOdds> odds, OddsQuery query, TeamSide side = TeamSide.Home)
        {
            if (odds.Count == 0 || odds.SelectMany(o => o.Snapshots).Any() == false)
            {
                throw new InvalidOperationException("No odds data available to chart");
            }

            var game = odds[0];
            var chartData = BuildChartData(game, query, side);

            // Calculate y-axis bounds based on actual data
            var allValues = game.Snapshots
                .Select(s => GetValueForMarketType(s, query.MarketType, side))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            var yMin = allValues.Count > 0 ? (double)allValues.Min() : 0;
            var yMax = allValues.Count > 0 ? (double)allValues.Max() : 100;

            // Add some padding to the y-axis
            var padding = Math.Max(Math.Abs(yMax - yMin) * 0.1, 5);
            yMin -= padding;
            yMax += padding;

            // Build title based on market type and team side
            var teamName = side == TeamSide.Home ? game.HomeTeam : game.AwayTeam;
            var title = query.MarketType switch
            {
                MarketType.Total => $"{game.AwayTeam} @ {game.HomeTeam} - Total (O/U)",
                MarketType.Spread => $"{teamName} Spread Movement",
                MarketType.Moneyline => $"{teamName} Moneyline Movement",
                _ => $"{game.AwayTeam} @ {game.HomeTeam} - {query.MarketType}"
            };

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
                                    labelString = GetYAxisLabel(query.MarketType, side)
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

            _logger.LogDebug("Generating chart from QuickChart. Y-axis range: {Min} to {Max}", yMin, yMax);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }

        private static object BuildChartData(NormalizedOdds game, OddsQuery query, TeamSide side)
        {
            var datasets = new List<object>();

            var snapshotsByBook = game.Snapshots
                .GroupBy(s => s.BookmakerName)
                .ToList();

            // Different point styles for each bookmaker
            var pointStyles = new[] { "circle", "rect", "triangle", "rectRot", "cross", "crossRot", "star", "rectRounded" };
            var lineStyles = new[] { Array.Empty<int>(), new[] { 5, 5 }, new[] { 10, 5 }, new[] { 2, 2 } };

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
                        y = GetValueForMarketType(s, query.MarketType, side)
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

        private static decimal? GetValueForMarketType(BookSnapshot snapshot, MarketType marketType, TeamSide side)
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

        private static string GetYAxisLabel(MarketType marketType, TeamSide side)
        {
            var teamLabel = side == TeamSide.Home ? "Home" : "Away";
            return marketType switch
            {
                MarketType.Spread => $"Spread ({teamLabel} Team)",
                MarketType.Total => "Total Points (O/U)",
                MarketType.Moneyline => $"Moneyline ({teamLabel} Team)",
                _ => "Value"
            };
        }
    }
}