using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IHistoricalTracker
    {
        Task RecordSignalAsync(MarketFingerprint fingerprint, ConfidenceScore confidence);
        Task UpdateOutcomeAsync(string eventId, string marketKey, decimal closingLine, SignalOutcome outcome);
        Task<PerformanceStats> GetPerformanceStatsAsync(DateTime from, DateTime to, SubscriptionTier tier);
    }
}
