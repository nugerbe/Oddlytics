using Microsoft.EntityFrameworkCore;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Data
{
    public class MarketRepository(OddsTrackerDbContext context) : IMarketRepository
    {
        public async Task<List<Sport>> GetAllSportsAsync()
        {
            var sports = await context.Sports
                .AsNoTracking()
                .Include(s => s.SportMarkets)
                    .ThenInclude(sm => sm.MarketDefinition)
                .Where(s => s.IsActive)
                .ToListAsync();

            return [.. sports.Select(s => s.ToModel())];
        }

        public async Task<Sport?> GetSportByKeyAsync(string sportKey)
        {
            var sports = await GetAllSportsAsync();
            return sports.FirstOrDefault(s => s.Key.Equals(sportKey, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<Sport>> GetSportsByCategoryAsync(SportCategory category)
        {
            var sports = await GetAllSportsAsync();
            return [.. sports.Where(s => s.Category == category)];
        }

        public async Task<List<MarketDefinition>> GetAllMarketsAsync()
        {
            var markets = await context.MarketDefinitions
                .Where(m => m.IsActive)
                .AsNoTracking()
                .ToListAsync();

            return markets?.Select(m => m.ToModel()).ToList() ?? [];
        }

        public async Task<List<MarketDefinition>> GetMarketsForSportAsync(string sportKey)
        {
            var sport = await context.Sports
                .AsNoTracking()
                .Include(s => s.SportMarkets)
                .FirstOrDefaultAsync(s => s.Key.Equals(sportKey, StringComparison.OrdinalIgnoreCase));

            return sport?.ToModel().AvailableMarkets.ToList() ?? [];
        }

        public async Task<List<MarketDefinition>> GetPlayerPropsAsync()
        {
            var markets = await GetAllMarketsAsync();
            return [.. markets.Where(m => m.IsPlayerProp)];
        }

        public async Task<List<BookmakerInfo>> GetAllBookmakersAsync()
        {
            var bookmakers = await context.Bookmakers
                .Where(b => b.IsActive)
                .AsNoTracking()
                .ToListAsync();

            return bookmakers?.Select(b => b.ToModel()).ToList() ?? [];
        }

        public async Task<MarketDefinition?> GetMarketByKeyAsync(string marketKey)
        {
            var markets = await GetAllMarketsAsync();
            return markets.FirstOrDefault(m => m.Key.Equals(marketKey, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<MarketDefinition>?> GetMarketByCategoryAsync(MarketCategory category)
        {
            var markets = await GetAllMarketsAsync();
            return [.. markets.Where(m => m.Category == category)];
        }

        /// <summary>
        /// Find a market by matching keywords against user input for a specific sport.
        /// Prioritizes more specific matches (player props, period-specific) over general ones.
        /// Returns null if no match found.
        /// </summary>
        public async Task<MarketDefinition?> GetMarketByKeywordAsync(string input, string? sportKey = null)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var normalizedInput = input.ToLowerInvariant();

            // Get markets scoped to sport if provided, otherwise all markets
            var markets = string.IsNullOrEmpty(sportKey)
                ? await GetAllMarketsAsync()
                : await GetMarketsForSportAsync(sportKey);

            // Find all matching markets
            var matches = markets
                .Where(m => m.MatchesKeyword(normalizedInput))
                .ToList();

            if (matches.Count == 0)
                return null;

            // Prioritize matches (most specific first):
            // 1. Player props (most specific)
            // 2. Period-specific markets (1H, 2H, quarters)
            // 3. Alternates
            // 4. Base game lines (least specific)
            return matches
                .OrderByDescending(m => m.IsPlayerProp)
                .ThenByDescending(m => m.Period.HasValue)
                .ThenByDescending(m => m.IsAlternate)
                .ThenByDescending(m => GetLongestMatchingKeyword(m, normalizedInput))
                .First();
        }

        public async Task<Sport?> GetSportByKeywordAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var normalizedInput = input.ToLowerInvariant();

            var sports = await GetAllSportsAsync();

            var matches = sports
                .Where(s => s.MatchesKeyword(normalizedInput))
                .ToList();

            if (matches.Count == 0)
                return null;

            return matches
                .OrderByDescending(s => GetLongestMatchingKeyword(s, normalizedInput))
                .First();
        }

        public async Task<BookmakerInfo?> GetBookmakerByKeyAsync(string bookmakerKey)
        {
            var bookmakers = await GetAllBookmakersAsync();
            return bookmakers.FirstOrDefault(b =>
                b.Key.Equals(bookmakerKey, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<BookmakerInfo>> GetBookmakersByTierAsync(BookmakerTier tier)
        {
            var bookmakers = await GetAllBookmakersAsync();
            return [.. bookmakers.Where(b => b.Tier == tier)];
        }

        public async Task<List<MarketDefinition>> GetAccessibleMarketsAsync(SubscriptionTier tier)
        {
            var allMarkets = await GetAllMarketsAsync();
            return [.. allMarkets.Where(m => m.RequiredTier <= tier)];
        }

        public async Task<List<BookmakerInfo>> GetAccessibleBookmakersAsync(SubscriptionTier tier)
        {
            var allBookmakers = await GetAllBookmakersAsync();

            return tier switch
            {
                SubscriptionTier.Sharp => [.. allBookmakers],
                SubscriptionTier.Core => [.. allBookmakers.Where(b => b.Tier <= BookmakerTier.Market)],
                _ => [.. allBookmakers.Where(b => b.Tier == BookmakerTier.Retail)]
            };
        }

        public bool CanAccessMarket(SubscriptionTier tier, string marketKey)
        {
            // Quick sync check for common markets
            var isPlayerProp = marketKey.StartsWith("player_");
            var isAlternate = marketKey.Contains("alternate") || marketKey.Contains("_alt_");

            return tier switch
            {
                SubscriptionTier.Sharp => true,
                SubscriptionTier.Core => !isPlayerProp,
                _ => !isPlayerProp && !isAlternate
            };
        }

        public bool CanAccessBookmaker(SubscriptionTier tier, string bookmakerKey)
        {
            var bookmakerTier = GetBookmakerTier(bookmakerKey);

            return tier switch
            {
                SubscriptionTier.Sharp => true,
                SubscriptionTier.Core => bookmakerTier <= BookmakerTier.Market,
                _ => bookmakerTier == BookmakerTier.Retail
            };
        }

        public BookmakerTier GetBookmakerTier(string bookmakerKey)
        {
            var matchingTier = context.Bookmakers
                .AsNoTracking()
                .FirstOrDefault(b => b.Key.Equals(bookmakerKey, StringComparison.OrdinalIgnoreCase))?
                .Tier;

            if (matchingTier == null) return BookmakerTier.Retail;
            BookmakerTier tier = Enum.Parse<BookmakerTier>(matchingTier);

            return tier;
        }

        #region Default Getters

        public async Task<string[]> GetDefaultMarketsAsync(SubscriptionTier tier = SubscriptionTier.Starter)
        {
            var markets = await GetAccessibleMarketsAsync(tier);
            var keys = markets
                .Where(m => !m.IsPlayerProp && !m.IsAlternate)
                .Select(m => m.Key)
                .ToArray();

            return keys.Length > 0 ? keys : ["h2h", "spreads", "totals"];
        }

        public async Task<string[]> GetDefaultBookmakersAsync(SubscriptionTier tier = SubscriptionTier.Starter)
        {
            var bookmakers = await GetAccessibleBookmakersAsync(tier);
            return [.. bookmakers.Select(b => b.Key)];
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Get the length of the longest keyword that matches the input.
        /// Used for prioritizing more specific matches.
        /// </summary>
        private static int GetLongestMatchingKeyword(Sport sport, string input) =>
            sport.Keywords
                .Where(k => input.Contains(k, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Length)
                .DefaultIfEmpty(0)
                .Max();

        private static int GetLongestMatchingKeyword(MarketDefinition market, string input) =>
            market.Keywords
                .Where(k => input.Contains(k, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Length)
                .DefaultIfEmpty(0)
                .Max();

        #endregion
    }
}
