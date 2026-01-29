using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Services
{
    public class OddsService(
        IOddsApiClient oddsClient,
        ISportsDataService sportsDataService,
        IServiceScopeFactory scopeFactory,
        IEnhancedCacheService cache,
        ILogger<OddsService> logger) : IOddsService
    {
        #region Cached Repository Access

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

        private async Task<List<BookmakerInfo>> GetAccessibleBookmakersAsync(SubscriptionTier userTier)
        {
            var cacheKey = $"bookmakers:accessible:{userTier}";

            var cached = await cache.GetAsync<List<BookmakerInfo>>(cacheKey);
            if (cached is not null)
                return cached;

            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var bookmakers = await repo.GetAccessibleBookmakersAsync(userTier);

            await cache.SetAsync(cacheKey, bookmakers, TimeSpan.FromHours(1));
            return bookmakers;
        }

        private async Task<bool> CanAccessMarketAsync(SubscriptionTier userTier, string marketKey)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            return repo.CanAccessMarket(userTier, marketKey);
        }

        #endregion

        public async Task<List<OddsBase>> GetOddsAsync(OddsQueryBase query, SubscriptionTier userTier)
        {
            var market = query.MarketDefinition;

            if (!await CanAccessMarketAsync(userTier, market.Key))
            {
                logger.LogWarning(
                    "User tier {Tier} cannot access market {Market}",
                    userTier, market.Key);
                return [];
            }

            return query switch
            {
                PlayerOddsQuery playerQuery => await GetPlayerOddsAsync(playerQuery, userTier),
                GameOddsQuery gameQuery => await GetGameOddsAsync(gameQuery, userTier),
                _ => throw new ArgumentException($"Unknown query type: {query.GetType().Name}")
            };
        }

        private async Task<List<OddsBase>> GetPlayerOddsAsync(PlayerOddsQuery query, SubscriptionTier userTier)
        {
            var market = query.MarketDefinition;

            if (string.IsNullOrEmpty(query.Team))
            {
                var playerInfo = await sportsDataService.GetPlayerTeamAsync(query.Sport, query.PlayerName);
                if (playerInfo is not null)
                {
                    query.Team = playerInfo.TeamFullName;
                    logger.LogInformation("Resolved player {Player} to team {Team}",
                        query.PlayerName, playerInfo.TeamFullName);
                }
            }

            var accessibleBookmakers = await GetAccessibleBookmakersAsync(userTier);
            var markets = new[] { market.Key };
            var bookmakers = accessibleBookmakers.Select(x => x.Key).ToArray();

            logger.LogInformation(
                "Fetching player props for Player: {Player}, Market: {Market}, Sport: {Sport}, Tier: {Tier}",
                query.PlayerName, market.Key, query.Sport, userTier);

            var currentEvents = await oddsClient.GetOddsAsync(query.Sport, markets, bookmakers);
            if (currentEvents is null || currentEvents.Count == 0)
            {
                logger.LogWarning("No odds data returned from The Odds API");
                return [];
            }

            OddsEvent? matchingEvent = null;
            if (!string.IsNullOrEmpty(query.Team))
            {
                matchingEvent = currentEvents.FirstOrDefault(e =>
                    TeamNameEquals(e.HomeTeam, query.Team) || TeamNameEquals(e.AwayTeam, query.Team));
            }

            if (matchingEvent is null)
            {
                logger.LogWarning("No matching game found for player {Player}", query.PlayerName);
                return [];
            }

            var snapshots = await GetSnapshotsAsync(matchingEvent, query, bookmakers, markets);
            var normalized = await NormalizePlayerSnapshotsAsync(snapshots, query);

            return normalized is not null ? [normalized] : [];
        }

        private async Task<List<OddsBase>> GetGameOddsAsync(GameOddsQuery query, SubscriptionTier userTier)
        {
            var market = query.MarketDefinition;
            var accessibleBookmakers = await GetAccessibleBookmakersAsync(userTier);
            var markets = new[] { market.Key };
            var bookmakers = accessibleBookmakers.Select(x => x.Key).ToArray();

            logger.LogInformation(
                "Fetching odds for HomeTeam: {HomeTeam}, AwayTeam: {AwayTeam}, Market: {Market}, Sport: {Sport}, Tier: {Tier}",
                query.HomeTeam, query.AwayTeam ?? "any", market.Key, query.Sport, userTier);

            var currentEvents = await oddsClient.GetOddsAsync(query.Sport, markets, bookmakers);
            if (currentEvents is null || currentEvents.Count == 0)
            {
                logger.LogWarning("No odds data returned from The Odds API");
                return [];
            }

            OddsEvent? matchingEvent = !string.IsNullOrEmpty(query.AwayTeam)
                ? currentEvents.FirstOrDefault(e =>
                    (TeamNameEquals(e.HomeTeam, query.HomeTeam) && TeamNameEquals(e.AwayTeam, query.AwayTeam)) ||
                    (TeamNameEquals(e.HomeTeam, query.AwayTeam) && TeamNameEquals(e.AwayTeam, query.HomeTeam)))
                : currentEvents.FirstOrDefault(e =>
                    TeamNameEquals(e.HomeTeam, query.HomeTeam) || TeamNameEquals(e.AwayTeam, query.HomeTeam));

            if (matchingEvent is null)
            {
                logger.LogWarning("No matching game found for HomeTeam='{Home}', AwayTeam='{Away}'",
                    query.HomeTeam, query.AwayTeam ?? "null");
                return [];
            }

            logger.LogInformation("Matched game: {Away} @ {Home} (ID: {Id})",
                matchingEvent.AwayTeam, matchingEvent.HomeTeam, matchingEvent.Id);

            var snapshots = await GetSnapshotsAsync(matchingEvent, query, bookmakers, markets);
            var normalized = await NormalizeGameSnapshotsAsync(snapshots, query);

            return [normalized];
        }

        private async Task<List<OddsSnapshot>> GetSnapshotsAsync(
            OddsEvent matchingEvent,
            OddsQueryBase query,
            string[] bookmakers,
            string[] markets)
        {
            if (query.DaysBack > 0)
            {
                logger.LogInformation("Fetching line movement for {Days} days back", query.DaysBack);
                var intervalsPerDay = query.DaysBack <= 1 ? 8 : (query.DaysBack <= 3 ? 4 : 2);

                return await oddsClient.GetLineMovementAsync(
                    query.Sport,
                    matchingEvent.Id,
                    query.DaysBack,
                    intervalsPerDay,
                    markets,
                    bookmakers);
            }

            return [new OddsSnapshot(DateTime.UtcNow, matchingEvent)];
        }

        private static bool TeamNameEquals(string? apiTeamName, string? queryTeamName)
        {
            if (string.IsNullOrEmpty(apiTeamName) || string.IsNullOrEmpty(queryTeamName))
                return false;

            var api = apiTeamName.ToLowerInvariant().Trim();
            var query = queryTeamName.ToLowerInvariant().Trim();

            return api == query || api.Contains(query) || query.Contains(api);
        }

        private async Task<GameOdds> NormalizeGameSnapshotsAsync(List<OddsSnapshot> snapshots, OddsQueryBase query)
        {
            var market = query.MarketDefinition;

            if (snapshots.Count == 0)
            {
                return new GameOdds { MarketDefinition = market };
            }

            var firstEvent = snapshots[0].Event;
            var bookSnapshots = await ExtractGameBookSnapshotsAsync(snapshots, market);

            return new GameOdds
            {
                EventId = firstEvent.Id ?? string.Empty,
                HomeTeam = firstEvent.HomeTeam ?? string.Empty,
                AwayTeam = firstEvent.AwayTeam ?? string.Empty,
                CommenceTime = firstEvent.CommenceTime,
                MarketDefinition = market,
                Snapshots = [.. bookSnapshots.Cast<BookSnapshotBase>()]
            };
        }

        private async Task<PlayerOdds?> NormalizePlayerSnapshotsAsync(List<OddsSnapshot> snapshots, PlayerOddsQuery query)
        {
            var market = query.MarketDefinition;

            if (snapshots.Count == 0)
                return null;

            var firstEvent = snapshots[0].Event;
            var bookSnapshots = await ExtractPlayerBookSnapshotsAsync(snapshots, market, query.PlayerName);

            if (bookSnapshots.Count == 0)
                return null;

            var isHome = TeamNameEquals(firstEvent.HomeTeam, query.Team);

            return new PlayerOdds
            {
                EventId = firstEvent.Id ?? string.Empty,
                PlayerName = query.PlayerName,
                Team = query.Team ?? string.Empty,
                Opponent = isHome ? firstEvent.AwayTeam : firstEvent.HomeTeam,
                CommenceTime = firstEvent.CommenceTime,
                MarketDefinition = market,
                Snapshots = [.. bookSnapshots.Cast<BookSnapshotBase>()]
            };
        }

        private async Task<List<GameBookSnapshot>> ExtractGameBookSnapshotsAsync(
            List<OddsSnapshot> snapshots,
            MarketDefinition market)
        {
            var tierLookup = await GetBookmakerTiersAsync();
            var allBookSnapshots = new List<GameBookSnapshot>();

            foreach (var snapshot in snapshots)
            {
                foreach (var bookmaker in snapshot.Event.Bookmakers ?? [])
                {
                    var bookSnapshot = CreateGameSnapshot(snapshot.Event, bookmaker, market, snapshot.Timestamp, tierLookup);
                    if (bookSnapshot is not null)
                    {
                        allBookSnapshots.Add(bookSnapshot);
                    }
                }
            }

            return allBookSnapshots;
        }

        private async Task<List<PlayerBookSnapshot>> ExtractPlayerBookSnapshotsAsync(
            List<OddsSnapshot> snapshots,
            MarketDefinition market,
            string playerName)
        {
            var tierLookup = await GetBookmakerTiersAsync();
            var allBookSnapshots = new List<PlayerBookSnapshot>();

            foreach (var snapshot in snapshots)
            {
                foreach (var bookmaker in snapshot.Event.Bookmakers ?? [])
                {
                    var bookSnapshot = CreatePlayerSnapshot(bookmaker, market, snapshot.Timestamp, playerName, tierLookup);
                    if (bookSnapshot is not null)
                    {
                        allBookSnapshots.Add(bookSnapshot);
                    }
                }
            }

            return allBookSnapshots;
        }

        private static GameBookSnapshot? CreateGameSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            MarketDefinition market,
            DateTime timestamp,
            Dictionary<string, BookmakerTier> tierLookup)
        {
            var apiMarket = bookmaker.Markets?.FirstOrDefault(m =>
                m.Key.Equals(market.Key, StringComparison.OrdinalIgnoreCase));

            if (apiMarket?.Outcomes is null || apiMarket.Outcomes.Count == 0)
                return null;

            var (primaryName, secondaryName, usePoint) = GetOutcomeConfig(market, evt);

            var primaryOutcome = apiMarket.Outcomes.FirstOrDefault(o => o.Name == primaryName);
            var secondaryOutcome = apiMarket.Outcomes.FirstOrDefault(o => o.Name == secondaryName);

            if (primaryOutcome is null)
                return null;

            var bookmakerTier = tierLookup.TryGetValue(bookmaker.Key ?? "", out var tier)
                ? tier
                : BookmakerTier.Retail;

            return new GameBookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                BookType = bookmakerTier,
                Timestamp = timestamp,
                Line = usePoint ? (primaryOutcome.Point ?? 0) : 0,
                HomeOdds = primaryOutcome.Price,
                AwayOdds = secondaryOutcome?.Price
            };
        }

        private static (string Primary, string Secondary, bool UsePoint) GetOutcomeConfig(
            MarketDefinition market,
            OddsEvent evt)
        {
            return market.OutcomeType switch
            {
                OutcomeType.OverUnder => ("Over", "Under", true),
                OutcomeType.TeamBased => market.Key.Contains("h2h") || market.Key.Contains("moneyline")
                    ? (evt.HomeTeam, evt.AwayTeam, false)
                    : (evt.HomeTeam, evt.AwayTeam, true),
                OutcomeType.YesNo => ("Yes", "No", false),
                OutcomeType.Named => (evt.HomeTeam, evt.AwayTeam, false),
                _ => (evt.HomeTeam, evt.AwayTeam, true)
            };
        }

        private static PlayerBookSnapshot? CreatePlayerSnapshot(
            Bookmaker bookmaker,
            MarketDefinition market,
            DateTime timestamp,
            string playerName,
            Dictionary<string, BookmakerTier> tierLookup)
        {
            var apiMarket = bookmaker.Markets?.FirstOrDefault(m =>
                m.Key.Equals(market.Key, StringComparison.OrdinalIgnoreCase));

            if (apiMarket?.Outcomes is null || apiMarket.Outcomes.Count == 0)
                return null;

            var playerOutcomes = apiMarket.Outcomes
                .Where(o => o.Description?.Contains(playerName, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            var overOutcome = playerOutcomes.FirstOrDefault(o => o.Name == "Over");
            var underOutcome = playerOutcomes.FirstOrDefault(o =>
                o.Name == "Under" && o.Description == overOutcome?.Description);

            var bookmakerTier = tierLookup.TryGetValue(bookmaker.Key ?? "", out var tier)
                ? tier
                : BookmakerTier.Retail;

            if (overOutcome is null && market.OutcomeType == OutcomeType.YesNo)
            {
                var yesOutcome = playerOutcomes.FirstOrDefault(o => o.Name == "Yes");
                if (yesOutcome is not null)
                {
                    return new PlayerBookSnapshot
                    {
                        BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                        BookmakerKey = bookmaker.Key ?? "unknown",
                        BookType = bookmakerTier,
                        Timestamp = timestamp,
                        Line = yesOutcome.Point ?? 0,
                        OverOdds = yesOutcome.Price,
                        UnderOdds = playerOutcomes.FirstOrDefault(o => o.Name == "No")?.Price,
                        PlayerName = yesOutcome.Description ?? playerName,
                        PropType = market.DisplayName
                    };
                }
            }

            if (overOutcome is null)
                return null;

            return new PlayerBookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                BookType = bookmakerTier,
                Timestamp = timestamp,
                Line = overOutcome.Point ?? 0,
                OverOdds = overOutcome.Price,
                UnderOdds = underOutcome?.Price,
                PlayerName = overOutcome.Description ?? playerName,
                PropType = market.DisplayName
            };
        }
    }
}