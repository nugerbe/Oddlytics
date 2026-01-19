using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IMarketRepository
    {
        Task<List<Sport>> GetAllSportsAsync();

        Task<Sport?> GetSportByKeyAsync(string sportKey);

        Task<List<Sport>> GetSportsByCategoryAsync(SportCategory category);

        Task<List<MarketDefinition>> GetAllMarketsAsync();

        Task<List<MarketDefinition>> GetMarketsForSportAsync(string sportKey);

        Task<List<MarketDefinition>> GetPlayerPropsAsync();

        Task<List<BookmakerInfo>> GetAllBookmakersAsync();

        Task<MarketDefinition?> GetMarketByKeywordAsync(string input, string? sportKey = null);

        Task<Sport?> GetSportByKeywordAsync(string input);

        Task<BookmakerInfo?> GetBookmakerByKeyAsync(string bookmakerKey);

        Task<List<BookmakerInfo>> GetBookmakersByTierAsync(BookmakerTier tier);

        Task<MarketDefinition?> GetMarketByKeyAsync(string marketKey);

        Task<List<MarketDefinition>?> GetMarketByCategoryAsync(MarketCategory category);

        Task<List<MarketDefinition>> GetAccessibleMarketsAsync(SubscriptionTier tier);

        Task<List<BookmakerInfo>> GetAccessibleBookmakersAsync(SubscriptionTier tier);

        bool CanAccessMarket(SubscriptionTier tier, string marketKey);

        bool CanAccessBookmaker(SubscriptionTier tier, string bookmakerKey);

        BookmakerTier GetBookmakerTier(string bookmakerKey);

        Task<string[]> GetDefaultMarketsAsync(SubscriptionTier tier = SubscriptionTier.Starter);

        Task<string[]> GetDefaultBookmakersAsync(SubscriptionTier tier = SubscriptionTier.Starter);
    }
}
