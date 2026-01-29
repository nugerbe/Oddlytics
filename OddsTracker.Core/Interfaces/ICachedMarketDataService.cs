using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Centralized service for cached access to market repository data.
    /// Eliminates duplicate caching logic across services.
    /// </summary>
    public interface ICachedMarketDataService
    {
        #region Sports

        /// <summary>
        /// Gets all active sports (cached for 30 minutes).
        /// </summary>
        Task<List<Sport>> GetActiveSportsAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets all sports including inactive (cached for 30 minutes).
        /// </summary>
        Task<List<Sport>> GetAllSportsAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets a sport by keyword match in input string.
        /// </summary>
        Task<Sport?> GetSportByKeywordAsync(string input, CancellationToken ct = default);

        #endregion

        #region Markets

        /// <summary>
        /// Gets all markets for a sport (cached for 1 hour).
        /// </summary>
        Task<List<MarketDefinition>> GetMarketsForSportAsync(string sportKey, CancellationToken ct = default);

        /// <summary>
        /// Gets a market by its key (cached for 1 hour).
        /// </summary>
        Task<MarketDefinition?> GetMarketByKeyAsync(string marketKey, CancellationToken ct = default);

        /// <summary>
        /// Gets a market by keyword match in input string, scoped to a sport.
        /// </summary>
        Task<MarketDefinition?> GetMarketByKeywordAsync(string input, string sportKey, CancellationToken ct = default);

        /// <summary>
        /// Checks if a subscription tier can access a specific market.
        /// </summary>
        Task<bool> CanAccessMarketAsync(SubscriptionTier tier, string marketKey, CancellationToken ct = default);

        #endregion

        #region Bookmakers

        /// <summary>
        /// Gets all bookmaker tiers as a dictionary (cached for 1 hour).
        /// Key: bookmaker key, Value: tier.
        /// </summary>
        Task<Dictionary<string, BookmakerTier>> GetBookmakerTiersAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets bookmakers accessible to a subscription tier (cached for 1 hour).
        /// </summary>
        Task<List<BookmakerInfo>> GetAccessibleBookmakersAsync(SubscriptionTier tier, CancellationToken ct = default);

        /// <summary>
        /// Gets a single bookmaker's tier (uses cached dictionary).
        /// </summary>
        Task<BookmakerTier> GetBookmakerTierAsync(string bookmakerKey, CancellationToken ct = default);

        #endregion

        #region Cache Invalidation

        /// <summary>
        /// Invalidates all cached sport data.
        /// </summary>
        Task InvalidateSportsCacheAsync(CancellationToken ct = default);

        /// <summary>
        /// Invalidates all cached market data for a sport.
        /// </summary>
        Task InvalidateMarketsCacheAsync(string? sportKey = null, CancellationToken ct = default);

        /// <summary>
        /// Invalidates all cached bookmaker data.
        /// </summary>
        Task InvalidateBookmakersCacheAsync(CancellationToken ct = default);

        #endregion
    }
}