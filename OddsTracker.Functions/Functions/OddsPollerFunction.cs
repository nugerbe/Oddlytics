using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;

namespace OddsTracker.Functions.Functions
{
    /// <summary>
    /// Timer-triggered function that polls The Odds API for line movements.
    /// Runs every 60 seconds during active hours, creates market fingerprints,
    /// calculates confidence scores, and sends alerts for significant movements.
    /// </summary>
    public class OddsPollerFunction(
        IOddsApiClient oddsClient,
        IMovementFingerprintService fingerprintService,
        IConfidenceScoringEngine confidenceEngine,
        IAlertEngine alertEngine,
        IHistoricalTracker historicalTracker,
        IEnhancedCacheService cache,
        IDiscordAlertService discordService,
        ILogger<OddsPollerFunction> logger)
    {
        private static readonly string[] Sports = ["americanfootball_nfl"];
        private static readonly string[] Markets = ["h2h", "spreads", "totals"];
        private static readonly TimeSpan ClosingLineCacheWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ClosingLineCacheDuration = TimeSpan.FromHours(8);

        /// <summary>
        /// Timer trigger: runs every 60 seconds
        /// CRON: "0 * * * * *" = every minute at second 0
        /// </summary>
        [Function("OddsPoller")]
        public async Task Run([TimerTrigger("0 * * * * *")] MyTimerInfo timerInfo)
        {
            logger.LogInformation("OddsPoller function triggered at {Time}", DateTime.UtcNow);

            if (timerInfo.IsPastDue)
            {
                logger.LogWarning("Timer is running late");
            }

            var totalEvents = 0;
            var alertsSent = 0;

            foreach (var sport in Sports)
            {
                try
                {
                    var events = await oddsClient.GetOddsAsync(sport, Markets);

                    if (events is null || events.Count == 0)
                    {
                        logger.LogDebug("No events returned for {Sport}", sport);
                        continue;
                    }

                    totalEvents += events.Count;

                    foreach (var evt in events)
                    {
                        foreach (var marketKey in Markets)
                        {
                            try
                            {
                                var sent = await ProcessMarketAsync(evt, marketKey);
                                if (sent) alertsSent++;
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Error processing {Event} {Market}", evt.Id, marketKey);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error polling {Sport}", sport);
                }
            }

            logger.LogInformation(
                "OddsPoller completed: {Events} events processed, {Alerts} alerts sent",
                totalEvents, alertsSent);
        }

        private async Task<bool> ProcessMarketAsync(OddsEvent evt, string marketKey)
        {
            var marketType = MarketTypeExtensions.FromApiKey(marketKey);
            if (marketType is null) return false;

            // Extract book snapshots from event
            var bookSnapshots = ExtractBookSnapshots(evt, marketKey);
            if (bookSnapshots.Count == 0) return false;

            // Create market key string for cache lookup
            var marketKeyStr = $"{evt.Id}:{marketType}";

            // Get previous fingerprint from cache
            var previousFingerprint = await fingerprintService.GetPreviousFingerprintAsync(marketKeyStr);

            // Create new fingerprint
            var fingerprint = await fingerprintService.CreateFingerprintAsync(evt.Id, marketType.Value, bookSnapshots);

            // Save fingerprint to cache
            await fingerprintService.SaveFingerprintAsync(fingerprint);

            // Store closing line if game is about to start
            await TryStoreClosingLineAsync(evt, fingerprint);

            // Check for material change
            if (!fingerprintService.HasMaterialChange(fingerprint, previousFingerprint))
            {
                logger.LogDebug("No material change for {Market}", marketKeyStr);
                return false;
            }

            // Calculate confidence score
            var confidence = confidenceEngine.CalculateScore(fingerprint);

            // Record signal for historical tracking
            await historicalTracker.RecordSignalAsync(fingerprint, confidence);

            // Evaluate for alert
            var alert = await alertEngine.EvaluateForAlertAsync(fingerprint, confidence);
            if (alert is null) return false;

            // Check if we should send (deduplication, cooldown, etc.)
            if (!await alertEngine.ShouldSendAlertAsync(alert)) return false;

            // Send alert via Discord
            await discordService.SendAlertAsync(alert);
            await alertEngine.MarkAlertSentAsync(alert);

            logger.LogInformation(
                "Alert sent for {Market}: {Type} (Confidence: {Score})",
                marketKeyStr, alert.Type, confidence.Score);

            return true;
        }

        private static List<BookSnapshot> ExtractBookSnapshots(OddsEvent evt, string marketKey)
        {
            var snapshots = new List<BookSnapshot>();

            foreach (var bookmaker in evt.Bookmakers ?? [])
            {
                var market = bookmaker.Markets?.FirstOrDefault(m => m.Key == marketKey);
                if (market is null) continue;

                var snapshot = CreateSnapshot(evt, bookmaker, market, marketKey);
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        private static BookSnapshot? CreateSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            Market market,
            string marketKey)
        {
            return marketKey switch
            {
                "spreads" => CreateSpreadSnapshot(evt, bookmaker, market),
                "totals" => CreateTotalSnapshot(bookmaker, market),
                "h2h" => CreateMoneylineSnapshot(evt, bookmaker, market),
                _ => null
            };
        }

        private static BookSnapshot? CreateSpreadSnapshot(OddsEvent evt, Bookmaker bookmaker, Market market)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            if (homeOutcome is null) return null;

            return new BookSnapshot
            {
                BookmakerName = bookmaker.Title,
                BookmakerKey = bookmaker.Key,
                Line = homeOutcome.Point ?? 0,
                HomeOdds = homeOutcome.Price,
                AwayOdds = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam)?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        private static BookSnapshot? CreateTotalSnapshot(Bookmaker bookmaker, Market market)
        {
            var overOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == "Over");
            if (overOutcome is null) return null;

            return new BookSnapshot
            {
                BookmakerName = bookmaker.Title,
                BookmakerKey = bookmaker.Key,
                Line = overOutcome.Point ?? 0,
                HomeOdds = overOutcome.Price,
                AwayOdds = market.Outcomes?.FirstOrDefault(o => o.Name == "Under")?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        private static BookSnapshot? CreateMoneylineSnapshot(OddsEvent evt, Bookmaker bookmaker, Market market)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam);
            if (homeOutcome is null) return null;

            return new BookSnapshot
            {
                BookmakerName = bookmaker.Title,
                BookmakerKey = bookmaker.Key,
                Line = homeOutcome.Price,  // For ML, "line" is the odds
                HomeOdds = homeOutcome.Price,
                AwayOdds = awayOutcome?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        private async Task TryStoreClosingLineAsync(OddsEvent evt, MarketFingerprint fingerprint)
        {
            var timeUntilGame = evt.CommenceTime - DateTime.UtcNow;

            // Store closing line if game starts within 5 minutes
            if (timeUntilGame > TimeSpan.Zero && timeUntilGame <= ClosingLineCacheWindow)
            {
                var closingKey = $"closingline:{evt.Id}:{fingerprint.Market.MarketType}";
                var existing = await cache.GetAsync<ClosingLineWrapper>(closingKey);

                if (existing is null)
                {
                    await cache.SetAsync(closingKey, new ClosingLineWrapper
                    {
                        ClosingLine = fingerprint.ConsensusLine,
                        RecordedAt = DateTime.UtcNow
                    }, ClosingLineCacheDuration);

                    logger.LogInformation(
                        "Stored closing line for {Event}:{Market} = {Line}",
                        evt.Id, fingerprint.Market.MarketType, fingerprint.ConsensusLine);
                }
            }
        }
    }
}