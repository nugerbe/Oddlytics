using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Manages Discord role assignments based on subscription tier
    /// </summary>
    public interface IDiscordRoleManager
    {
        Task SyncRolesAsync(ulong discordUserId, SubscriptionTier tier);
    }
}
