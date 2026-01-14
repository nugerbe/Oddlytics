using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IHistoricalRepository
    {
        Task SaveSignalAsync(SignalSnapshot signal);
        Task UpdateSignalAsync(SignalSnapshot signal);
        Task<List<SignalSnapshot>> GetSignalsForEventAsync(string eventId, MarketType marketType);
        Task<List<SignalSnapshot>> GetSignalsInRangeAsync(DateTime from, DateTime to);
        Task<List<SignalSnapshot>> GetPendingOutcomeSignalsAsync(DateTime gameTimeBefore);
    }
}
