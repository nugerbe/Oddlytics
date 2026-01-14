using Microsoft.EntityFrameworkCore;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Data
{
    /// <summary>
    /// EF Core implementation of subscription repository.
    /// </summary>
    public class EfSubscriptionRepository(OddsTrackerDbContext context) : ISubscriptionRepository
    {
        public async Task<UserSubscription?> GetByDiscordIdAsync(ulong discordUserId)
        {
            var entity = await context.UserSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.DiscordUserId == discordUserId);

            return entity?.ToModel();
        }

        public async Task<UserSubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId)
        {
            var entity = await context.UserSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.StripeCustomerId == stripeCustomerId);

            return entity?.ToModel();
        }

        public async Task SaveAsync(UserSubscription subscription)
        {
            var existing = await context.UserSubscriptions
                .FirstOrDefaultAsync(u => u.DiscordUserId == subscription.DiscordUserId);

            if (existing is null)
            {
                // Insert
                var entity = UserSubscriptionEntity.FromModel(subscription);
                context.UserSubscriptions.Add(entity);
            }
            else
            {
                // Update
                existing.StripeCustomerId = subscription.StripeCustomerId;
                existing.StripeSubscriptionId = subscription.StripeSubscriptionId;
                existing.Tier = subscription.Tier;
                existing.SubscriptionStart = subscription.SubscriptionStart;
                existing.SubscriptionEnd = subscription.SubscriptionEnd;
                existing.GracePeriodEnd = subscription.GracePeriodEnd;
                existing.QueriesUsedToday = subscription.QueriesUsedToday;
                existing.LastQueryDate = subscription.LastQueryDate;
            }

            await context.SaveChangesAsync();
        }
    }
}