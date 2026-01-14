using OddsTracker.Core.Models;

namespace OddsTracker.Core.Models
{
    /// <summary>
    /// Database entity for user subscriptions.
    /// </summary>
    public class UserSubscriptionEntity
    {
        public long Id { get; set; }
        public ulong DiscordUserId { get; set; }
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public SubscriptionTier Tier { get; set; } = SubscriptionTier.Starter;
        public DateTime? SubscriptionStart { get; set; }
        public DateTime? SubscriptionEnd { get; set; }
        public DateTime? GracePeriodEnd { get; set; }
        public int QueriesUsedToday { get; set; }
        public DateTime LastQueryDate { get; set; } = DateTime.UtcNow.Date;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public UserSubscription ToModel() => new()
        {
            DiscordUserId = DiscordUserId,
            StripeCustomerId = StripeCustomerId,
            StripeSubscriptionId = StripeSubscriptionId,
            Tier = Tier,
            SubscriptionStart = SubscriptionStart,
            SubscriptionEnd = SubscriptionEnd,
            GracePeriodEnd = GracePeriodEnd,
            QueriesUsedToday = QueriesUsedToday,
            LastQueryDate = LastQueryDate
        };

        public static UserSubscriptionEntity FromModel(UserSubscription model) => new()
        {
            DiscordUserId = model.DiscordUserId,
            StripeCustomerId = model.StripeCustomerId,
            StripeSubscriptionId = model.StripeSubscriptionId,
            Tier = model.Tier,
            SubscriptionStart = model.SubscriptionStart,
            SubscriptionEnd = model.SubscriptionEnd,
            GracePeriodEnd = model.GracePeriodEnd,
            QueriesUsedToday = model.QueriesUsedToday,
            LastQueryDate = model.LastQueryDate
        };
    }
}
