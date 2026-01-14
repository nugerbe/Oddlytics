using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface ITierEnforcer
    {
        int GetHistoricalDaysAllowed(SubscriptionTier tier);
        bool CanAccessFeature(SubscriptionTier tier, string feature);
        List<AlertChannel> GetAllowedChannels(SubscriptionTier tier);
    }
}
