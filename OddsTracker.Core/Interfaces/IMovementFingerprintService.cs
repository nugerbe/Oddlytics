using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IMovementFingerprintService
    {
        Task<MarketFingerprint> CreateFingerprintAsync(NormalizedOdds odds, MarketFingerprint? previous);
        Task<MarketFingerprint> CreateFingerprintAsync(string eventId, MarketType marketType, List<BookSnapshot> snapshots);
        Task<MarketFingerprint?> GetPreviousFingerprintAsync(string marketKey);
        Task SaveFingerprintAsync(MarketFingerprint fingerprint);
        bool HasMaterialChange(MarketFingerprint current, MarketFingerprint? previous);
    }
}
