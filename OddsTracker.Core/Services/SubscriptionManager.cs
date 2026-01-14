using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Manages user subscriptions with caching and tier enforcement.
    /// </summary>
    public class SubscriptionManager(
        IEnhancedCacheService cache,
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionManager> logger) : ISubscriptionManager
    {
        public async Task<UserSubscription> GetOrCreateSubscriptionAsync(ulong discordUserId)
        {
            // Check cache first
            var cached = await cache.GetUserSubscriptionAsync(discordUserId);
            if (cached is not null)
            {
                // Reset daily counter if new day
                if (cached.LastQueryDate.Date < DateTime.UtcNow.Date)
                {
                    cached.QueriesUsedToday = 0;
                    cached.LastQueryDate = DateTime.UtcNow.Date;
                    await cache.SetUserSubscriptionAsync(cached);
                }
                return cached;
            }

            // Try database
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetService<ISubscriptionRepository>();

            UserSubscription? subscription = null;
            if (repository is not null)
            {
                subscription = await repository.GetByDiscordIdAsync(discordUserId);
            }

            if (subscription is null)
            {
                // Create new starter subscription
                subscription = new UserSubscription
                {
                    DiscordUserId = discordUserId,
                    Tier = SubscriptionTier.Starter,
                    LastQueryDate = DateTime.UtcNow.Date
                };

                if (repository is not null)
                {
                    await repository.SaveAsync(subscription);
                }
            }

            // Reset daily counter if new day
            if (subscription.LastQueryDate.Date < DateTime.UtcNow.Date)
            {
                subscription.QueriesUsedToday = 0;
                subscription.LastQueryDate = DateTime.UtcNow.Date;
                if (repository is not null)
                {
                    await repository.SaveAsync(subscription);
                }
            }

            await cache.SetUserSubscriptionAsync(subscription);
            return subscription;
        }

        public async Task<bool> CanPerformQueryAsync(ulong discordUserId)
        {
            var subscription = await GetOrCreateSubscriptionAsync(discordUserId);

            // Check if subscription is active (for paid tiers)
            if (subscription.Tier != SubscriptionTier.Starter && !subscription.IsActive)
            {
                logger.LogWarning("User {UserId} subscription expired", discordUserId);
                return false;
            }

            // Check daily limit
            if (subscription.QueriesUsedToday >= subscription.DailyQueryLimit)
            {
                logger.LogDebug(
                    "User {UserId} at daily limit ({Used}/{Limit})",
                    discordUserId,
                    subscription.QueriesUsedToday,
                    subscription.DailyQueryLimit);
                return false;
            }

            return true;
        }

        public async Task RecordQueryAsync(ulong discordUserId)
        {
            var subscription = await GetOrCreateSubscriptionAsync(discordUserId);
            subscription.QueriesUsedToday++;

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetService<ISubscriptionRepository>();
            if (repository is not null)
            {
                await repository.SaveAsync(subscription);
            }

            await cache.SetUserSubscriptionAsync(subscription);

            logger.LogDebug("User {UserId} query count: {Count}/{Limit}",
                discordUserId, subscription.QueriesUsedToday, subscription.DailyQueryLimit);
        }

        public async Task<bool> HasChannelAccessAsync(ulong discordUserId, AlertChannel channel)
        {
            var subscription = await GetOrCreateSubscriptionAsync(discordUserId);

            return channel switch
            {
                AlertChannel.CoreGeneral => subscription.Tier >= SubscriptionTier.Core,
                AlertChannel.SharpOnly => subscription.Tier >= SubscriptionTier.Sharp,
                AlertChannel.DirectMessage => subscription.Tier >= SubscriptionTier.Sharp,
                _ => false
            };
        }

        public async Task UpdateTierAsync(ulong discordUserId, SubscriptionTier tier)
        {
            var subscription = await GetOrCreateSubscriptionAsync(discordUserId);
            var previousTier = subscription.Tier;

            subscription.Tier = tier;
            subscription.SubscriptionStart = DateTime.UtcNow;

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetService<ISubscriptionRepository>();
            if (repository is not null)
            {
                await repository.SaveAsync(subscription);
            }

            await cache.InvalidateUserAsync(discordUserId);

            logger.LogInformation("Updated subscription for user {UserId}: {OldTier} -> {NewTier}",
                discordUserId, previousTier, tier);
        }
    }

    /// <summary>
    /// Enforces tier-based feature access.
    /// </summary>
    public static class TierEnforcer
    {
        private static readonly Dictionary<string, SubscriptionTier> FeatureMinimumTier = new()
        {
            { "movement_alerts", SubscriptionTier.Core },
            { "high_confidence_alerts", SubscriptionTier.Sharp },
            { "dm_alerts", SubscriptionTier.Sharp },
            { "sharp_channel", SubscriptionTier.Sharp },
            { "first_mover_detection", SubscriptionTier.Core },
            { "full_confidence_scores", SubscriptionTier.Core },
            { "confidence_history", SubscriptionTier.Sharp },
            { "full_ai_explanations", SubscriptionTier.Core },
            { "historical_stats", SubscriptionTier.Core }
        };

        public static int GetHistoricalDaysAllowed(SubscriptionTier tier) => tier switch
        {
            SubscriptionTier.Starter => 1,
            SubscriptionTier.Core => 7,
            SubscriptionTier.Sharp => 30,
            _ => 1
        };

        public static bool CanAccessFeature(SubscriptionTier tier, string feature)
        {
            if (!FeatureMinimumTier.TryGetValue(feature, out var minimumTier))
                return true;

            return tier >= minimumTier;
        }

        public static List<AlertChannel> GetAllowedChannels(SubscriptionTier tier)
        {
            List<AlertChannel> channels = [];

            if (tier >= SubscriptionTier.Core)
                channels.Add(AlertChannel.CoreGeneral);

            if (tier >= SubscriptionTier.Sharp)
            {
                channels.Add(AlertChannel.SharpOnly);
                channels.Add(AlertChannel.DirectMessage);
            }

            return channels;
        }
    }
}