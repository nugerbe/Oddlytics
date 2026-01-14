using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Repository for subscription data
    /// </summary>
    public interface ISubscriptionRepository
    {
        Task<UserSubscription?> GetByDiscordIdAsync(ulong discordUserId);
        Task<UserSubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId);
        Task SaveAsync(UserSubscription subscription);
    }
}
