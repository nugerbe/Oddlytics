using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Centralized service for cached access to market repository data.
    /// 
    /// <para><b>Cache TTLs:</b></para>
    /// <list type="bullet">
    ///   <item>Sports: 30 minutes</item>
    ///   <item>Markets: 1 hour</item>
    ///   <item>Bookmakers: 1 hour</item>
    /// </list>
    /// 
    /// <para><b>Cache Keys:</b></para>
    /// <list type="bullet">
    ///   <item><c>mktdata:sports:all</c></item>
    ///   <item><c>mktdata:sports:active</c></item>
    ///   <item><c>mktdata:markets:sport:{sportKey}</c></item>
    ///   <item><c>mktdata:market:key:{marketKey}</c></item>
    ///   <item><c>mktdata:bookmakers:tiers</c></item>
    ///   <item><c>mktdata:bookmakers:accessible:{tier}</c></item>
    /// </list>
    /// </summary>
    public class CachedMarketDataService(
        IServiceScopeFactory scopeFactory,
        IEnhancedCacheService cache,
        ILogger<CachedMarketDataService> logger) : ICachedMarketDataService
    {
        private static readonly TimeSpan SportsCacheTtl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan MarketsCacheTtl = TimeSpan.FromHours(1);
        private static readonly TimeSpan BookmakersCacheTtl = TimeSpan.FromHours(1);

        private const string CachePrefix = "mktdata";

        #region Sports

        public async Task<List<Sport>> GetActiveSportsAsync(CancellationToken ct = default)
        {
            const string cacheKey = $"{CachePrefix}:sports:active";

            var cached = await cache.GetAsync<List<Sport>>(cacheKey);
            if (cached is not null)
            {
                logger.LogDebug("Cache hit: {Key}", cacheKey);
                return cached;
            }

            logger.LogDebug("Cache miss: {Key}", cacheKey);

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var sports = await repo.GetAllSportsAsync();
            var active = sports.Where(s => s.IsActive).ToList();

            await cache.SetAsync(cacheKey, active, SportsCacheTtl);
            return active;
        }

        public async Task<List<Sport>> GetAllSportsAsync(CancellationToken ct = default)
        {
            const string cacheKey = $"{CachePrefix}:sports:all";

            var cached = await cache.GetAsync<List<Sport>>(cacheKey);
            if (cached is not null)
            {
                logger.LogDebug("Cache hit: {Key}", cacheKey);
                return cached;
            }

            logger.LogDebug("Cache miss: {Key}", cacheKey);

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var sports = await repo.GetAllSportsAsync();

            await cache.SetAsync(cacheKey, sports, SportsCacheTtl);
            return sports;
        }

        public async Task<Sport?> GetSportByKeywordAsync(string input, CancellationToken ct = default)
        {
            var sports = await GetAllSportsAsync(ct);

            foreach (var sport in sports)
            {
                if (input.Contains(sport.Key, StringComparison.OrdinalIgnoreCase))
                    return ToSportDefinition(sport);

                if (sport.Keywords?.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase)) == true)
                    return ToSportDefinition(sport);
            }

            // Default to first active sport (typically NFL)
            var defaultSport = sports.FirstOrDefault(s => s.IsActive);
            return defaultSport is not null ? ToSportDefinition(defaultSport) : null;
        }

        private static Sport ToSportDefinition(Sport sport) => new()
        {
            Key = sport.Key,
            DisplayName = sport.DisplayName,
            IsActive = sport.IsActive,
            Keywords = sport.Keywords ?? []
        };

        #endregion

        #region Markets

        public async Task<List<MarketDefinition>> GetMarketsForSportAsync(string sportKey, CancellationToken ct = default)
        {
            var cacheKey = $"{CachePrefix}:markets:sport:{sportKey}";

            var cached = await cache.GetAsync<List<MarketDefinition>>(cacheKey);
            if (cached is not null)
            {
                logger.LogDebug("Cache hit: {Key}", cacheKey);
                return cached;
            }

            logger.LogDebug("Cache miss: {Key}", cacheKey);

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var markets = await repo.GetMarketsForSportAsync(sportKey);

            await cache.SetAsync(cacheKey, markets, MarketsCacheTtl);
            return markets;
        }

        public async Task<MarketDefinition?> GetMarketByKeyAsync(string marketKey, CancellationToken ct = default)
        {
            var cacheKey = $"{CachePrefix}:market:key:{marketKey}";

            var cached = await cache.GetAsync<MarketDefinition>(cacheKey);
            if (cached is not null)
            {
                logger.LogDebug("Cache hit: {Key}", cacheKey);
                return cached;
            }

            logger.LogDebug("Cache miss: {Key}", cacheKey);

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var market = await repo.GetMarketByKeyAsync(marketKey);

            if (market is not null)
                await cache.SetAsync(cacheKey, market, MarketsCacheTtl);

            return market;
        }

        public async Task<MarketDefinition?> GetMarketByKeywordAsync(string input, string sportKey, CancellationToken ct = default)
        {
            var markets = await GetMarketsForSportAsync(sportKey, ct);

            foreach (var market in markets)
            {
                if (market.Keywords.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return market;
            }

            // Default to spreads
            return markets.FirstOrDefault(m => m.Key.Contains("spread", StringComparison.OrdinalIgnoreCase))
                ?? markets.FirstOrDefault();
        }

        public async Task<bool> CanAccessMarketAsync(SubscriptionTier tier, string marketKey, CancellationToken ct = default)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            return repo.CanAccessMarket(tier, marketKey);
        }

        #endregion

        #region Bookmakers

        public async Task<Dictionary<string, BookmakerTier>> GetBookmakerTiersAsync(CancellationToken ct = default)
        {
            const string cacheKey = $"{CachePrefix}:bookmakers:tiers";

            var cached = await cache.GetAsync<Dictionary<string, BookmakerTier>>(cacheKey);
            if (cached is not null)
            {
                logger.LogDebug("Cache hit: {Key}", cacheKey);
                return cached;
            }

            logger.LogDebug("Cache miss: {Key}", cacheKey);

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var bookmakers = await repo.GetAllBookmakersAsync();
            var tiers = bookmakers.ToDictionary(
                b => b.Key,
                b => b.Tier,
                StringComparer.OrdinalIgnoreCase);

            await cache.SetAsync(cacheKey, tiers, BookmakersCacheTtl);
            return tiers;
        }

        public async Task<List<BookmakerInfo>> GetAccessibleBookmakersAsync(SubscriptionTier tier, CancellationToken ct = default)
        {
            var cacheKey = $"{CachePrefix}:bookmakers:accessible:{tier}";

            var cached = await cache.GetAsync<List<BookmakerInfo>>(cacheKey);
            if (cached is not null)
            {
                logger.LogDebug("Cache hit: {Key}", cacheKey);
                return cached;
            }

            logger.LogDebug("Cache miss: {Key}", cacheKey);

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var bookmakers = await repo.GetAccessibleBookmakersAsync(tier);

            await cache.SetAsync(cacheKey, bookmakers, BookmakersCacheTtl);
            return bookmakers;
        }

        public async Task<BookmakerTier> GetBookmakerTierAsync(string bookmakerKey, CancellationToken ct = default)
        {
            var tiers = await GetBookmakerTiersAsync(ct);

            return tiers.TryGetValue(bookmakerKey, out var tier)
                ? tier
                : BookmakerTier.Retail;
        }

        #endregion

        #region Cache Invalidation

        public async Task InvalidateSportsCacheAsync(CancellationToken ct = default)
        {
            await cache.RemoveAsync($"{CachePrefix}:sports:all");
            await cache.RemoveAsync($"{CachePrefix}:sports:active");

            logger.LogInformation("Invalidated sports cache");
        }

        public async Task InvalidateMarketsCacheAsync(string? sportKey = null, CancellationToken ct = default)
        {
            if (sportKey is not null)
            {
                await cache.RemoveAsync($"{CachePrefix}:markets:sport:{sportKey}");
                logger.LogInformation("Invalidated markets cache for sport: {SportKey}", sportKey);
            }
            else
            {
                // Without Redis SCAN, we can only invalidate if we know the sport keys
                var sports = await GetAllSportsAsync(ct);
                foreach (var sport in sports)
                {
                    await cache.RemoveAsync($"{CachePrefix}:markets:sport:{sport.Key}");
                }
                logger.LogInformation("Invalidated all markets cache");
            }
        }

        public async Task InvalidateBookmakersCacheAsync(CancellationToken ct = default)
        {
            await cache.RemoveAsync($"{CachePrefix}:bookmakers:tiers");

            // Invalidate all tier-specific caches
            foreach (var tier in Enum.GetValues<SubscriptionTier>())
            {
                await cache.RemoveAsync($"{CachePrefix}:bookmakers:accessible:{tier}");
            }

            logger.LogInformation("Invalidated bookmakers cache");
        }

        #endregion
    }
}