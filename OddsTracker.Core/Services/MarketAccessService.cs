using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services
{
    public class MarketAccessService(
        IOddsApiClient oddsClient,
        IEnhancedCacheService cache,
        ILogger<MarketAccessService> logger) : IMarketAccessService
    {
        private static readonly TimeSpan MarketsCacheDuration = TimeSpan.FromMinutes(5);

        // Bookmaker classifications by tier
        // Starter: Retail only (consumer-focused books)
        // Core: Retail + Market makers
        // Sharp: All books including sharp/offshore

        private static readonly HashSet<string> RetailBookmakers = new(StringComparer.OrdinalIgnoreCase)
        {
            "draftkings",
            "fanduel",
            "fanatics",
            "betrivers"
        };

        private static readonly HashSet<string> MarketBookmakers = new(StringComparer.OrdinalIgnoreCase)
        {
            "betmgm",
            "williamhill_us"  // Caesars
        };

        private static readonly HashSet<string> SharpBookmakers = new(StringComparer.OrdinalIgnoreCase)
        {
            "lowvig",
            "betonlineag",
            "bovada",
            "mybookieag",
            "betus"
        };

        #region Market Access

        public bool CanAccessMarket(SubscriptionTier userTier, MarketType marketType)
        {
            var requiredTier = marketType.RequiredTier();
            return userTier >= requiredTier;
        }

        public IEnumerable<MarketType> GetAccessibleMarkets(SubscriptionTier userTier)
        {
            return Enum.GetValues<MarketType>()
                .Where(m => CanAccessMarket(userTier, m));
        }

        #endregion

        #region Bookmaker Access

        public bool CanAccessBookmaker(SubscriptionTier userTier, string bookmakerKey)
        {
            var requiredTier = GetBookmakerRequiredTier(bookmakerKey);
            return userTier >= requiredTier;
        }

        public IEnumerable<string> GetAccessibleBookmakers(SubscriptionTier userTier)
        {
            var accessible = new List<string>();

            // Starter and above: Retail
            accessible.AddRange(RetailBookmakers);

            // Core and above: Market makers
            if (userTier >= SubscriptionTier.Core)
            {
                accessible.AddRange(MarketBookmakers);
            }

            // Sharp only: Sharp/offshore books
            if (userTier >= SubscriptionTier.Sharp)
            {
                accessible.AddRange(SharpBookmakers);
            }

            return accessible;
        }

        public string[] FilterBookmakersByTier(SubscriptionTier userTier, string[]? bookmakers = null)
        {
            var accessibleBooks = GetAccessibleBookmakers(userTier).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (bookmakers is null || bookmakers.Length == 0)
            {
                return accessibleBooks.ToArray();
            }

            return bookmakers
                .Where(b => accessibleBooks.Contains(b))
                .ToArray();
        }

        private static SubscriptionTier GetBookmakerRequiredTier(string bookmakerKey)
        {
            if (RetailBookmakers.Contains(bookmakerKey))
                return SubscriptionTier.Starter;

            if (MarketBookmakers.Contains(bookmakerKey))
                return SubscriptionTier.Core;

            if (SharpBookmakers.Contains(bookmakerKey))
                return SubscriptionTier.Sharp;

            // Unknown bookmakers default to Sharp tier
            return SubscriptionTier.Sharp;
        }

        #endregion

        #region Event Markets

        public async Task<bool> IsMarketAvailableAsync(string eventId, MarketType marketType)
        {
            var markets = await GetAvailableMarketsAsync(eventId);
            var apiKey = marketType.ToApiKey();

            return markets.Markets.Any(m => m.MarketKey == apiKey);
        }

        public async Task<bool> IsMarketAvailableAsync(string eventId, MarketType marketType, string bookmakerKey)
        {
            var markets = await GetAvailableMarketsAsync(eventId);
            var apiKey = marketType.ToApiKey();

            return markets.Markets.Any(m =>
                m.MarketKey == apiKey &&
                m.BookmakerKey.Equals(bookmakerKey, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<EventMarkets> GetAvailableMarketsAsync(string eventId)
        {
            var cacheKey = $"event_markets:{eventId}";

            var cached = await cache.GetAsync<EventMarkets>(cacheKey);
            if (cached is not null)
            {
                return cached;
            }

            var response = await oddsClient.GetEventMarketsAsync(eventId);

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

            await cache.SetAsync(cacheKey, eventMarkets, MarketsCacheDuration);

            logger.LogDebug(
                "Cached {Count} markets for event {EventId}",
                eventMarkets.Markets.Count,
                eventId);

            return eventMarkets;
        }

        public async Task<EventMarkets> GetAccessibleMarketsForEventAsync(string eventId, SubscriptionTier userTier)
        {
            var allMarkets = await GetAvailableMarketsAsync(eventId);

            // Filter by both market type AND bookmaker tier
            var accessibleMarkets = allMarkets.Markets
                .Where(m =>
                    m.MarketType.HasValue &&
                    CanAccessMarket(userTier, m.MarketType.Value) &&
                    CanAccessBookmaker(userTier, m.BookmakerKey))
                .ToList();

            return new EventMarkets
            {
                EventId = allMarkets.EventId,
                HomeTeam = allMarkets.HomeTeam,
                AwayTeam = allMarkets.AwayTeam,
                CommenceTime = allMarkets.CommenceTime,
                Markets = accessibleMarkets
            };
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for bookmaker classification
    /// </summary>
    public static class BookmakerExtensions
    {
        public static BookmakerTier GetTier(string bookmakerKey)
        {
            return bookmakerKey.ToLowerInvariant() switch
            {
                "draftkings" or "fanduel" or "fanatics" or "betrivers" => BookmakerTier.Retail,
                "betmgm" or "williamhill_us" => BookmakerTier.Market,
                "lowvig" or "betonlineag" or "bovada" or "mybookieag" or "betus" => BookmakerTier.Sharp,
                _ => BookmakerTier.Sharp  // Unknown defaults to Sharp
            };
        }
    }
}