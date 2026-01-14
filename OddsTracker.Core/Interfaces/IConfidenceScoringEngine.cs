using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IConfidenceScoringEngine
    {
        ConfidenceScore CalculateScore(MarketFingerprint fingerprint);
    }
}
