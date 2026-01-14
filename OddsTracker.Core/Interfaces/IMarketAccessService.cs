using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IMarketAccessService
    {
        /// <summary>
        /// Check if a user can access a specific market type
        /// </summary>
        bool CanAccessMarket(SubscriptionTier userTier, MarketType marketType);

        /// <summary>
        /// Check if a user can access a specific bookmaker
        /// </summary>
        bool CanAccessBookmaker(SubscriptionTier userTier, string bookmakerKey);

        /// <summary>
        /// Get all market types available to a user's tier
        /// </summary>
        IEnumerable<MarketType> GetAccessibleMarkets(SubscriptionTier userTier);

        /// <summary>
        /// Get all bookmakers available to a user's tier
        /// </summary>
        IEnumerable<string> GetAccessibleBookmakers(SubscriptionTier userTier);

        /// <summary>
        /// Filter bookmakers list to only those accessible by tier
        /// </summary>
        string[] FilterBookmakersByTier(SubscriptionTier userTier, string[]? bookmakers = null);

        /// <summary>
        /// Check if a market is available for an event across any bookmaker
        /// </summary>
        Task<bool> IsMarketAvailableAsync(string eventId, MarketType marketType);

        /// <summary>
        /// Check if a market is available for an event at a specific bookmaker
        /// </summary>
        Task<bool> IsMarketAvailableAsync(string eventId, MarketType marketType, string bookmakerKey);

        /// <summary>
        /// Get all available markets for an event
        /// </summary>
        Task<EventMarkets> GetAvailableMarketsAsync(string eventId);

        /// <summary>
        /// Get markets available to user for an event (filtered by tier for both markets and bookmakers)
        /// </summary>
        Task<EventMarkets> GetAccessibleMarketsForEventAsync(string eventId, SubscriptionTier userTier);
    }
}
