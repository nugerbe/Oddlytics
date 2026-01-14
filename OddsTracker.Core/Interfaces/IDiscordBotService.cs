using Discord;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IDiscordBotService
    {
        Task SendAlertAsync(MarketAlert alert);
        Task SendDMAsync(ulong userId, string message, Embed? embed = null);
        Task AssignRoleAsync(ulong userId, SubscriptionTier tier);
    }
}
