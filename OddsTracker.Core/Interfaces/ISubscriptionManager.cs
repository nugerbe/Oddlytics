using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface ISubscriptionManager
    {
        Task<UserSubscription> GetOrCreateSubscriptionAsync(ulong discordUserId);
        Task<bool> CanPerformQueryAsync(ulong discordUserId);
        Task RecordQueryAsync(ulong discordUserId);
        Task<bool> HasChannelAccessAsync(ulong discordUserId, AlertChannel channel);
        Task UpdateTierAsync(ulong discordUserId, SubscriptionTier tier);
    }
}
