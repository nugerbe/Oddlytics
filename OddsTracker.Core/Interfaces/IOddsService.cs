using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IOddsService
    {
        Task<List<OddsBase>> GetOddsAsync(OddsQueryBase query, SubscriptionTier userTier);
    }
}
