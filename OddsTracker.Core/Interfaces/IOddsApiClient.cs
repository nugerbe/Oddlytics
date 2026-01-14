using OddsTracker.Core.Clients;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Client for The Odds API (https://the-odds-api.com)
    /// Free tier: 500 requests/month with real (non-scrambled) data
    /// </summary>
    public interface IOddsApiClient
    {
        /// <summary>
        /// Get current odds for a sport
        /// </summary>
        Task<List<OddsEvent>> GetOddsAsync(
            string sport,
            string[] markets,
            string[]? bookmakers = null);

        /// <summary>
        /// Get current NFL odds for all upcoming games
        /// </summary>
        Task<List<OddsEvent>> GetNflOddsAsync(
            string[]? markets = null,
            string[]? bookmakers = null);

        /// <summary>
        /// Get odds for a specific event (supports more markets including player props)
        /// </summary>
        Task<OddsEvent?> GetEventOddsAsync(
            string eventId,
            string[]? markets = null,
            string[]? bookmakers = null);

        /// <summary>
        /// Get list of upcoming NFL events (does not count against quota)
        /// </summary>
        Task<List<SportEvent>> GetNflEventsAsync();

        /// <summary>
        /// Get NFL scores for live and recently completed games
        /// </summary>
        Task<List<ScoreEvent>> GetNflScoresAsync(int daysFrom = 1);

        /// <summary>
        /// Get historical odds snapshot at a specific point in time for all games
        /// (Requires paid plan - costs 10 per region per market)
        /// </summary>
        Task<HistoricalOddsResponse?> GetHistoricalOddsAsync(
            DateTime snapshotTime,
            string[]? markets = null);

        /// <summary>
        /// Get historical odds for a specific event at a specific point in time
        /// (Requires paid plan - costs 10 per region per market)
        /// </summary>
        Task<HistoricalEventOddsResponse?> GetHistoricalEventOddsAsync(
            string eventId,
            DateTime snapshotTime,
            string[]? markets = null,
            string[]? bookmakers = null);

        /// <summary>
        /// Get line movement for an event over a time range by making multiple historical calls
        /// Returns snapshots at regular intervals from startTime to now
        /// </summary>
        Task<List<OddsSnapshot>> GetLineMovementAsync(
            string eventId,
            int daysBack = 3,
            int intervalsPerDay = 4,
            string[]? markets = null,
            string[]? bookmakers = null);

        /// <summary>
        /// Get available markets for an event (does not return odds, just market availability)
        /// </summary>
        Task<EventMarketsResponse?> GetEventMarketsAsync(string eventId);
    }
}
