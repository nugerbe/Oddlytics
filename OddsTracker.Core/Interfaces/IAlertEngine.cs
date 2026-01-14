using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IAlertEngine
    {
        Task<MarketAlert?> EvaluateForAlertAsync(MarketFingerprint fingerprint, ConfidenceScore confidence);
        Task<bool> ShouldSendAlertAsync(MarketAlert alert);
        Task MarkAlertSentAsync(MarketAlert alert);
    }
}
