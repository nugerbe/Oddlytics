using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Tracks historical signal performance for aggregated stats.
    /// Uses IServiceScopeFactory to access scoped DbContext from singleton service.
    /// </summary>
    public class HistoricalTracker(
        IServiceScopeFactory scopeFactory,
        ILogger<HistoricalTracker> logger,
        IOptions<HistoricalTrackerOptions>? options = null) : IHistoricalTracker
    {
        private readonly HistoricalTrackerOptions _options = options?.Value ?? new HistoricalTrackerOptions();

        public async Task RecordSignalAsync(MarketFingerprint fingerprint, ConfidenceScore confidence)
        {
            var snapshot = new SignalSnapshot
            {
                EventId = fingerprint.Market.EventId,
                MarketType = fingerprint.Market.MarketType,
                SignalTime = DateTime.UtcNow,
                GameTime = fingerprint.Market.CommenceTime,
                LineAtSignal = fingerprint.ConsensusLine,
                ConfidenceAtSignal = confidence.Level,
                ConfidenceScoreAtSignal = confidence.Score,
                FirstMoverBook = fingerprint.FirstMoverBook ?? string.Empty,
                FirstMoverType = fingerprint.FirstMoverType
            };

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IHistoricalRepository>();
            await repository.SaveSignalAsync(snapshot);

            logger.LogInformation(
                "Recorded signal: {EventId} {MarketType} Line={Line} Confidence={Confidence}",
                snapshot.EventId,
                snapshot.MarketType,
                snapshot.LineAtSignal,
                snapshot.ConfidenceAtSignal);
        }

        public async Task UpdateOutcomeAsync(string eventId, MarketType marketType, decimal closingLine, SignalOutcome outcome)
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IHistoricalRepository>();

            var signals = await repository.GetSignalsForEventAsync(eventId, marketType);

            foreach (var signal in signals)
            {
                signal.ClosingLine = closingLine;
                signal.Outcome = outcome;

                await repository.UpdateSignalAsync(signal);

                logger.LogInformation(
                    "Updated outcome: {EventId} {MarketType} {Outcome}",
                    eventId,
                    marketType,
                    signal.Outcome);
            }
        }

        public async Task<PerformanceStats> GetPerformanceStatsAsync(DateTime from, DateTime to, SubscriptionTier tier)
        {
            var maxDaysBack = tier switch
            {
                SubscriptionTier.Starter => _options.StarterHistoricalDays,
                SubscriptionTier.Core => _options.CoreHistoricalDays,
                SubscriptionTier.Sharp => _options.SharpHistoricalDays,
                _ => _options.StarterHistoricalDays
            };

            var earliestAllowed = DateTime.UtcNow.AddDays(-maxDaysBack);
            var effectiveFrom = from < earliestAllowed ? earliestAllowed : from;

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IHistoricalRepository>();

            var signals = await repository.GetSignalsInRangeAsync(effectiveFrom, to);
            var completedSignals = signals.Where(s => s.Outcome.HasValue).ToList();

            return new PerformanceStats
            {
                PeriodStart = effectiveFrom,
                PeriodEnd = to,
                TotalSignals = completedSignals.Count,
                ExtendedCount = completedSignals.Count(s => s.Outcome == SignalOutcome.Extended),
                RevertedCount = completedSignals.Count(s => s.Outcome == SignalOutcome.Reverted),
                StableCount = completedSignals.Count(s => s.Outcome == SignalOutcome.Stable),
                ByConfidence = BuildConfidenceStats(completedSignals),
                ByFirstMover = BuildFirstMoverStats(completedSignals)
            };
        }

        private static Dictionary<ConfidenceLevel, BucketStats> BuildConfidenceStats(List<SignalSnapshot> signals)
        {
            var stats = new Dictionary<ConfidenceLevel, BucketStats>();

            foreach (var level in Enum.GetValues<ConfidenceLevel>())
            {
                var bucket = signals.Where(s => s.ConfidenceAtSignal == level).ToList();
                stats[level] = new BucketStats
                {
                    Total = bucket.Count,
                    Extended = bucket.Count(s => s.Outcome == SignalOutcome.Extended),
                    Reverted = bucket.Count(s => s.Outcome == SignalOutcome.Reverted)
                };
            }

            return stats;
        }

        private static Dictionary<BookmakerTier, BucketStats> BuildFirstMoverStats(List<SignalSnapshot> signals)
        {
            var stats = new Dictionary<BookmakerTier, BucketStats>();

            foreach (var bookType in Enum.GetValues<BookmakerTier>())
            {
                var bucket = signals.Where(s => s.FirstMoverType == bookType).ToList();
                stats[bookType] = new BucketStats
                {
                    Total = bucket.Count,
                    Extended = bucket.Count(s => s.Outcome == SignalOutcome.Extended),
                    Reverted = bucket.Count(s => s.Outcome == SignalOutcome.Reverted)
                };
            }

            return stats;
        }
    }
}