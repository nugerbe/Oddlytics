using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Extended cache service interface with typed methods for platform models.
    /// </summary>
    public interface IEnhancedCacheService : ICacheService
    {
        Task<MarketFingerprint?> GetFingerprintAsync(string eventId, MarketType marketType);
        Task SetFingerprintAsync(MarketFingerprint fingerprint);

        Task<ConfidenceScore?> GetConfidenceScoreAsync(string marketKey);
        Task SetConfidenceScoreAsync(ConfidenceScore score);

        Task<string?> GetAIExplanationAsync(string promptHash);
        Task SetAIExplanationAsync(string promptHash, string explanation);

        Task<UserSubscription?> GetUserSubscriptionAsync(ulong userId);
        Task SetUserSubscriptionAsync(UserSubscription subscription);

        Task<RateLimitEntry?> GetRateLimitAsync(ulong userId);
        Task SetRateLimitAsync(RateLimitEntry entry);

        Task InvalidateMarketAsync(string eventId, MarketType marketType);
        Task InvalidateEventAsync(string eventId);
        Task InvalidateUserAsync(ulong userId);
        Task RemoveAsync(string key);
    }
}
