using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Extended cache service interface with typed methods for platform models.
    /// </summary>
    public interface IEnhancedCacheService : ICacheService
    {
        /// <summary>
        /// Get cached market fingerprint by event ID and market key
        /// </summary>
        Task<MarketFingerprint?> GetFingerprintAsync(string eventId, string marketKey);
        Task SetFingerprintAsync(MarketFingerprint fingerprint);

        /// <summary>
        /// Get cached confidence score by market key
        /// </summary>
        Task<ConfidenceScore?> GetConfidenceScoreAsync(string marketKey);
        Task SetConfidenceScoreAsync(ConfidenceScore score);

        /// <summary>
        /// Get cached AI explanation by prompt hash
        /// </summary>
        Task<string?> GetAIExplanationAsync(string promptHash);
        Task SetAIExplanationAsync(string promptHash, string explanation);

        /// <summary>
        /// Get cached user subscription
        /// </summary>
        Task<UserSubscription?> GetUserSubscriptionAsync(ulong userId);
        Task SetUserSubscriptionAsync(UserSubscription subscription);

        /// <summary>
        /// Get cached rate limit entry
        /// </summary>
        Task<RateLimitEntry?> GetRateLimitAsync(ulong userId);
        Task SetRateLimitAsync(RateLimitEntry entry);

        Task<List<SignalSnapshot>?> GetSignalsForEventAsync(string eventId, string marketKey);
        Task SetSignalsForEventAsync(string eventId, string marketKey, List<SignalSnapshot> signals);

        /// <summary>
        /// Invalidate all caches for a specific market
        /// </summary>
        Task InvalidateMarketAsync(string eventId, string marketKey);

        /// <summary>
        /// Invalidate all caches for an event (all markets)
        /// </summary>
        Task InvalidateEventAsync(string eventId, IEnumerable<string>? marketKeys = null);

        /// <summary>
        /// Invalidate all user-related caches
        /// </summary>
        Task InvalidateUserAsync(ulong userId);

        Task RemoveAsync(string key);
    }
}
