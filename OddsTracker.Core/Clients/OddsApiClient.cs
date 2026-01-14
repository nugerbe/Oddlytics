using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Net.Http.Json;

namespace OddsTracker.Core.Clients
{
    public class OddsApiClient(HttpClient httpClient, string apiKey) : IOddsApiClient
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly string _apiKey = apiKey;
        private const string BaseUrl = "https://api.the-odds-api.com/v4";
        private const string DefaultSport = "americanfootball_nfl";

        // Default US bookmakers
        private static readonly string[] DefaultBookmakers =
        [
            "draftkings", "fanduel", "betmgm", "caesars",
            "betrivers", "pointsbetus", "wynnbet", "unibet_us"
        ];

        // Default markets: moneyline (h2h), spreads, totals
        private static readonly string[] DefaultMarkets = ["h2h", "spreads", "totals"];

        public async Task<List<OddsEvent>> GetOddsAsync(
            string sport,
            string[] markets,
            string[]? bookmakers = null)
        {
            bookmakers ??= DefaultBookmakers;

            var marketsParam = string.Join(",", markets);
            var bookmakersParam = string.Join(",", bookmakers);

            var url = $"{BaseUrl}/sports/{sport}/odds" +
                      $"?apiKey={_apiKey}" +
                      $"&regions=us" +
                      $"&markets={marketsParam}" +
                      $"&bookmakers={bookmakersParam}" +
                      $"&oddsFormat=american";

            var response = await _httpClient.GetFromJsonAsync<List<OddsEvent>>(url);
            return response ?? [];
        }

        public async Task<List<OddsEvent>> GetNflOddsAsync(
            string[]? markets = null,
            string[]? bookmakers = null)
        {
            markets ??= DefaultMarkets;
            return await GetOddsAsync(DefaultSport, markets, bookmakers);
        }

        public async Task<OddsEvent?> GetEventOddsAsync(
            string eventId,
            string[]? markets = null,
            string[]? bookmakers = null)
        {
            markets ??= DefaultMarkets;
            bookmakers ??= DefaultBookmakers;

            var marketsParam = string.Join(",", markets);
            var bookmakersParam = string.Join(",", bookmakers);

            var url = $"{BaseUrl}/sports/{DefaultSport}/events/{eventId}/odds" +
                      $"?apiKey={_apiKey}" +
                      $"&regions=us" +
                      $"&markets={marketsParam}" +
                      $"&bookmakers={bookmakersParam}" +
                      $"&oddsFormat=american";

            return await _httpClient.GetFromJsonAsync<OddsEvent>(url);
        }

        public async Task<List<SportEvent>> GetNflEventsAsync()
        {
            // This endpoint is free - doesn't count against quota
            var url = $"{BaseUrl}/sports/{DefaultSport}/events?apiKey={_apiKey}";
            var response = await _httpClient.GetFromJsonAsync<List<SportEvent>>(url);
            return response ?? [];
        }

        public async Task<List<ScoreEvent>> GetNflScoresAsync(int daysFrom = 1)
        {
            var url = $"{BaseUrl}/sports/{DefaultSport}/scores" +
                      $"?apiKey={_apiKey}" +
                      $"&daysFrom={daysFrom}";

            var response = await _httpClient.GetFromJsonAsync<List<ScoreEvent>>(url);
            return response ?? [];
        }

        public async Task<HistoricalOddsResponse?> GetHistoricalOddsAsync(
            DateTime snapshotTime,
            string[]? markets = null)
        {
            markets ??= DefaultMarkets;
            var marketsParam = string.Join(",", markets);
            var dateParam = snapshotTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            var url = $"{BaseUrl}/historical/sports/{DefaultSport}/odds" +
                      $"?apiKey={_apiKey}" +
                      $"&regions=us" +
                      $"&markets={marketsParam}" +
                      $"&oddsFormat=american" +
                      $"&date={dateParam}";

            return await _httpClient.GetFromJsonAsync<HistoricalOddsResponse>(url);
        }

        public async Task<HistoricalEventOddsResponse?> GetHistoricalEventOddsAsync(
            string eventId,
            DateTime snapshotTime,
            string[]? markets = null,
            string[]? bookmakers = null)
        {
            markets ??= DefaultMarkets;
            bookmakers ??= DefaultBookmakers;

            var marketsParam = string.Join(",", markets);
            var bookmakersParam = string.Join(",", bookmakers);
            var dateParam = snapshotTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            var url = $"{BaseUrl}/historical/sports/{DefaultSport}/events/{eventId}/odds" +
                      $"?apiKey={_apiKey}" +
                      $"&regions=us" +
                      $"&markets={marketsParam}" +
                      $"&bookmakers={bookmakersParam}" +
                      $"&oddsFormat=american" +
                      $"&date={dateParam}";

            try
            {
                return await _httpClient.GetFromJsonAsync<HistoricalEventOddsResponse>(url);
            }
            catch (HttpRequestException)
            {
                // Historical data may not be available for all timestamps
                return null;
            }
        }

        public async Task<List<OddsSnapshot>> GetLineMovementAsync(
            string eventId,
            int daysBack = 3,
            int intervalsPerDay = 4,
            string[]? markets = null,
            string[]? bookmakers = null)
        {
            var snapshots = new List<OddsSnapshot>();
            var now = DateTime.UtcNow;
            var startTime = now.AddDays(-daysBack);

            // Calculate interval in hours
            var intervalHours = 24.0 / intervalsPerDay;

            // Collect historical snapshots
            var currentTime = startTime;
            while (currentTime < now)
            {
                var historicalResponse = await GetHistoricalEventOddsAsync(
                    eventId,
                    currentTime,
                    markets,
                    bookmakers);

                if (historicalResponse?.Data != null)
                {
                    snapshots.Add(new OddsSnapshot(
                        historicalResponse.Timestamp,
                        historicalResponse.Data
                    ));
                }

                currentTime = currentTime.AddHours(intervalHours);

                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }

            // Get current odds as the final snapshot
            var currentOdds = await GetEventOddsAsync(eventId, markets, bookmakers);
            if (currentOdds != null)
            {
                snapshots.Add(new OddsSnapshot(now, currentOdds));
            }

            return snapshots;
        }

        public async Task<EventMarketsResponse?> GetEventMarketsAsync(string eventId)
        {
            var url = $"{BaseUrl}/sports/{DefaultSport}/events/{eventId}/markets?apiKey={_apiKey}&regions=us";

            var response = await _httpClient.GetFromJsonAsync<EventMarketsResponse>(url);
            return response;
        }
    }

    #region Extension Methods

    public static class OddsEventExtensions
    {
        /// <summary>
        /// Get the moneyline odds for home and away teams
        /// </summary>
        public static (int? HomeOdds, int? AwayOdds) GetMoneyline(this OddsEvent evt, string bookmakerKey)
        {
            var bookmaker = evt.Bookmakers.FirstOrDefault(b =>
                b.Key.Equals(bookmakerKey, StringComparison.OrdinalIgnoreCase));

            var market = bookmaker?.Markets.FirstOrDefault(m => m.Key == "h2h");
            if (market == null) return (null, null);

            var homeOutcome = market.Outcomes.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes.FirstOrDefault(o => o.Name == evt.AwayTeam);

            return (homeOutcome?.Price, awayOutcome?.Price);
        }

        /// <summary>
        /// Get the spread for the home team
        /// </summary>
        public static (decimal? Spread, int? HomeOdds, int? AwayOdds) GetSpread(this OddsEvent evt, string bookmakerKey)
        {
            var bookmaker = evt.Bookmakers.FirstOrDefault(b =>
                b.Key.Equals(bookmakerKey, StringComparison.OrdinalIgnoreCase));

            var market = bookmaker?.Markets.FirstOrDefault(m => m.Key == "spreads");
            if (market == null) return (null, null, null);

            var homeOutcome = market.Outcomes.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes.FirstOrDefault(o => o.Name == evt.AwayTeam);

            return (homeOutcome?.Point, homeOutcome?.Price, awayOutcome?.Price);
        }

        /// <summary>
        /// Get the total (over/under) line
        /// </summary>
        public static (decimal? Total, int? OverOdds, int? UnderOdds) GetTotal(this OddsEvent evt, string bookmakerKey)
        {
            var bookmaker = evt.Bookmakers.FirstOrDefault(b =>
                b.Key.Equals(bookmakerKey, StringComparison.OrdinalIgnoreCase));

            var market = bookmaker?.Markets.FirstOrDefault(m => m.Key == "totals");
            if (market == null) return (null, null, null);

            var overOutcome = market.Outcomes.FirstOrDefault(o => o.Name == "Over");
            var underOutcome = market.Outcomes.FirstOrDefault(o => o.Name == "Under");

            return (overOutcome?.Point, overOutcome?.Price, underOutcome?.Price);
        }
    }
    #endregion
}