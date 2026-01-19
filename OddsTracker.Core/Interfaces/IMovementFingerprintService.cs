using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IMovementFingerprintService
    {
        /// <summary>
        /// Create a fingerprint from odds data with optional previous fingerprint for comparison
        /// </summary>
        Task<MarketFingerprint> CreateFingerprintAsync(OddsBase odds, MarketFingerprint? previous);

        /// <summary>
        /// Create a fingerprint from raw snapshots with market definition
        /// </summary>
        Task<MarketFingerprint> CreateFingerprintAsync(
            string eventId,
            MarketDefinition market,
            List<BookSnapshotBase> snapshots,
            string homeTeam = "",
            string awayTeam = "",
            DateTime? commenceTime = null);

        /// <summary>
        /// Get the previously cached fingerprint for a market
        /// </summary>
        Task<MarketFingerprint?> GetPreviousFingerprintAsync(string eventId, string marketKey);

        /// <summary>
        /// Save fingerprint to cache
        /// </summary>
        Task SaveFingerprintAsync(MarketFingerprint fingerprint);

        /// <summary>
        /// Check if there's a material change between fingerprints
        /// </summary>
        bool HasMaterialChange(MarketFingerprint current, MarketFingerprint? previous);
    }
}
