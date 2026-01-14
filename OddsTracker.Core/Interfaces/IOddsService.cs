using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IOddsService
    {
        Task<List<NormalizedOdds>> GetOddsAsync(OddsQuery query, SubscriptionTier userTier);
    }
}
