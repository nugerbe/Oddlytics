using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Text.Json;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Enhanced cache service with typed methods for platform models and invalidation support.
    /// 
    /// <para><b>Redis Key Schema:</b></para>
    /// <list type="bullet">
    ///   <item>Raw odds: <c>odds:raw:{eventId}:{marketKey}</c></item>
    ///   <item>Fingerprints: <c>fingerprint:{eventId}:{marketKey}</c></item>
    ///   <item>Confidence scores: <c>confidence:{marketKey}</c></item>
    ///   <item>AI explanations: <c>ai:explain:{promptHash}</c></item>
    ///   <item>User subscriptions: <c>subscription:{userId}</c></item>
    ///   <item>Rate limits: <c>ratelimit:{userId}</c></item>
    ///   <item>Charts: <c>chart:{cacheKey}:{side}</c></item>
    /// </list>
    /// 
    /// <para><b>Invalidation Rules:</b></para>
    /// <list type="bullet">
    ///   <item>New odds arrive → Invalidate raw odds + fingerprint + confidence</item>
    ///   <item>Market closes → Invalidate all caches for that event</item>
    ///   <item>Subscription changes → Invalidate user subscription cache</item>
    /// </list>
    /// </summary>
    public class EnhancedCacheService(
        IDistributedCache cache,
        ILogger<EnhancedCacheService> logger,
        IOptions<CacheOptions>? options = null) : IEnhancedCacheService
    {
        private readonly CacheOptions _options = options?.Value ?? new CacheOptions();

        public bool IsEnabled => _options.Enabled;

        #region Generic Cache Operations

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            if (!_options.Enabled)
                return null;

            try
            {
                var data = await cache.GetStringAsync(key);
                if (data is null)
                    return null;

                logger.LogDebug("Cache hit: {Key}", key);
                return JsonSerializer.Deserialize<T>(data);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache read error: {Key}", key);
                return null;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
        {
            if (!_options.Enabled)
                return;

            try
            {
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(_options.DefaultTtlMinutes)
                };

                var data = JsonSerializer.Serialize(value);
                await cache.SetStringAsync(key, data, cacheOptions);
                logger.LogDebug("Cache set: {Key}, TTL: {TTL}", key, expiry ?? TimeSpan.FromMinutes(_options.DefaultTtlMinutes));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache write error: {Key}", key);
            }
        }

        public async Task<byte[]?> GetBytesAsync(string key)
        {
            if (!_options.Enabled)
                return null;

            try
            {
                return await cache.GetAsync(key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache read error (bytes): {Key}", key);
                return null;
            }
        }

        public async Task SetBytesAsync(string key, byte[] value, TimeSpan? expiry = null)
        {
            if (!_options.Enabled)
                return;

            try
            {
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(_options.DefaultTtlMinutes)
                };

                await cache.SetAsync(key, value, cacheOptions);
                logger.LogDebug("Cache set (bytes): {Key}, TTL: {TTL}", key, expiry ?? TimeSpan.FromMinutes(_options.DefaultTtlMinutes));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache write error (bytes): {Key}", key);
            }
        }

        public async Task RemoveAsync(string key)
        {
            if (!_options.Enabled)
                return;

            try
            {
                await cache.RemoveAsync(key);
                logger.LogDebug("Cache removed: {Key}", key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache remove error: {Key}", key);
            }
        }

        #endregion

        #region Fingerprint Cache

        public async Task<MarketFingerprint?> GetFingerprintAsync(string eventId, string marketKey) =>
            await GetAsync<MarketFingerprint>($"fingerprint:{eventId}:{marketKey}");

        public async Task SetFingerprintAsync(MarketFingerprint fingerprint) =>
            await SetAsync(
                $"fingerprint:{fingerprint.Market.EventId}:{fingerprint.Market.MarketType.Key}",
                fingerprint,
                TimeSpan.FromHours(_options.FingerprintTtlHours));

        #endregion

        #region Confidence Score Cache

        public async Task<ConfidenceScore?> GetConfidenceScoreAsync(string marketKey) =>
            await GetAsync<ConfidenceScore>($"confidence:{marketKey}");

        public async Task SetConfidenceScoreAsync(ConfidenceScore score) =>
            await SetAsync(
                $"confidence:{score.MarketKey}",
                score,
                TimeSpan.FromMinutes(_options.ConfidenceTtlMinutes));

        #endregion

        #region AI Explanation Cache

        public async Task<string?> GetAIExplanationAsync(string promptHash) =>
            await GetAsync<string>($"ai:explain:{promptHash}");

        public async Task SetAIExplanationAsync(string promptHash, string explanation) =>
            await SetAsync(
                $"ai:explain:{promptHash}",
                explanation,
                TimeSpan.FromMinutes(_options.AIExplanationTtlMinutes));

        #endregion

        #region User Subscription Cache

        public async Task<UserSubscription?> GetUserSubscriptionAsync(ulong userId) =>
            await GetAsync<UserSubscription>($"subscription:{userId}");

        public async Task SetUserSubscriptionAsync(UserSubscription subscription) =>
            await SetAsync(
                $"subscription:{subscription.DiscordUserId}",
                subscription,
                TimeSpan.FromMinutes(_options.SubscriptionTtlMinutes));

        #endregion

        #region Rate Limit Cache

        public async Task<RateLimitEntry?> GetRateLimitAsync(ulong userId) =>
            await GetAsync<RateLimitEntry>($"ratelimit:{userId}");

        public async Task SetRateLimitAsync(RateLimitEntry entry)
        {
            var ttl = entry.WindowEnd - DateTime.UtcNow;
            var effectiveTtl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromHours(24);
            await SetAsync($"ratelimit:{entry.UserId}", entry, effectiveTtl);
        }

        #endregion

        #region Invalidation

        public async Task InvalidateMarketAsync(string eventId, string marketKey)
        {
            if (!_options.Enabled)
                return;

            string[] keys =
            [
                $"fingerprint:{eventId}:{marketKey}",
                $"confidence:{eventId}:{marketKey}",
                $"odds:raw:{eventId}:{marketKey}"
            ];

            foreach (var key in keys)
            {
                await cache.RemoveAsync(key);
            }

            logger.LogInformation("Invalidated cache for market: {EventId}:{MarketKey}", eventId, marketKey);
        }

        public async Task InvalidateEventAsync(string eventId, IEnumerable<string>? marketKeys = null)
        {
            if (!_options.Enabled)
                return;

            if (marketKeys is not null)
            {
                // Invalidate specific markets
                foreach (var marketKey in marketKeys)
                {
                    await InvalidateMarketAsync(eventId, marketKey);
                }
            }
            else
            {
                // Without market keys, we can only log a warning
                // In production, you'd want to track active markets per event
                logger.LogWarning(
                    "InvalidateEventAsync called without market keys for {EventId}. " +
                    "Consider passing market keys for complete invalidation.",
                    eventId);
            }

            logger.LogInformation("Invalidated caches for event: {EventId}", eventId);
        }

        public async Task InvalidateUserAsync(ulong userId)
        {
            if (!_options.Enabled)
                return;

            string[] keys =
            [
                $"subscription:{userId}",
                $"ratelimit:{userId}"
            ];

            foreach (var key in keys)
            {
                await cache.RemoveAsync(key);
            }

            logger.LogInformation("Invalidated cache for user: {UserId}", userId);
        }

        #endregion
    }
}