using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OddsTracker.Core.Services
{
    public class MovementFingerprintService(ICacheService cache, ILogger<MovementFingerprintService> logger) : IMovementFingerprintService
    {
        private readonly ICacheService _cache = cache;
        private readonly ILogger<MovementFingerprintService> _logger = logger;

        // Book classification
        private static readonly Dictionary<string, BookmakerTier> BookClassification = new(StringComparer.OrdinalIgnoreCase)
        {
            // Sharp books - fast, accurate, respected
            ["lowvig"] = BookmakerTier.Sharp,
            ["betonlineag"] = BookmakerTier.Sharp,

            // Market makers - high volume, influential
            ["betmgm"] = BookmakerTier.Market,
            ["williamhill_us"] = BookmakerTier.Market,
            ["bet365"] = BookmakerTier.Market,

            // Retail - slower to move, follow sharps
            ["fanatics"] = BookmakerTier.Retail,
            ["betus"] = BookmakerTier.Retail,
            ["mybookieag"] = BookmakerTier.Retail,
            ["bovada"] = BookmakerTier.Retail,
            ["draftkings"] = BookmakerTier.Retail,
            ["fanduel"] = BookmakerTier.Retail,
        };

        public async Task<MarketFingerprint> CreateFingerprintAsync(NormalizedOdds odds, MarketFingerprint? previous)
        {
            // Classify each book
            foreach (var snapshot in odds.Snapshots)
            {
                snapshot.BookType = ClassifyBook(snapshot.BookmakerKey ?? snapshot.BookmakerName);
            }

            // Calculate consensus line (median across all books)
            var lines = odds.Snapshots.Select(s => s.Line).OrderBy(l => l).ToList();
            var consensusLine = lines.Count > 0
                ? lines[lines.Count / 2]
                : 0m;

            var fingerprint = new MarketFingerprint
            {
                Market = new MarketKey(
                    odds.EventId,
                    odds.HomeTeam,
                    odds.AwayTeam,
                    odds.MarketType,
                    odds.CommenceTime),
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
            MarketType marketType,
            List<BookSnapshot> snapshots)
        {
            var marketKey = $"{eventId}:{marketType}";
            var previous = await GetPreviousFingerprintAsync(marketKey);

            // Classify each book
            foreach (var snapshot in snapshots)
            {
                snapshot.BookType = ClassifyBook(snapshot.BookmakerKey ?? snapshot.BookmakerName);
            }

            // Calculate consensus line (median across all books)
            var lines = snapshots.Select(s => s.Line).OrderBy(l => l).ToList();
            var consensusLine = lines.Count > 0
                ? lines[lines.Count / 2]
                : 0m;

            var fingerprint = new MarketFingerprint
            {
                Market = new MarketKey(eventId, "", "", marketType, DateTime.UtcNow),
                Timestamp = DateTime.UtcNow,
                ConsensusLine = consensusLine,
                PreviousConsensusLine = previous?.ConsensusLine ?? consensusLine,
                BookSnapshots = snapshots
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

        private static BookmakerTier ClassifyBook(string bookmakerKey)
        {
            return BookClassification.GetValueOrDefault(
                bookmakerKey.ToLowerInvariant(),
                BookmakerTier.Retail);
        }

        private void DetectFirstMover(MarketFingerprint current, MarketFingerprint? previous)
        {
            if (previous is null || current.DeltaMagnitude < 0.5m)
                return;

            // Find book that moved first (earliest timestamp with different line)
            var movedBooks = current.BookSnapshots
                .Where(b =>
                {
                    var prevBook = previous.BookSnapshots
                        .FirstOrDefault(pb => pb.BookmakerName == b.BookmakerName);
                    return prevBook is not null && Math.Abs(b.Line - prevBook.Line) >= 0.5m;
                })
                .OrderBy(b => b.Timestamp)
                .ToList();

            if (movedBooks.Count > 0)
            {
                var firstMover = movedBooks[0];
                current.FirstMoverBook = firstMover.BookmakerName;
                current.FirstMoverType = firstMover.BookType;
                current.FirstMoveTime = firstMover.Timestamp;

                _logger.LogDebug(
                    "First mover detected: {Book} ({Type}) at {Time}",
                    firstMover.BookmakerName,
                    firstMover.BookType,
                    firstMover.Timestamp);
            }
        }

        private static void CalculateVelocity(MarketFingerprint current, MarketFingerprint? previous)
        {
            if (previous is null)
            {
                current.Velocity = 0;
                return;
            }

            var timeDiff = (current.Timestamp - previous.Timestamp).TotalHours;
            if (timeDiff <= 0)
            {
                current.Velocity = 0;
                return;
            }

            // Points per hour
            current.Velocity = current.DeltaMagnitude / (decimal)timeDiff;
        }

        private static void CalculateRetailLag(MarketFingerprint fingerprint)
        {
            if (fingerprint.FirstMoverType != BookmakerTier.Sharp || !fingerprint.FirstMoveTime.HasValue)
                return;

            // Find when first retail book matched the move
            var retailFollow = fingerprint.BookSnapshots
                .Where(b => b.BookType == BookmakerTier.Retail)
                .Where(b => Math.Abs(b.Line - fingerprint.ConsensusLine) < 0.5m)
                .OrderBy(b => b.Timestamp)
                .FirstOrDefault();

            if (retailFollow is not null)
            {
                fingerprint.RetailLag = retailFollow.Timestamp - fingerprint.FirstMoveTime.Value;
            }
        }

        private static void TrackStability(MarketFingerprint current, MarketFingerprint? previous)
        {
            if (previous is null)
                return;

            // Check if line reversed (moved back toward previous)
            var prevDelta = previous.ConsensusLine - previous.PreviousConsensusLine;
            var currDelta = current.ConsensusLine - previous.ConsensusLine;

            // Signs differ = reversal
            if (prevDelta != 0 && currDelta != 0 &&
                Math.Sign(prevDelta) != Math.Sign(currDelta))
            {
                current.LastReversalTime = DateTime.UtcNow;
            }
            else
            {
                current.LastReversalTime = previous.LastReversalTime;
            }
        }

        private static string GenerateContentHash(MarketFingerprint fingerprint)
        {
            var content = JsonSerializer.Serialize(new
            {
                fingerprint.ConsensusLine,
                fingerprint.FirstMoverBook,
                fingerprint.ConfirmingBooks,
                Books = fingerprint.BookSnapshots.Select(b => new { b.BookmakerName, b.Line })
            });

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToBase64String(hashBytes)[..16];
        }

        public async Task<MarketFingerprint?> GetPreviousFingerprintAsync(string marketKey)
        {
            return await _cache.GetAsync<MarketFingerprint>($"fingerprint:{marketKey}");
        }

        public async Task SaveFingerprintAsync(MarketFingerprint fingerprint)
        {
            var key = $"fingerprint:{fingerprint.Market.Key}";
            await _cache.SetAsync(key, fingerprint, TimeSpan.FromHours(24));
        }

        public bool HasMaterialChange(MarketFingerprint current, MarketFingerprint? previous)
        {
            return current.HasMaterialChange(previous);
        }
    }
}