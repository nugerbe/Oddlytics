using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IOddsOrchestrator
    {
        Task<OddsQueryResult> ProcessQueryAsync(string userMessage, SubscriptionTier userTier);
    }
}
