using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Net.Http;
using System.Net.Http.Json;

namespace OddsTracker.Core.Clients
{
    /// <summary>
    /// Simple HTTP client for The Odds API.
    /// No business logic - just makes API calls.
    /// Tier filtering and defaults are handled by OddsService.
    /// </summary>
    public class OddsApiClient(
        HttpClient httpClient,
        string apiKey,
        ILogger<OddsApiClient> logger) : IOddsApiClient
    {
        private const string BaseUrl = "https://api.the-odds-api.com/v4";
        private const string DefaultSport = "americanfootball_nfl";

        #region Public API Methods

        public async Task<List<OddsEvent>> GetOddsAsync(
            string sport,
            string[]? markets = null,
            string[]? bookmakers = null)
        {
            var url = $"{BaseUrl}/sports/{sport}/odds" +
                      $"?apiKey={apiKey}" +
                      $"&regions=us" +
                      $"&oddsFormat=american";

            if (bookmakers is { Length: > 0 })
            {
                url += $"&bookmakers={string.Join(",", bookmakers)}";
            }

            if (markets is { Length: > 0 })
            {
                url += $"&markets={string.Join(",", markets)}";
            }

            logger.LogDebug("Fetching odds: {Sport}, Markets: {Markets}", sport, markets);

            var response = await httpClient.GetFromJsonAsync<List<OddsEvent>>(url);
            return response ?? [];
        }

        public async Task<OddsEvent?> GetEventOddsAsync(
            string eventId,
            string sport,
            string[]? markets = null,
            string[]? bookmakers = null)
        {
            var url = $"{BaseUrl}/sports/{sport}/events/{eventId}/odds" +
                      $"?apiKey={apiKey}" +
                      $"&regions=us" +
                      $"&oddsFormat=american";

            if (markets is { Length: > 0 })
                url += $"&markets={string.Join(",", markets)}";

            if (bookmakers is { Length: > 0 })
                url += $"&bookmakers={string.Join(",", bookmakers)}";

            logger.LogDebug("Fetching event odds: {EventId}, Markets: {Markets}", eventId, markets);

            return await httpClient.GetFromJsonAsync<OddsEvent>(url);
        }

        public async Task<List<SportEvent>> GetEventsAsync(string? sport = null)
        {
            sport ??= DefaultSport;

            // This endpoint is free - doesn't count against quota
            var url = $"{BaseUrl}/sports/{sport}/events?apiKey={apiKey}";
            var response = await httpClient.GetFromJsonAsync<List<SportEvent>>(url);
            return response ?? [];
        }

        public async Task<EventMarkets> GetEventMarketsAsync(string eventId, string sport)
        {
            var url = $"{BaseUrl}/{sport}/events/{eventId}/markets?apiKey={apiKey}&regions=us";
            var response = await httpClient.GetFromJsonAsync<EventMarketsResponse>(url);

            if (response is null)
            {
                logger.LogWarning("Failed to get markets for event {EventId}", eventId);
                return new EventMarkets { EventId = eventId };
            }

            var eventMarkets = new EventMarkets
            {
                EventId = response.Id,
                HomeTeam = response.HomeTeam,
                AwayTeam = response.AwayTeam,
                CommenceTime = response.CommenceTime,
                Markets = [.. response.Bookmakers
                    .SelectMany(b => b.Markets.Select(m => new AvailableMarket
                    {
                        BookmakerKey = b.Key,
                        BookmakerName = b.Title,
                        MarketKey = m.Key,
                        LastUpdate = m.LastUpdate
                    }))]
            };

            logger.LogDebug("Cached {Count} markets for event {EventId}",
                eventMarkets.Markets.Count, eventId);

            return eventMarkets;
        }

        public async Task<List<ScoreEvent>> GetScoresAsync(string? sport = null, int daysFrom = 1)
        {
            sport ??= DefaultSport;

            var url = $"{BaseUrl}/sports/{sport}/scores" +
                      $"?apiKey={apiKey}" +
                      $"&daysFrom={daysFrom}";

            var response = await httpClient.GetFromJsonAsync<List<ScoreEvent>>(url);
            return response ?? [];
        }

        public async Task<HistoricalOddsResponse?> GetHistoricalOddsAsync(
            DateTime snapshotTime,
            string sport,
            string[]? markets = null)
        {
            var dateParam = snapshotTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            var url = $"{BaseUrl}/historical/sports/{sport}/odds" +
                      $"?apiKey={apiKey}" +
                      $"&regions=us" +
                      $"&oddsFormat=american" +
                      $"&date={dateParam}";

            if (markets is { Length: > 0 })
                url += $"&markets={string.Join(",", markets)}";

            return await httpClient.GetFromJsonAsync<HistoricalOddsResponse>(url);
        }

        public async Task<HistoricalEventOddsResponse?> GetHistoricalEventOddsAsync(
            string eventId,
            DateTime snapshotTime,
            string sport,
            string[]? markets = null,
            string[]? bookmakers = null)
        {
            var dateParam = snapshotTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            var url = $"{BaseUrl}/historical/sports/{sport}/events/{eventId}/odds" +
                      $"?apiKey={apiKey}" +
                      $"&regions=us" +
                      $"&oddsFormat=american" +
                      $"&date={dateParam}";

            if (markets is { Length: > 0 })
                url += $"&markets={string.Join(",", markets)}";
            if (bookmakers is { Length: > 0 })
                url += $"&bookmakers={string.Join(",", bookmakers)}";

            try
            {
                return await httpClient.GetFromJsonAsync<HistoricalEventOddsResponse>(url);
            }
            catch (HttpRequestException)
            {
                // Historical data may not be available for all timestamps
                return null;
            }
        }

        public async Task<List<OddsSnapshot>> GetLineMovementAsync(
            string sportKey,
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
                var historicalResponse = await GetHistoricalEventOddsAsync(eventId, currentTime, sportKey, markets, bookmakers);

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
            var currentOdds = await GetEventOddsAsync(eventId, sportKey, markets, bookmakers);
            if (currentOdds != null)
            {
                snapshots.Add(new OddsSnapshot(now, currentOdds));
            }

            return snapshots;
        }

        #endregion
    }
}