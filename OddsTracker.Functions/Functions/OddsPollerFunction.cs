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
        IMarketRepository marketRepository,
        IMovementFingerprintService fingerprintService,
        IConfidenceScoringEngine confidenceEngine,
        IAlertEngine alertEngine,
        IHistoricalTracker historicalTracker,
        IEnhancedCacheService cache,
        IDiscordAlertService discordService,
        ILogger<OddsPollerFunction> logger)
    {
        private static readonly TimeSpan ClosingLineCacheWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ClosingLineCacheDuration = TimeSpan.FromHours(8);

        // Player props polling frequency - every 5th run (5 minutes)
        private static int _pollCounter = 0;
        private const int PlayerPropPollInterval = 5;

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

            _pollCounter++;

            var totalEvents = 0;
            var alertsSent = 0;
            var shouldPollPlayerProps = _pollCounter % PlayerPropPollInterval == 0;

            // Get active sports from database
            var sports = await marketRepository.GetAllSportsAsync();
            var activeSports = sports.Where(s => s.IsActive).ToList();

            logger.LogDebug("Polling {Count} active sports (PlayerProps: {PlayerProps})",
                activeSports.Count, shouldPollPlayerProps);

            foreach (var sport in activeSports)
            {
                try
                {
                    // Get all markets available for this sport
                    var allMarkets = await marketRepository.GetMarketsForSportAsync(sport.Key);
                    var marketsByKey = allMarkets.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

                    // Poll game markets (spreads, totals, moneylines)
                    var (gameEvents, gameAlerts) = await PollGameMarketsAsync(sport, allMarkets, marketsByKey);
                    totalEvents += gameEvents;
                    alertsSent += gameAlerts;

                    // Poll player props less frequently
                    if (shouldPollPlayerProps)
                    {
                        var (propEvents, propAlerts) = await PollPlayerPropsAsync(sport, allMarkets, marketsByKey);
                        totalEvents += propEvents;
                        alertsSent += propAlerts;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error polling {Sport}", sport.Key);
                }
            }

            logger.LogInformation(
                "OddsPoller completed: {Events} events processed, {Alerts} alerts sent",
                totalEvents, alertsSent);
        }

        private async Task<(int events, int alerts)> PollGameMarketsAsync(
            Sport sport,
            IReadOnlyList<MarketDefinition> allMarkets,
            Dictionary<string, MarketDefinition> marketsByKey)
        {
            // Filter to game-level markets (non-player props, non-alternates)
            var gameMarkets = allMarkets
                .Where(m => !m.IsPlayerProp && !m.IsAlternate)
                .Select(m => m.Key)
                .ToArray();

            if (gameMarkets.Length == 0)
            {
                logger.LogDebug("No game markets configured for {Sport}", sport.Key);
                return (0, 0);
            }

            logger.LogDebug("Polling {Sport} for game markets: {Markets}",
                sport.Key, string.Join(", ", gameMarkets.Take(5)));

            var events = await oddsClient.GetOddsAsync(sport.Key, gameMarkets);

            if (events is null || events.Count == 0)
            {
                logger.LogDebug("No events returned for {Sport}", sport.Key);
                return (0, 0);
            }

            var alertsSent = 0;

            foreach (var evt in events)
            {
                foreach (var marketKey in gameMarkets)
                {
                    if (!marketsByKey.TryGetValue(marketKey, out var market))
                        continue;

                    try
                    {
                        var sent = await ProcessGameMarketAsync(evt, market);
                        if (sent) alertsSent++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing game market {Event} {Market}", evt.Id, marketKey);
                    }
                }
            }

            return (events.Count, alertsSent);
        }

        private async Task<(int events, int alerts)> PollPlayerPropsAsync(
            Sport sport,
            IReadOnlyList<MarketDefinition> allMarkets,
            Dictionary<string, MarketDefinition> marketsByKey)
        {
            // Filter to player prop markets (non-alternates)
            var propMarkets = allMarkets
                .Where(m => m.IsPlayerProp && !m.IsAlternate)
                .Select(m => m.Key)
                .ToArray();

            if (propMarkets.Length == 0)
            {
                logger.LogDebug("No player prop markets configured for {Sport}", sport.Key);
                return (0, 0);
            }

            logger.LogDebug("Polling {Sport} for player prop markets: {Count} markets",
                sport.Key, propMarkets.Length);

            // First get events to know which games have props available
            var events = await oddsClient.GetEventsAsync(sport.Key);

            if (events is null || events.Count == 0)
            {
                return (0, 0);
            }

            var alertsSent = 0;
            var eventsProcessed = 0;

            // For player props, we need to fetch per-event odds
            // Limit to upcoming games (next 24 hours) to conserve API calls
            var upcomingEvents = events
                .Where(e => e.CommenceTime > DateTime.UtcNow && e.CommenceTime < DateTime.UtcNow.AddHours(24))
                .ToList();

            foreach (var evt in upcomingEvents)
            {
                try
                {
                    // Get player prop odds for this event
                    var eventOdds = await oddsClient.GetEventOddsAsync(evt.Id, sport.Key, propMarkets);

                    if (eventOdds?.Bookmakers is null || eventOdds.Bookmakers.Count == 0)
                        continue;

                    eventsProcessed++;

                    // Process each player prop market
                    foreach (var marketKey in propMarkets)
                    {
                        if (!marketsByKey.TryGetValue(marketKey, out var market))
                            continue;

                        try
                        {
                            var sent = await ProcessPlayerPropMarketAsync(eventOdds, market);
                            if (sent) alertsSent++;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error processing player prop {Event} {Market}",
                                evt.Id, marketKey);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error fetching player props for event {EventId}", evt.Id);
                }
            }

            return (eventsProcessed, alertsSent);
        }

        private async Task<bool> ProcessGameMarketAsync(OddsEvent evt, MarketDefinition market)
        {
            // Extract book snapshots from event
            var bookSnapshots = ExtractGameBookSnapshots(evt, market);
            if (bookSnapshots.Count == 0) return false;

            // Get previous fingerprint from cache
            var previousFingerprint = await fingerprintService.GetPreviousFingerprintAsync(evt.Id, market.Key);

            // Create new fingerprint
            var fingerprint = await fingerprintService.CreateFingerprintAsync(
                evt.Id,
                market,
                bookSnapshots.Cast<BookSnapshotBase>().ToList(),
                evt.HomeTeam,
                evt.AwayTeam,
                evt.CommenceTime);

            // Save fingerprint to cache
            await fingerprintService.SaveFingerprintAsync(fingerprint);

            // Store closing line if game is about to start
            await TryStoreClosingLineAsync(evt, fingerprint);

            // Check for material change
            if (!fingerprintService.HasMaterialChange(fingerprint, previousFingerprint))
            {
                logger.LogDebug("No material change for {EventId}:{MarketKey}", evt.Id, market.Key);
                return false;
            }

            return await ProcessAlertAsync(fingerprint);
        }

        private async Task<bool> ProcessPlayerPropMarketAsync(OddsEvent evt, MarketDefinition market)
        {
            // Extract player book snapshots from event
            var bookSnapshots = ExtractPlayerBookSnapshots(evt, market);
            if (bookSnapshots.Count == 0) return false;

            // Group by player (each player is essentially a separate market)
            var playerGroups = bookSnapshots
                .GroupBy(s => s.PlayerName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var alertSent = false;

            foreach (var playerGroup in playerGroups)
            {
                var playerName = playerGroup.Key;
                var playerSnapshots = playerGroup.ToList();

                // Use player name in the cache key to differentiate
                var marketCacheKey = $"{market.Key}:{playerName.ToLowerInvariant().Replace(" ", "_")}";

                // Get previous fingerprint for this player
                var previousFingerprint = await fingerprintService.GetPreviousFingerprintAsync(
                    evt.Id, marketCacheKey);

                // Create fingerprint for this player's prop
                var fingerprint = await fingerprintService.CreateFingerprintAsync(
                    evt.Id,
                    market,
                    [.. playerSnapshots.Cast<BookSnapshotBase>()],
                    evt.HomeTeam,
                    evt.AwayTeam,
                    evt.CommenceTime);

                // Override the market key to include player
                // Note: This requires MarketKey to be settable or using a different approach

                await fingerprintService.SaveFingerprintAsync(fingerprint);

                if (!fingerprintService.HasMaterialChange(fingerprint, previousFingerprint))
                    continue;

                if (await ProcessAlertAsync(fingerprint))
                    alertSent = true;
            }

            return alertSent;
        }

        private async Task<bool> ProcessAlertAsync(MarketFingerprint fingerprint)
        {
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
                "Alert sent for {EventId}:{MarketKey}: {Type} (Confidence: {Score})",
                fingerprint.Market.EventId,
                fingerprint.Market.MarketType.Key,
                alert.Type,
                confidence.Score);

            return true;
        }

        #region Snapshot Extraction

        private List<GameBookSnapshot> ExtractGameBookSnapshots(OddsEvent evt, MarketDefinition market)
        {
            var snapshots = new List<GameBookSnapshot>();

            foreach (var bookmaker in evt.Bookmakers ?? [])
            {
                var apiMarket = bookmaker.Markets?.FirstOrDefault(m =>
                    m.Key.Equals(market.Key, StringComparison.OrdinalIgnoreCase));

                if (apiMarket is null) continue;

                var snapshot = CreateGameSnapshot(evt, bookmaker, apiMarket, market);
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        private List<PlayerBookSnapshot> ExtractPlayerBookSnapshots(OddsEvent evt, MarketDefinition market)
        {
            var snapshots = new List<PlayerBookSnapshot>();

            foreach (var bookmaker in evt.Bookmakers ?? [])
            {
                var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key ?? "");

                var apiMarket = bookmaker.Markets?.FirstOrDefault(m =>
                    m.Key.Equals(market.Key, StringComparison.OrdinalIgnoreCase));

                if (apiMarket?.Outcomes is null) continue;

                // Player props have outcomes with description containing player name
                var playerOutcomes = apiMarket.Outcomes
                    .Where(o => !string.IsNullOrEmpty(o.Description))
                    .GroupBy(o => o.Description!, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var playerGroup in playerOutcomes)
                {
                    var overOutcome = playerGroup.FirstOrDefault(o => o.Name == "Over");
                    var underOutcome = playerGroup.FirstOrDefault(o => o.Name == "Under");

                    // For Yes/No props
                    if (overOutcome is null && market.OutcomeType == OutcomeType.YesNo)
                    {
                        var yesOutcome = playerGroup.FirstOrDefault(o => o.Name == "Yes");
                        if (yesOutcome is not null)
                        {
                            snapshots.Add(new PlayerBookSnapshot
                            {
                                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                                BookmakerKey = bookmaker.Key ?? "unknown",
                                BookType = bookmakerTier,
                                PlayerName = playerGroup.Key,
                                Line = yesOutcome.Point ?? 0,
                                OverOdds = yesOutcome.Price,
                                UnderOdds = playerGroup.FirstOrDefault(o => o.Name == "No")?.Price,
                                PropType = market.DisplayName,
                                Timestamp = bookmaker.LastUpdate
                            });
                        }
                        continue;
                    }

                    if (overOutcome is null) continue;

                    snapshots.Add(new PlayerBookSnapshot
                    {
                        BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                        BookmakerKey = bookmaker.Key ?? "unknown",
                        BookType = bookmakerTier,
                        PlayerName = playerGroup.Key,
                        Line = overOutcome.Point ?? 0,
                        OverOdds = overOutcome.Price,
                        UnderOdds = underOutcome?.Price,
                        PropType = market.DisplayName,
                        Timestamp = bookmaker.LastUpdate
                    });
                }
            }

            return snapshots;
        }

        private GameBookSnapshot? CreateGameSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            Market apiMarket,
            MarketDefinition market)
        {
            return market.OutcomeType switch
            {
                OutcomeType.TeamBased when market.Key.Contains("spread", StringComparison.OrdinalIgnoreCase)
                    => CreateSpreadSnapshot(evt, bookmaker, apiMarket),

                OutcomeType.OverUnder
                    => CreateTotalSnapshot(bookmaker, apiMarket),

                OutcomeType.TeamBased
                    => CreateMoneylineSnapshot(evt, bookmaker, apiMarket),

                OutcomeType.YesNo
                    => CreateYesNoSnapshot(bookmaker, apiMarket),

                OutcomeType.Named
                    => CreateNamedSnapshot(evt, bookmaker, apiMarket),

                _ => null
            };
        }

        private GameBookSnapshot? CreateSpreadSnapshot(OddsEvent evt, Bookmaker bookmaker, Market market)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            if (homeOutcome is null) return null;

            var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key ?? "");

            return new GameBookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                BookType = bookmakerTier,
                Line = homeOutcome.Point ?? 0,
                HomeOdds = homeOutcome.Price,
                AwayOdds = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam)?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        private GameBookSnapshot? CreateTotalSnapshot(Bookmaker bookmaker, Market market)
        {
            var overOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == "Over");
            if (overOutcome is null) return null;

            var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key ?? "");

            return new GameBookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                BookType = bookmakerTier,
                Line = overOutcome.Point ?? 0,
                HomeOdds = overOutcome.Price,
                AwayOdds = market.Outcomes?.FirstOrDefault(o => o.Name == "Under")?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        private GameBookSnapshot? CreateMoneylineSnapshot(OddsEvent evt, Bookmaker bookmaker, Market market)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam);

            if (homeOutcome is null) return null;

            var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key ?? "");

            return new GameBookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                BookType = bookmakerTier,
                Line = homeOutcome.Price,
                HomeOdds = homeOutcome.Price,
                AwayOdds = awayOutcome?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        private GameBookSnapshot? CreateYesNoSnapshot(Bookmaker bookmaker, Market market)
        {
            var yesOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == "Yes");
            if (yesOutcome is null) return null;

            var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key ?? "");

            return new GameBookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                BookType = bookmakerTier,
                Line = yesOutcome.Price,
                HomeOdds = yesOutcome.Price,
                AwayOdds = market.Outcomes?.FirstOrDefault(o => o.Name == "No")?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        private GameBookSnapshot? CreateNamedSnapshot(OddsEvent evt, Bookmaker bookmaker, Market market)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam);

            if (homeOutcome is null) return null;

            var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key ?? "");

            return new GameBookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                BookType = bookmakerTier,
                Line = homeOutcome.Price,
                HomeOdds = homeOutcome.Price,
                AwayOdds = awayOutcome?.Price,
                Timestamp = bookmaker.LastUpdate
            };
        }

        #endregion

        private async Task TryStoreClosingLineAsync(OddsEvent evt, MarketFingerprint fingerprint)
        {
            var timeUntilGame = evt.CommenceTime - DateTime.UtcNow;

            if (timeUntilGame > TimeSpan.Zero && timeUntilGame <= ClosingLineCacheWindow)
            {
                var closingKey = $"closingline:{evt.Id}:{fingerprint.Market.MarketType.Key}";
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
                        evt.Id, fingerprint.Market.MarketType.Key, fingerprint.ConsensusLine);
                }
            }
        }
    }
}