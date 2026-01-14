using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Discord alert service that uses webhooks instead of the bot client.
    /// Used by Azure Functions since they can't maintain a persistent bot connection.
    /// </summary>
    public interface IDiscordAlertService
    {
        Task SendAlertAsync(MarketAlert alert);
    }
}
