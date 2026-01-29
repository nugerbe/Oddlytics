using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
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
        IServiceScopeFactory scopeFactory,
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

        private static int _pollCounter = 0;
        private const int PlayerPropPollInterval = 5;

        [Function("OddsPoller")]
#if DEBUG
        public async Task Run([TimerTrigger("0 * * * * *", RunOnStartup = true)] MyTimerInfo timerInfo)
#else
        public async Task Run([TimerTrigger("0 * * * * *")] MyTimerInfo timerInfo)
#endif
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

            // Get active sports (cached)
            var activeSports = await GetActiveSportsAsync();

            // Pre-fetch bookmaker tiers once for the entire run
            var bookmakerTiers = await GetBookmakerTiersAsync();

            logger.LogDebug("Polling {Count} active sports (PlayerProps: {PlayerProps})",
                activeSports.Count, shouldPollPlayerProps);

            foreach (var sport in activeSports)
            {
                try
                {
                    var allMarkets = await GetMarketsForSportAsync(sport.Key);
                    var marketsByKey = allMarkets.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

                    var (gameEvents, gameAlerts) = await PollGameMarketsAsync(sport, allMarkets, marketsByKey, bookmakerTiers);
                    totalEvents += gameEvents;
                    alertsSent += gameAlerts;

                    if (shouldPollPlayerProps)
                    {
                        var (propEvents, propAlerts) = await PollPlayerPropsAsync(sport, allMarkets, marketsByKey, bookmakerTiers);
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
            Dictionary<string, MarketDefinition> marketsByKey,
            Dictionary<string, BookmakerTier> bookmakerTiers)
        {
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
                        var sent = await ProcessGameMarketAsync(evt, market, bookmakerTiers);
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
            Dictionary<string, MarketDefinition> marketsByKey,
            Dictionary<string, BookmakerTier> bookmakerTiers)
        {
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

            var events = await oddsClient.GetEventsAsync(sport.Key);

            if (events is null || events.Count == 0)
            {
                return (0, 0);
            }

            var alertsSent = 0;
            var eventsProcessed = 0;

            var upcomingEvents = events
                .Where(e => e.CommenceTime > DateTime.UtcNow && e.CommenceTime < DateTime.UtcNow.AddHours(24))
                .ToList();

            foreach (var evt in upcomingEvents)
            {
                try
                {
                    var eventOdds = await oddsClient.GetEventOddsAsync(evt.Id, sport.Key, propMarkets);

                    if (eventOdds?.Bookmakers is null || eventOdds.Bookmakers.Count == 0)
                        continue;

                    eventsProcessed++;

                    foreach (var marketKey in propMarkets)
                    {
                        if (!marketsByKey.TryGetValue(marketKey, out var market))
                            continue;

                        try
                        {
                            var sent = await ProcessPlayerPropMarketAsync(eventOdds, market, bookmakerTiers);
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

        private async Task<bool> ProcessGameMarketAsync(
            OddsEvent evt,
            MarketDefinition market,
            Dictionary<string, BookmakerTier> bookmakerTiers)
        {
            var bookSnapshots = ExtractGameBookSnapshots(evt, market, bookmakerTiers);
            if (bookSnapshots.Count == 0) return false;

            var previousFingerprint = await fingerprintService.GetPreviousFingerprintAsync(evt.Id, market.Key);

            var fingerprint = await fingerprintService.CreateFingerprintAsync(
                evt.Id,
                market,
                bookSnapshots.Cast<BookSnapshotBase>().ToList(),
                evt.HomeTeam,
                evt.AwayTeam,
                evt.CommenceTime);

            await fingerprintService.SaveFingerprintAsync(fingerprint);
            await TryStoreClosingLineAsync(evt, fingerprint);

            if (!fingerprintService.HasMaterialChange(fingerprint, previousFingerprint))
            {
                logger.LogDebug("No material change for {EventId}:{MarketKey}", evt.Id, market.Key);
                return false;
            }

            return await ProcessAlertAsync(fingerprint);
        }

        private async Task<bool> ProcessPlayerPropMarketAsync(
            OddsEvent evt,
            MarketDefinition market,
            Dictionary<string, BookmakerTier> bookmakerTiers)
        {
            var bookSnapshots = ExtractPlayerBookSnapshots(evt, market, bookmakerTiers);
            if (bookSnapshots.Count == 0) return false;

            var playerGroups = bookSnapshots
                .GroupBy(s => s.PlayerName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var alertSent = false;

            foreach (var playerGroup in playerGroups)
            {
                var playerName = playerGroup.Key;
                var playerSnapshots = playerGroup.ToList();

                var marketCacheKey = $"{market.Key}:{playerName.ToLowerInvariant().Replace(" ", "_")}";

                var previousFingerprint = await fingerprintService.GetPreviousFingerprintAsync(
                    evt.Id, marketCacheKey);

                var fingerprint = await fingerprintService.CreateFingerprintAsync(
                    evt.Id,
                    market,
                    [.. playerSnapshots.Cast<BookSnapshotBase>()],
                    evt.HomeTeam,
                    evt.AwayTeam,
                    evt.CommenceTime);

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
            var confidence = confidenceEngine.CalculateScore(fingerprint);

            await historicalTracker.RecordSignalAsync(fingerprint, confidence);

            var alert = await alertEngine.EvaluateForAlertAsync(fingerprint, confidence);
            if (alert is null) return false;

            if (!await alertEngine.ShouldSendAlertAsync(alert)) return false;

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

        private List<GameBookSnapshot> ExtractGameBookSnapshots(
            OddsEvent evt,
            MarketDefinition market,
            Dictionary<string, BookmakerTier> bookmakerTiers)
        {
            var snapshots = new List<GameBookSnapshot>();

            foreach (var bookmaker in evt.Bookmakers ?? [])
            {
                var apiMarket = bookmaker.Markets?.FirstOrDefault(m =>
                    m.Key.Equals(market.Key, StringComparison.OrdinalIgnoreCase));

                if (apiMarket is null) continue;

                var snapshot = CreateGameSnapshot(evt, bookmaker, apiMarket, market, bookmakerTiers);
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        private List<PlayerBookSnapshot> ExtractPlayerBookSnapshots(
            OddsEvent evt,
            MarketDefinition market,
            Dictionary<string, BookmakerTier> bookmakerTiers)
        {
            var snapshots = new List<PlayerBookSnapshot>();

            foreach (var bookmaker in evt.Bookmakers ?? [])
            {
                var bookmakerTier = bookmakerTiers.TryGetValue(bookmaker.Key ?? "", out var tier)
                    ? tier
                    : BookmakerTier.Retail;

                var apiMarket = bookmaker.Markets?.FirstOrDefault(m =>
                    m.Key.Equals(market.Key, StringComparison.OrdinalIgnoreCase));

                if (apiMarket?.Outcomes is null) continue;

                var playerOutcomes = apiMarket.Outcomes
                    .Where(o => !string.IsNullOrEmpty(o.Description))
                    .GroupBy(o => o.Description!, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var playerGroup in playerOutcomes)
                {
                    var overOutcome = playerGroup.FirstOrDefault(o => o.Name == "Over");
                    var underOutcome = playerGroup.FirstOrDefault(o => o.Name == "Under");

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

        private static GameBookSnapshot? CreateGameSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            Market apiMarket,
            MarketDefinition market,
            Dictionary<string, BookmakerTier> bookmakerTiers)
        {
            var bookmakerTier = bookmakerTiers.TryGetValue(bookmaker.Key ?? "", out var tier)
                ? tier
                : BookmakerTier.Retail;

            return market.OutcomeType switch
            {
                OutcomeType.TeamBased when market.Key.Contains("spread", StringComparison.OrdinalIgnoreCase)
                    => CreateSpreadSnapshot(evt, bookmaker, apiMarket, bookmakerTier),

                OutcomeType.OverUnder
                    => CreateTotalSnapshot(bookmaker, apiMarket, bookmakerTier),

                OutcomeType.TeamBased
                    => CreateMoneylineSnapshot(evt, bookmaker, apiMarket, bookmakerTier),

                OutcomeType.YesNo
                    => CreateYesNoSnapshot(bookmaker, apiMarket, bookmakerTier),

                OutcomeType.Named
                    => CreateNamedSnapshot(evt, bookmaker, apiMarket, bookmakerTier),

                _ => null
            };
        }

        private static GameBookSnapshot? CreateSpreadSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            Market market,
            BookmakerTier bookmakerTier)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            if (homeOutcome is null) return null;

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

        private static GameBookSnapshot? CreateTotalSnapshot(
            Bookmaker bookmaker,
            Market market,
            BookmakerTier bookmakerTier)
        {
            var overOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == "Over");
            if (overOutcome is null) return null;

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

        private static GameBookSnapshot? CreateMoneylineSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            Market market,
            BookmakerTier bookmakerTier)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam);

            if (homeOutcome is null) return null;

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

        private static GameBookSnapshot? CreateYesNoSnapshot(
            Bookmaker bookmaker,
            Market market,
            BookmakerTier bookmakerTier)
        {
            var yesOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == "Yes");
            if (yesOutcome is null) return null;

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

        private static GameBookSnapshot? CreateNamedSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            Market market,
            BookmakerTier bookmakerTier)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam);

            if (homeOutcome is null) return null;

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

        #endregion

        #region Cached Repository Access
        private async Task<List<Sport>> GetActiveSportsAsync()
        {
            const string cacheKey = "sports:active";

            var cached = await cache.GetAsync<List<Sport>>(cacheKey);
            if (cached is not null)
                return cached;

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var sports = await repo.GetAllSportsAsync();
            var active = sports.Where(s => s.IsActive).ToList();

            await cache.SetAsync(cacheKey, active, TimeSpan.FromMinutes(30));
            return active;
        }

        private async Task<List<MarketDefinition>> GetMarketsForSportAsync(string sportKey)
        {
            var cacheKey = $"markets:sport:{sportKey}";

            var cached = await cache.GetAsync<List<MarketDefinition>>(cacheKey);
            if (cached is not null)
                return cached;

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var markets = await repo.GetMarketsForSportAsync(sportKey);

            await cache.SetAsync(cacheKey, markets, TimeSpan.FromHours(1));
            return markets;
        }

        private async Task<Dictionary<string, BookmakerTier>> GetBookmakerTiersAsync()
        {
            const string cacheKey = "bookmakers:tiers";

            var cached = await cache.GetAsync<Dictionary<string, BookmakerTier>>(cacheKey);
            if (cached is not null)
                return cached;

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var bookmakers = await repo.GetAllBookmakersAsync();
            var tiers = bookmakers.ToDictionary(
                b => b.Key,
                b => b.Tier,
                StringComparer.OrdinalIgnoreCase);

            await cache.SetAsync(cacheKey, tiers, TimeSpan.FromHours(1));
            return tiers;
        }

        #endregion
    }
}