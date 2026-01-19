using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IHistoricalRepository
    {
        Task SaveSignalAsync(SignalSnapshot snapshot);
        Task UpdateSignalAsync(SignalSnapshot snapshot);
        Task<List<SignalSnapshot>> GetSignalsForEventAsync(string eventId, string marketKey);
        Task<List<SignalSnapshot>> GetSignalsInRangeAsync(DateTime from, DateTime to);
    }
}
