using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services
{
    public class OddsService : IOddsService
    {
        private readonly IOddsApiClient _oddsClient;
        private readonly ISportsDataService _sportsDataService;
        private readonly IMarketAccessService _marketAccessService;
        private readonly ILogger<OddsService> _logger;

        public OddsService(
            IOddsApiClient oddsClient,
            ISportsDataService sportsDataService,
            IMarketAccessService marketAccessService,
            ILogger<OddsService> logger)
        {
            _oddsClient = oddsClient;
            _sportsDataService = sportsDataService;
            _marketAccessService = marketAccessService;
            _logger = logger;
        }

        public async Task<List<NormalizedOdds>> GetOddsAsync(OddsQuery query, SubscriptionTier userTier)
        {
            // Check if user can access this market type
            if (!_marketAccessService.CanAccessMarket(userTier, query.MarketType))
            {
                _logger.LogWarning(
                    "User tier {Tier} cannot access market {Market}. Required: {Required}",
                    userTier, query.MarketType, query.MarketType.RequiredTier());
                return [];
            }

            // If player prop with no team, look up the player's team
            if (query.MarketType.IsPlayerProp() &&
                !string.IsNullOrEmpty(query.PlayerName) &&
                string.IsNullOrEmpty(query.HomeTeam))
            {
                var playerInfo = await _sportsDataService.GetPlayerTeamAsync(query.PlayerName);
                if (playerInfo is not null)
                {
                    query.HomeTeam = playerInfo.TeamFullName;
                    _logger.LogInformation(
                        "Resolved player {Player} to team {Team}",
                        query.PlayerName, playerInfo.TeamFullName);
                }
                else
                {
                    _logger.LogWarning(
                        "Could not resolve player {Player} to a team. Query may fail.",
                        query.PlayerName);
                }
            }

            // Get bookmakers accessible to this tier
            var accessibleBookmakers = _marketAccessService.FilterBookmakersByTier(userTier);

            _logger.LogInformation(
                "Fetching odds for HomeTeam: {HomeTeam}, AwayTeam: {AwayTeam}, Market: {Market}, Player: {Player}, Tier: {Tier}, Bookmakers: {Books}",
                query.HomeTeam, query.AwayTeam ?? "any", query.MarketType, query.PlayerName ?? "N/A",
                userTier, string.Join(", ", accessibleBookmakers));

            // Use the extension method to get the API market key
            var marketKey = query.MarketType.ToApiKey();
            var markets = new[] { marketKey };

            // Step 1: Get current odds to find the event ID (filtered by tier bookmakers)
            var currentEvents = await _oddsClient.GetNflOddsAsync(markets, accessibleBookmakers);

            if (currentEvents is null || currentEvents.Count == 0)
            {
                _logger.LogWarning("No odds data returned from The Odds API");
                return [];
            }

            _logger.LogInformation("Received odds for {Count} games", currentEvents.Count);

            // Step 2: Find the matching game
            OddsEvent? matchingEvent;

            if (!string.IsNullOrEmpty(query.AwayTeam))
            {
                // Both teams specified - find game with both teams
                matchingEvent = currentEvents.FirstOrDefault(e =>
                    (TeamNameEquals(e.HomeTeam, query.HomeTeam) && TeamNameEquals(e.AwayTeam, query.AwayTeam)) ||
                    (TeamNameEquals(e.HomeTeam, query.AwayTeam) && TeamNameEquals(e.AwayTeam, query.HomeTeam)));
            }
            else
            {
                // Single team - find game where that team is playing (home or away)
                matchingEvent = currentEvents.FirstOrDefault(e =>
                    TeamNameEquals(e.HomeTeam, query.HomeTeam) || TeamNameEquals(e.AwayTeam, query.HomeTeam));
            }

            if (matchingEvent is null)
            {
                _logger.LogWarning("No matching game found for query HomeTeam='{Home}', AwayTeam='{Away}'",
                    query.HomeTeam, query.AwayTeam ?? "null");
                return [];
            }

            _logger.LogInformation("Matched game: {Away} @ {Home} (ID: {Id})",
                matchingEvent.AwayTeam, matchingEvent.HomeTeam, matchingEvent.Id);

            // Step 3: Get line movement history if days > 0
            List<OddsSnapshot> snapshots;
            if (query.DaysBack > 0)
            {
                _logger.LogInformation("Fetching line movement for {Days} days back", query.DaysBack);

                var intervalsPerDay = query.DaysBack <= 1 ? 8 : (query.DaysBack <= 3 ? 4 : 2);

                snapshots = await _oddsClient.GetLineMovementAsync(
                    matchingEvent.Id,
                    query.DaysBack,
                    intervalsPerDay,
                    markets,
                    accessibleBookmakers);

                _logger.LogInformation("Retrieved {Count} historical snapshots", snapshots.Count);
            }
            else
            {
                snapshots = [new OddsSnapshot(DateTime.UtcNow, matchingEvent)];
            }

            // Step 4: Normalize all snapshots into our format
            var normalized = NormalizeSnapshots(snapshots, query.MarketType, query.PlayerName);

            return [normalized];
        }

        private static bool TeamNameEquals(string? apiTeamName, string? queryTeamName)
        {
            if (string.IsNullOrEmpty(apiTeamName) || string.IsNullOrEmpty(queryTeamName))
                return false;

            var api = apiTeamName.ToLowerInvariant().Trim();
            var query = queryTeamName.ToLowerInvariant().Trim();

            return api == query || api.Contains(query) || query.Contains(api);
        }

        private static NormalizedOdds NormalizeSnapshots(
            List<OddsSnapshot> snapshots,
            MarketType marketType,
            string? playerName = null)
        {
            if (snapshots.Count == 0)
            {
                return new NormalizedOdds();
            }

            var firstEvent = snapshots[0].Event;
            var allBookSnapshots = new List<BookSnapshot>();

            foreach (var snapshot in snapshots)
            {
                var evt = snapshot.Event;
                var timestamp = snapshot.Timestamp;

                foreach (var bookmaker in evt.Bookmakers ?? [])
                {
                    // For player props, we may get multiple players - create a snapshot for each
                    if (marketType.IsPlayerProp() && string.IsNullOrEmpty(playerName))
                    {
                        var market = bookmaker.Markets?.FirstOrDefault(m => m.Key == marketType.ToApiKey());
                        if (market?.Outcomes is not null)
                        {
                            // Get unique player names
                            var players = market.Outcomes
                                .Where(o => o.Name == "Over" && !string.IsNullOrEmpty(o.Description))
                                .Select(o => o.Description!)
                                .Distinct();

                            foreach (var player in players)
                            {
                                var bookSnapshot = CreateSnapshot(evt, bookmaker, marketType, timestamp, player);
                                if (bookSnapshot is not null)
                                {
                                    allBookSnapshots.Add(bookSnapshot);
                                }
                            }
                        }
                    }
                    else
                    {
                        var bookSnapshot = CreateSnapshot(evt, bookmaker, marketType, timestamp, playerName);
                        if (bookSnapshot is not null)
                        {
                            allBookSnapshots.Add(bookSnapshot);
                        }
                    }
                }
            }

            return new NormalizedOdds
            {
                EventId = firstEvent.Id ?? string.Empty,
                HomeTeam = firstEvent.HomeTeam ?? string.Empty,
                AwayTeam = firstEvent.AwayTeam ?? string.Empty,
                CommenceTime = firstEvent.CommenceTime,
                MarketType = marketType,
                PlayerName = playerName,
                Snapshots = allBookSnapshots
            };
        }

        private static BookSnapshot? CreateSnapshot(
            OddsEvent evt,
            Bookmaker bookmaker,
            MarketType marketType,
            DateTime timestamp,
            string? playerName = null)
        {
            var marketKey = marketType.ToApiKey();
            var market = bookmaker.Markets?.FirstOrDefault(m => m.Key == marketKey);
            if (market is null) return null;

            // Handle player props
            if (marketType.IsPlayerProp())
            {
                return CreatePlayerPropSnapshot(bookmaker, market, timestamp, playerName);
            }

            // Handle game lines
            return marketType switch
            {
                MarketType.Spread or MarketType.SpreadH1 or MarketType.SpreadH2 or
                MarketType.SpreadQ2 or MarketType.SpreadQ3 or MarketType.SpreadQ4 or
                MarketType.AlternateSpread
                    => CreateSpreadSnapshot(evt, bookmaker, market, timestamp),

                MarketType.Total or MarketType.TotalH1 or MarketType.TotalH2 or
                MarketType.TotalQ2 or MarketType.TotalQ3 or MarketType.TotalQ4 or
                MarketType.AlternateTotal or MarketType.TeamTotal or MarketType.AlternateTeamTotal
                    => CreateTotalSnapshot(bookmaker, market, timestamp),

                MarketType.Moneyline or MarketType.MoneylineH1 or MarketType.MoneylineH2 or
                MarketType.MoneylineQ2 or MarketType.MoneylineQ3 or MarketType.MoneylineQ4 or
                MarketType.ThreeWayH1
                    => CreateMoneylineSnapshot(evt, bookmaker, market, timestamp),

                _ => null
            };
        }

        private static BookSnapshot? CreatePlayerPropSnapshot(
            Bookmaker bookmaker,
            Market market,
            DateTime timestamp,
            string? playerName = null)
        {
            // If player name specified, filter by that player
            var outcomes = market.Outcomes ?? [];

            if (!string.IsNullOrEmpty(playerName))
            {
                outcomes = outcomes
                    .Where(o => o.Description?.Contains(playerName, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }

            // Get Over/Under for the player
            var overOutcome = outcomes.FirstOrDefault(o => o.Name == "Over");
            var underOutcome = outcomes.FirstOrDefault(o =>
                o.Name == "Under" && o.Description == overOutcome?.Description);

            if (overOutcome is null) return null;

            return new BookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                Timestamp = timestamp,
                Line = overOutcome.Point ?? 0,
                HomeOdds = overOutcome.Price,  // Over odds
                AwayOdds = underOutcome?.Price, // Under odds
                PlayerName = overOutcome.Description,
                OutcomeName = "Over/Under"
            };
        }

        private static string GetMarketKey(MarketType marketType) => marketType.ToApiKey();

        private static BookSnapshot? CreateSpreadSnapshot(
            OddsEvent evt, Bookmaker bookmaker, Market market, DateTime timestamp)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam);

            if (homeOutcome is null) return null;

            return new BookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                Timestamp = timestamp,
                Line = homeOutcome.Point ?? 0,
                HomeOdds = homeOutcome.Price,
                AwayOdds = awayOutcome?.Price
            };
        }

        private static BookSnapshot? CreateTotalSnapshot(
            Bookmaker bookmaker, Market market, DateTime timestamp)
        {
            var overOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == "Over");
            var underOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == "Under");

            if (overOutcome is null) return null;

            return new BookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                Timestamp = timestamp,
                Line = overOutcome.Point ?? 0,
                HomeOdds = overOutcome.Price,
                AwayOdds = underOutcome?.Price
            };
        }

        private static BookSnapshot? CreateMoneylineSnapshot(
            OddsEvent evt, Bookmaker bookmaker, Market market, DateTime timestamp)
        {
            var homeOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.HomeTeam);
            var awayOutcome = market.Outcomes?.FirstOrDefault(o => o.Name == evt.AwayTeam);

            if (homeOutcome is null) return null;

            return new BookSnapshot
            {
                BookmakerName = bookmaker.Title ?? bookmaker.Key ?? "Unknown",
                BookmakerKey = bookmaker.Key ?? "unknown",
                Timestamp = timestamp,
                Line = 0,
                HomeOdds = homeOutcome.Price,
                AwayOdds = awayOutcome?.Price
            };
        }
    }
}