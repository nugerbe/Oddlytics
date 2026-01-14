using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Deterministic confidence scoring engine producing reproducible 0-100 scores.
    /// 
    /// <para><b>Components (each 0-25 points):</b></para>
    /// <list type="bullet">
    ///   <item><b>First Mover:</b> Sharp=25, Market=15, Retail=5</item>
    ///   <item><b>Velocity:</b> Movement speed in points/hour</item>
    ///   <item><b>Confirmation:</b> Number of books at consensus line</item>
    ///   <item><b>Stability:</b> Time since last reversal</item>
    /// </list>
    /// 
    /// <para><b>Score Buckets:</b> Low (0-49), Medium (50-79), High (80-100)</para>
    /// </summary>
    public class ConfidenceScoringEngine(
        ILogger<ConfidenceScoringEngine> logger,
        IOptions<ConfidenceScoringOptions>? options = null) : IConfidenceScoringEngine
    {
        private readonly ConfidenceScoringOptions _options = options?.Value ?? new ConfidenceScoringOptions();

        public ConfidenceScore CalculateScore(MarketFingerprint fingerprint)
        {
            var firstMoverScore = CalculateFirstMoverScore(fingerprint);
            var velocityScore = CalculateVelocityScore(fingerprint);
            var confirmationScore = CalculateConfirmationScore(fingerprint);
            var stabilityScore = CalculateStabilityScore(fingerprint);

            var totalScore = firstMoverScore + velocityScore + confirmationScore + stabilityScore;

            var score = new ConfidenceScore
            {
                MarketKey = fingerprint.Market.Key,
                Timestamp = DateTime.UtcNow,
                Score = totalScore,
                FirstMoverScore = firstMoverScore,
                VelocityScore = velocityScore,
                ConfirmationScore = confirmationScore,
                StabilityScore = stabilityScore,
                Explanation = GenerateExplanation(fingerprint)
            };

            logger.LogInformation(
                "Confidence score for {Market}: {Score} ({Level}) [FM:{FM} V:{V} C:{C} S:{S}]",
                fingerprint.Market.Key,
                score.Score,
                score.Level,
                firstMoverScore,
                velocityScore,
                confirmationScore,
                stabilityScore);

            return score;
        }

        private int CalculateFirstMoverScore(MarketFingerprint fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint.FirstMoverBook))
                return 0;

            return fingerprint.FirstMoverType switch
            {
                BookmakerTier.Sharp => _options.SharpMoverScore,
                BookmakerTier.Market => _options.MarketMoverScore,
                BookmakerTier.Retail => _options.RetailMoverScore,
                _ => 0
            };
        }

        private int CalculateVelocityScore(MarketFingerprint fingerprint)
        {
            if (fingerprint.Velocity <= 0)
                return 0;

            if (fingerprint.Velocity >= _options.HighVelocityThreshold)
                return _options.MaxComponentScore;

            if (fingerprint.Velocity >= _options.MediumVelocityThreshold)
            {
                var ratio = (fingerprint.Velocity - _options.MediumVelocityThreshold)
                          / (_options.HighVelocityThreshold - _options.MediumVelocityThreshold);
                return (int)(12 + (ratio * 13));
            }

            var lowRatio = fingerprint.Velocity / _options.MediumVelocityThreshold;
            return (int)(lowRatio * 12);
        }

        private int CalculateConfirmationScore(MarketFingerprint fingerprint)
        {
            var confirming = fingerprint.ConfirmingBooks;

            if (confirming >= _options.HighConfirmationThreshold)
                return _options.MaxComponentScore;

            if (confirming >= _options.MediumConfirmationThreshold)
            {
                var ratio = (decimal)(confirming - _options.MediumConfirmationThreshold)
                          / (_options.HighConfirmationThreshold - _options.MediumConfirmationThreshold);
                return (int)(12 + (ratio * 13));
            }

            if (confirming > 0)
            {
                var ratio = (decimal)confirming / _options.MediumConfirmationThreshold;
                return (int)(ratio * 12);
            }

            return 0;
        }

        private int CalculateStabilityScore(MarketFingerprint fingerprint)
        {
            var stabilityMinutes = fingerprint.StabilityWindow.TotalMinutes;

            if (stabilityMinutes >= _options.HighStabilityMinutes)
                return _options.MaxComponentScore;

            if (stabilityMinutes >= _options.MediumStabilityMinutes)
            {
                var ratio = (stabilityMinutes - _options.MediumStabilityMinutes)
                          / (_options.HighStabilityMinutes - _options.MediumStabilityMinutes);
                return (int)(12 + (ratio * 13));
            }

            var lowRatio = stabilityMinutes / _options.MediumStabilityMinutes;
            return (int)(lowRatio * 12);
        }

        private string GenerateExplanation(MarketFingerprint fingerprint)
        {
            List<string> parts = [];

            // First mover explanation
            if (!string.IsNullOrEmpty(fingerprint.FirstMoverBook))
            {
                var moverDesc = fingerprint.FirstMoverType switch
                {
                    BookmakerTier.Sharp => $"Sharp book ({fingerprint.FirstMoverBook}) moved first",
                    BookmakerTier.Market => $"Market maker ({fingerprint.FirstMoverBook}) initiated move",
                    _ => $"{fingerprint.FirstMoverBook} moved first"
                };
                parts.Add(moverDesc);
            }

            // Velocity explanation
            if (fingerprint.Velocity >= _options.HighVelocityThreshold)
                parts.Add($"Fast movement ({fingerprint.Velocity:F1} pts/hr)");
            else if (fingerprint.Velocity >= _options.MediumVelocityThreshold)
                parts.Add($"Moderate movement ({fingerprint.Velocity:F1} pts/hr)");

            // Confirmation explanation
            if (fingerprint.ConfirmingBooks >= _options.HighConfirmationThreshold)
                parts.Add($"Strong consensus ({fingerprint.ConfirmingBooks} books aligned)");
            else if (fingerprint.ConfirmingBooks >= _options.MediumConfirmationThreshold)
                parts.Add($"Building consensus ({fingerprint.ConfirmingBooks} books)");

            // Stability explanation
            if (fingerprint.StabilityWindow.TotalMinutes >= _options.HighStabilityMinutes)
                parts.Add($"Stable for {fingerprint.StabilityWindow.TotalMinutes:F0}+ minutes");
            else if (fingerprint.StabilityWindow.TotalMinutes >= _options.MediumStabilityMinutes)
                parts.Add($"Holding steady ({fingerprint.StabilityWindow.TotalMinutes:F0} min)");

            // Retail lag bonus info
            if (fingerprint.RetailLag.HasValue && fingerprint.RetailLag.Value.TotalMinutes > 5)
                parts.Add($"Retail lag: {fingerprint.RetailLag.Value.TotalMinutes:F0} min");

            // Movement direction
            if (fingerprint.DeltaMagnitude > 0)
            {
                var direction = fingerprint.ConsensusLine > fingerprint.PreviousConsensusLine ? "up" : "down";
                parts.Add($"Line moved {direction} {fingerprint.DeltaMagnitude:F1} pts");
            }

            return parts.Count > 0
                ? string.Join(". ", parts) + "."
                : "Insufficient signal data.";
        }
    }
}