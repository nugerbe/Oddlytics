using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Simple HTTP client interface for The Odds API.
    /// No business logic - just makes API calls.
    /// </summary>
    public interface IOddsApiClient
    {
        Task<List<OddsEvent>> GetOddsAsync(string sport, string[]? markets = null, string[]? bookmakers = null);

        Task<OddsEvent?> GetEventOddsAsync(string eventId, string sport, string[]? markets = null, string[]? bookmakers = null);

        Task<List<SportEvent>> GetEventsAsync(string? sport = null);

        Task<EventMarkets> GetEventMarketsAsync(string eventId, string sport);

        Task<List<ScoreEvent>> GetScoresAsync(string? sport = null, int daysFrom = 1);

        Task<HistoricalOddsResponse?> GetHistoricalOddsAsync(DateTime snapshotTime, string sport, string[]? markets = null);

        Task<HistoricalEventOddsResponse?> GetHistoricalEventOddsAsync(string eventId, DateTime snapshotTime, string sport, string[]? markets = null, string[]? bookmakers = null);

        Task<List<OddsSnapshot>> GetLineMovementAsync(string sportKey, string eventId, int daysBack = 3, int intervalsPerDay = 4, string[]? markets = null, string[]? bookmakers = null);
    }
}