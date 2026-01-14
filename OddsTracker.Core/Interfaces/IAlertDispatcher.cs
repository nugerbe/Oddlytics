using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Interface for dispatching alerts to Discord
    /// </summary>
    public interface IAlertDispatcher
    {
        Task DispatchAsync(MarketAlert alert);
    }
}
