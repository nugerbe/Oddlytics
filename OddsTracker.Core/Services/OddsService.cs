using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Services
{
    public class OddsService(
        IOddsApiClient oddsClient,
        ISportsDataService sportsDataService,
        IMarketRepository marketRepository,
        ILogger<OddsService> logger) : IOddsService
    {
        public async Task<List<OddsBase>> GetOddsAsync(OddsQueryBase query, SubscriptionTier userTier)
        {
            var market = query.MarketDefinition;

            // Check if user can access this market type
            if (!marketRepository.CanAccessMarket(userTier, market.Key))
            {
                logger.LogWarning(
                    "User tier {Tier} cannot access market {Market}. Required: {Required}",
                    userTier, market.Key, market.RequiredTier);
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

            // Resolve player's team if not specified
            if (string.IsNullOrEmpty(query.Team))
            {
                var playerInfo = await sportsDataService.GetPlayerTeamAsync(query.PlayerName);
                if (playerInfo is not null)
                {
                    query.Team = playerInfo.TeamFullName;
                    logger.LogInformation("Resolved player {Player} to team {Team}",
                        query.PlayerName, playerInfo.TeamFullName);
                }
            }

            var accessibleBookmakers = await marketRepository.GetAccessibleBookmakersAsync(userTier);
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

            // Find game where player's team is playing
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
            var normalized = NormalizePlayerSnapshots(snapshots, query);

            return normalized is not null ? [normalized] : [];
        }

        private async Task<List<OddsBase>> GetGameOddsAsync(GameOddsQuery query, SubscriptionTier userTier)
        {
            var market = query.MarketDefinition;
            var accessibleBookmakers = await marketRepository.GetAccessibleBookmakersAsync(userTier);
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
            var normalized = NormalizeGameSnapshots(snapshots, query);

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
                    bookmakers
                    );
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

        private GameOdds NormalizeGameSnapshots(List<OddsSnapshot> snapshots, OddsQueryBase query)
        {
            var market = query.MarketDefinition;

            if (snapshots.Count == 0)
            {
                return new GameOdds
                {
                    MarketDefinition = market
                };
            }

            var firstEvent = snapshots[0].Event;
            var bookSnapshots = ExtractGameBookSnapshots(snapshots, market);

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

        private PlayerOdds? NormalizePlayerSnapshots(List<OddsSnapshot> snapshots, PlayerOddsQuery query)
        {
            var market = query.MarketDefinition;

            if (snapshots.Count == 0)
                return null;

            var firstEvent = snapshots[0].Event;
            var bookSnapshots = ExtractPlayerBookSnapshots(snapshots, market, query.PlayerName);

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

        private List<GameBookSnapshot> ExtractGameBookSnapshots(
            List<OddsSnapshot> snapshots,
            MarketDefinition market)
        {
            var allBookSnapshots = new List<GameBookSnapshot>();

            foreach (var snapshot in snapshots)
            {
                foreach (var bookmaker in snapshot.Event.Bookmakers ?? [])
                {
                    var bookSnapshot = CreateGameSnapshot(snapshot.Event, bookmaker, market, snapshot.Timestamp);
                    if (bookSnapshot is not null)
                    {
                        allBookSnapshots.Add(bookSnapshot);
                    }
                }
            }

            return allBookSnapshots;
        }

        private List<PlayerBookSnapshot> ExtractPlayerBookSnapshots(
            List<OddsSnapshot> snapshots,
            MarketDefinition market,
            string playerName)
        {
            var allBookSnapshots = new List<PlayerBookSnapshot>();

            foreach (var snapshot in snapshots)
            {
                foreach (var bookmaker in snapshot.Event.Bookmakers ?? [])
                {
                    var bookSnapshot = CreatePlayerSnapshot(bookmaker, market, snapshot.Timestamp, playerName);
                    if (bookSnapshot is not null)
                    {
                        allBookSnapshots.Add(bookSnapshot);
                    }
                }
            }

            return allBookSnapshots;
        }

        /// <summary>
        /// Creates a game book snapshot based on market definition.
        /// Handles spreads, totals, and moneylines with appropriate outcome matching.
        /// </summary>
        private GameBookSnapshot? CreateGameSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            MarketDefinition market,
            DateTime timestamp)
        {
            var apiMarket = bookmaker.Markets?.FirstOrDefault(m =>
                m.Key.Equals(market.Key, StringComparison.OrdinalIgnoreCase));

            if (apiMarket?.Outcomes is null || apiMarket.Outcomes.Count == 0)
                return null;

            // Determine outcome names based on market outcome type
            var (primaryName, secondaryName, usePoint) = GetOutcomeConfig(market, evt);

            var primaryOutcome = apiMarket.Outcomes.FirstOrDefault(o => o.Name == primaryName);
            var secondaryOutcome = apiMarket.Outcomes.FirstOrDefault(o => o.Name == secondaryName);

            if (primaryOutcome is null)
                return null;

            var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key);

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

        /// <summary>
        /// Gets the outcome configuration for a market definition.
        /// Returns (primaryOutcomeName, secondaryOutcomeName, usePointAsLine)
        /// </summary>
        private static (string Primary, string Secondary, bool UsePoint) GetOutcomeConfig(
            MarketDefinition market,
            OddsEvent evt)
        {
            return market.OutcomeType switch
            {
                // Over/Under markets - totals, player props
                OutcomeType.OverUnder => ("Over", "Under", true),

                // Team-based markets
                OutcomeType.TeamBased => market.Key.Contains("h2h") || market.Key.Contains("moneyline")
                    ? (evt.HomeTeam, evt.AwayTeam, false)  // Moneyline - no point
                    : (evt.HomeTeam, evt.AwayTeam, true),   // Spreads - use point

                // Yes/No markets
                OutcomeType.YesNo => ("Yes", "No", false),

                // Named outcome markets (draw, etc.)
                OutcomeType.Named => (evt.HomeTeam, evt.AwayTeam, false),

                // Default to team-based with point
                _ => (evt.HomeTeam, evt.AwayTeam, true)
            };
        }

        private PlayerBookSnapshot? CreatePlayerSnapshot(
            Bookmaker bookmaker,
            MarketDefinition market,
            DateTime timestamp,
            string playerName)
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

            var bookmakerTier = marketRepository.GetBookmakerTier(bookmaker.Key);

            // For Yes/No props (like anytime TD), look for different outcome names
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