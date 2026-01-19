using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OddsTracker.Core.Services
{
    public class MovementFingerprintService(
        IEnhancedCacheService cache,
        IMarketRepository marketRepository,
        ILogger<MovementFingerprintService> logger) : IMovementFingerprintService
    {
        public async Task<MarketFingerprint> CreateFingerprintAsync(OddsBase odds, MarketFingerprint? previous)
        {
            var market = odds.MarketDefinition;

            // Classify each book
            await ClassifyBooksAsync(odds.Snapshots);

            // Build market key based on odds type
            var marketKey = odds switch
            {
                GameOdds game => new MarketKey(
                    odds.EventId,
                    game.HomeTeam,
                    game.AwayTeam,
                    market,
                    odds.CommenceTime),
                PlayerOdds player => new MarketKey(
                    odds.EventId,
                    player.Team,
                    player.Opponent ?? string.Empty,
                    market,
                    odds.CommenceTime),
                _ => throw new ArgumentException($"Unknown odds type: {odds.GetType().Name}")
            };

            // Calculate consensus line (median across all books)
            var lines = odds.Snapshots.Select(s => s.Line).OrderBy(l => l).ToList();
            var consensusLine = lines.Count > 0
                ? lines[lines.Count / 2]
                : 0m;

            var fingerprint = new MarketFingerprint
            {
                Market = marketKey,
                Timestamp = DateTime.UtcNow,
                ConsensusLine = consensusLine,
                PreviousConsensusLine = previous?.ConsensusLine ?? consensusLine,
                BookSnapshots = odds.Snapshots
            };

            // Detect first mover
            DetectFirstMover(fingerprint, previous);

            // Calculate velocity
            CalculateVelocity(fingerprint, previous);

            // Calculate retail lag
            CalculateRetailLag(fingerprint);

            // Track stability
            TrackStability(fingerprint, previous);

            // Generate content hash for change detection
            fingerprint.ContentHash = GenerateContentHash(fingerprint);

            return fingerprint;
        }

        public async Task<MarketFingerprint> CreateFingerprintAsync(
            string eventId,
            MarketDefinition market,
            List<BookSnapshotBase> snapshots,
            string homeTeam = "",
            string awayTeam = "",
            DateTime? commenceTime = null)
        {
            // Classify each book
            await ClassifyBooksAsync(snapshots);

            // Calculate consensus line
            var lines = snapshots.Select(s => s.Line).OrderBy(l => l).ToList();
            var consensusLine = lines.Count > 0 ? lines[lines.Count / 2] : 0m;

            var fingerprint = new MarketFingerprint
            {
                Market = new MarketKey(
                    eventId,
                    homeTeam,
                    awayTeam,
                    market,
                    commenceTime ?? DateTime.UtcNow),
                Timestamp = DateTime.UtcNow,
                ConsensusLine = consensusLine,
                PreviousConsensusLine = consensusLine,
                BookSnapshots = snapshots
            };

            fingerprint.ContentHash = GenerateContentHash(fingerprint);

            return fingerprint;
        }

        public async Task<MarketFingerprint?> GetPreviousFingerprintAsync(string eventId, string marketKey)
        {
            return await cache.GetFingerprintAsync(eventId, marketKey);
        }

        public async Task SaveFingerprintAsync(MarketFingerprint fingerprint)
        {
            await cache.SetFingerprintAsync(fingerprint);

            logger.LogDebug(
                "Saved fingerprint: {EventId}:{MarketKey} Consensus={Consensus}",
                fingerprint.Market.EventId,
                fingerprint.Market.MarketType.Key,
                fingerprint.ConsensusLine);
        }

        public bool HasMaterialChange(MarketFingerprint current, MarketFingerprint? previous)
        {
            return current.HasMaterialChange(previous);
        }

        /// <summary>
        /// Classify books using database bookmaker info when available
        /// </summary>
        private async Task ClassifyBooksAsync(List<BookSnapshotBase> snapshots)
        {
            // Try to get bookmaker info from database
            var bookmakers = await marketRepository.GetAllBookmakersAsync();
            var bookmakersByKey = bookmakers.ToDictionary(
                b => b.Key,
                b => b.Tier,
                StringComparer.OrdinalIgnoreCase);

            foreach (var snapshot in snapshots)
            {
                var key = snapshot.BookmakerKey ?? snapshot.BookmakerName;
                if (bookmakersByKey.TryGetValue(key, out var tier))
                {
                    snapshot.BookType = tier;
                }
            }
        }

        private static void DetectFirstMover(MarketFingerprint fingerprint, MarketFingerprint? previous)
        {
            if (previous is null || fingerprint.DeltaMagnitude < 0.5m)
                return;

            // Find the book that moved first (earliest timestamp with different line)
            var movers = fingerprint.BookSnapshots
                .Where(s => Math.Abs(s.Line - previous.ConsensusLine) >= 0.5m)
                .OrderBy(s => s.Timestamp)
                .ToList();

            if (movers.Count > 0)
            {
                var firstMover = movers[0];
                fingerprint.FirstMoverBook = firstMover.BookmakerName;
                fingerprint.FirstMoverType = firstMover.BookType;
                fingerprint.FirstMoveTime = firstMover.Timestamp;
            }
        }

        private static void CalculateVelocity(MarketFingerprint fingerprint, MarketFingerprint? previous)
        {
            if (previous is null)
            {
                fingerprint.Velocity = 0;
                return;
            }

            var timeDiff = fingerprint.Timestamp - previous.Timestamp;
            if (timeDiff.TotalHours < 0.01) // Avoid division by near-zero
            {
                fingerprint.Velocity = 0;
                return;
            }

            fingerprint.Velocity = fingerprint.DeltaMagnitude / (decimal)timeDiff.TotalHours;
        }

        private static void CalculateRetailLag(MarketFingerprint fingerprint)
        {
            if (fingerprint.FirstMoveTime is null || fingerprint.FirstMoverType == BookmakerTier.Retail)
                return;

            // Find when retail books caught up
            var retailMovers = fingerprint.BookSnapshots
                .Where(s => s.BookType == BookmakerTier.Retail &&
                           Math.Abs(s.Line - fingerprint.ConsensusLine) < 0.5m)
                .OrderBy(s => s.Timestamp)
                .FirstOrDefault();

            if (retailMovers is not null)
            {
                fingerprint.RetailLag = retailMovers.Timestamp - fingerprint.FirstMoveTime.Value;
            }
        }

        private static void TrackStability(MarketFingerprint fingerprint, MarketFingerprint? previous)
        {
            if (previous is null)
                return;

            // Check for reversal (line moved back toward previous)
            var currentDirection = Math.Sign(fingerprint.ConsensusLine - fingerprint.PreviousConsensusLine);
            var previousDirection = Math.Sign(previous.ConsensusLine - previous.PreviousConsensusLine);

            if (currentDirection != 0 && previousDirection != 0 && currentDirection != previousDirection)
            {
                fingerprint.LastReversalTime = DateTime.UtcNow;
            }
            else
            {
                fingerprint.LastReversalTime = previous.LastReversalTime;
            }
        }

        private static string GenerateContentHash(MarketFingerprint fingerprint)
        {
            var content = new
            {
                fingerprint.Market.Key,
                fingerprint.ConsensusLine,
                Books = fingerprint.BookSnapshots
                    .Select(s => new { s.BookmakerKey, s.Line })
                    .OrderBy(s => s.BookmakerKey)
            };

            var json = JsonSerializer.Serialize(content);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(bytes)[..16];
        }
    }
}