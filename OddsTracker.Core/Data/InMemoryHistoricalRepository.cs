using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Data
{
    /// <summary>
    /// In-memory implementation of IHistoricalRepository for development/testing.
    /// </summary>
    public class InMemoryHistoricalRepository : IHistoricalRepository
    {
        private readonly List<SignalSnapshot> _signals = [];
        private long _nextId = 1;
        private readonly object _lock = new();

        public Task SaveSignalAsync(SignalSnapshot signal)
        {
            lock (_lock)
            {
                signal.Id = _nextId++;
                _signals.Add(signal);
            }
            return Task.CompletedTask;
        }

        public Task UpdateSignalAsync(SignalSnapshot signal)
        {
            lock (_lock)
            {
                var index = _signals.FindIndex(s => s.Id == signal.Id);
                if (index >= 0)
                {
                    _signals[index] = signal;
                }
            }
            return Task.CompletedTask;
        }

        public Task<List<SignalSnapshot>> GetSignalsForEventAsync(string eventId, MarketType marketType)
        {
            lock (_lock)
            {
                var result = _signals
                    .Where(s => s.EventId == eventId && s.MarketType == marketType)
                    .ToList();
                return Task.FromResult(result);
            }
        }

        public Task<List<SignalSnapshot>> GetSignalsInRangeAsync(DateTime from, DateTime to)
        {
            lock (_lock)
            {
                var result = _signals
                    .Where(s => s.SignalTime >= from && s.SignalTime <= to)
                    .ToList();
                return Task.FromResult(result);
            }
        }

        public Task<List<SignalSnapshot>> GetPendingOutcomeSignalsAsync(DateTime gameTimeBefore)
        {
            lock (_lock)
            {
                var result = _signals
                    .Where(s => s.Outcome is null && s.GameTime < gameTimeBefore)
                    .ToList();
                return Task.FromResult(result);
            }
        }
    }
}
