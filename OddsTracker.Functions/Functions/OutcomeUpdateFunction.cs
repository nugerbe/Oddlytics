using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;

namespace OddsTracker.Functions.Functions
{
    /// <summary>
    /// Timer-triggered function that updates signal outcomes after games complete.
    /// Runs every 15 minutes, checks for completed games, and updates historical records.
    /// </summary>
    public class OutcomeUpdateFunction(
        IOddsApiClient oddsClient,
        ISportsDataService sportsDataService,
        IServiceScopeFactory scopeFactory,
        IHistoricalTracker historicalTracker,
        IEnhancedCacheService cache,
        ILogger<OutcomeUpdateFunction> logger)
    {
        /// Timer trigger: runs every 15 minutes
        /// CRON: "0 */15 * * * *" = at minute 0, 15, 30, 45 of every hour
        /// </summary>
        [Function("OutcomeUpdate")]
#if DEBUG
        public async Task Run([TimerTrigger("0 */15 * * * *", RunOnStartup = true)] MyTimerInfo timerInfo)
#else
        public async Task Run([TimerTrigger("0 */15 * * * *")] MyTimerInfo timerInfo)
#endif
        {
            logger.LogInformation("OutcomeUpdate function triggered at {Time}", DateTime.UtcNow);

            if (timerInfo.IsPastDue)
            {
                logger.LogWarning("Timer is running late");
            }

            try
            {
                await UpdateOutcomesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating outcomes");
                throw;
            }
        }

        private async Task UpdateOutcomesAsync()
        {
            var activeSports = await GetActiveSportsAsync();

            var totalProcessed = 0;
            var totalUpdated = 0;

            foreach (var sport in activeSports)
            {
                try
                {
                    var scores = await oddsClient.GetScoresAsync(sport.Key, daysFrom: 1);

                    var completedGames = scores
                        .Where(s => s.Completed == true)
                        .ToList();

                    if (completedGames.Count == 0)
                    {
                        logger.LogDebug("No completed games for {Sport}", sport.Key);
                        continue;
                    }

                    logger.LogInformation("Processing {Count} completed games for {Sport}",
                        completedGames.Count, sport.Key);

                    var sportMarkets = await GetMarketsForSportAsync(sport.Key);
                    var trackableMarkets = sportMarkets
                        .Where(m => !m.IsPlayerProp && !m.IsAlternate)
                        .ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

                    // Pre-fetch sport-specific game data for period scores
                    var sportGames = await GetSportGamesAsync(sport.Key);

                    foreach (var game in completedGames)
                    {
                        try
                        {
                            var gameUpdates = await ProcessCompletedGameAsync(game, trackableMarkets, sport.Key, sportGames);
                            totalProcessed++;
                            totalUpdated += gameUpdates;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error processing game {EventId}", game.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing sport {Sport}", sport.Key);
                }
            }

            logger.LogInformation(
                "OutcomeUpdate completed: {Processed} games processed, {Updated} outcomes updated",
                totalProcessed, totalUpdated);
        }

        private async Task<List<GameInfo>> GetSportGamesAsync(string sportKey)
        {
            try
            {
                return await sportsDataService.GetSeasonGamesAsync(sportKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch sport games for {Sport}", sportKey);
                return [];
            }
        }

        private async Task<int> ProcessCompletedGameAsync(
            ScoreEvent game,
            Dictionary<string, MarketDefinition> marketsByKey,
            string sportKey,
            List<GameInfo> sportGames)
        {
            var updatedCount = 0;

            foreach (var (marketKey, market) in marketsByKey)
            {
                var closingCacheKey = $"closingline:{game.Id}:{marketKey}";
                var closingLineWrapper = await cache.GetAsync<ClosingLineWrapper>(closingCacheKey);

                if (closingLineWrapper is null)
                    continue;

                var outcome = DetermineOutcome(game, market, closingLineWrapper.ClosingLine, sportGames);

                if (outcome.HasValue)
                {
                    await historicalTracker.UpdateOutcomeAsync(
                        game.Id ?? string.Empty,
                        marketKey,
                        closingLineWrapper.ClosingLine,
                        outcome.Value);

                    logger.LogInformation(
                        "Updated outcome for {Game} {Market}: {Outcome}",
                        game.Id,
                        market.DisplayName,
                        outcome.Value);

                    await cache.RemoveAsync(closingCacheKey);
                    updatedCount++;
                }
            }

            return updatedCount;
        }

        private SignalOutcome? DetermineOutcome(
            ScoreEvent game,
            MarketDefinition market,
            decimal closingLine,
            List<GameInfo> sportGames)
        {
            var scores = GetScores(game);
            if (scores is null)
                return null;

            var (homeScore, awayScore) = scores.Value;

            if (market.Period is not null)
            {
                var periodScores = GetPeriodScores(game, (GamePeriod)market.Period, sportGames);
                if (periodScores is null)
                {
                    logger.LogDebug("No period scores available for {Market}", market.Key);
                    return null;
                }
                (homeScore, awayScore) = periodScores.Value;
            }

            var totalScore = homeScore + awayScore;
            var margin = homeScore - awayScore;

            return market.OutcomeType switch
            {
                OutcomeType.OverUnder => DetermineOverUnderOutcome(totalScore, closingLine, market),
                OutcomeType.TeamBased => DetermineTeamBasedOutcome(margin, closingLine, market),
                OutcomeType.YesNo => DetermineYesNoOutcome(game, closingLine, market),
                OutcomeType.Named => DetermineNamedOutcome(margin, closingLine, market),
                _ => null
            };
        }

        #region Outcome Determination by Type

        private static SignalOutcome DetermineOverUnderOutcome(int totalScore, decimal closingLine, MarketDefinition market)
        {
            if (market.Key.Contains("team_total", StringComparison.OrdinalIgnoreCase))
            {
                // For team totals, closingLine is for a specific team
                // We'd need to know which team - for now treat as regular total
            }

            if (totalScore > closingLine)
                return SignalOutcome.Extended;
            if (totalScore < closingLine)
                return SignalOutcome.Reverted;

            return SignalOutcome.Stable;
        }

        private static SignalOutcome DetermineTeamBasedOutcome(int margin, decimal closingLine, MarketDefinition market)
        {
            if (market.Key.Contains("spread", StringComparison.OrdinalIgnoreCase) ||
                market.Key.Contains("handicap", StringComparison.OrdinalIgnoreCase))
            {
                var adjustedMargin = margin + closingLine;

                if (adjustedMargin > 0)
                    return SignalOutcome.Extended;
                if (adjustedMargin < 0)
                    return SignalOutcome.Reverted;

                return SignalOutcome.Stable;
            }

            if (market.Key.Contains("h2h", StringComparison.OrdinalIgnoreCase) ||
                market.Key.Contains("moneyline", StringComparison.OrdinalIgnoreCase))
            {
                var homeWon = margin > 0;
                var homeWasFavorite = closingLine < 0;

                if (margin == 0)
                    return SignalOutcome.Stable;

                if (homeWon == homeWasFavorite)
                    return SignalOutcome.Stable;

                return SignalOutcome.Reverted;
            }

            if (market.Key.Contains("draw_no_bet", StringComparison.OrdinalIgnoreCase))
            {
                if (margin == 0)
                    return SignalOutcome.Stable;

                var homeWon = margin > 0;
                var homeWasFavorite = closingLine < 0;

                return homeWon == homeWasFavorite ? SignalOutcome.Stable : SignalOutcome.Reverted;
            }

            return margin > 0 ? SignalOutcome.Extended : SignalOutcome.Reverted;
        }

        private SignalOutcome DetermineYesNoOutcome(ScoreEvent game, decimal closingLine, MarketDefinition market)
        {
            if (market.Key.Contains("btts", StringComparison.OrdinalIgnoreCase))
            {
                var scores = GetScores(game);
                if (scores is null)
                    return SignalOutcome.Stable;

                var bothScored = scores.Value.HomeScore > 0 && scores.Value.AwayScore > 0;
                var yesBet = closingLine > 0;

                if (bothScored == yesBet)
                    return SignalOutcome.Extended;

                return SignalOutcome.Reverted;
            }

            if (market.Key.Contains("overtime", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("Overtime outcome determination not implemented for {Market}", market.Key);
                return SignalOutcome.Stable;
            }

            if (market.Key.Contains("odd_even", StringComparison.OrdinalIgnoreCase))
            {
                var scores = GetScores(game);
                if (scores is null)
                    return SignalOutcome.Stable;

                var total = scores.Value.HomeScore + scores.Value.AwayScore;
                var isOdd = total % 2 == 1;

                return SignalOutcome.Stable;
            }

            return SignalOutcome.Stable;
        }

        private static SignalOutcome DetermineNamedOutcome(int margin, decimal closingLine, MarketDefinition market)
        {
            if (market.Key.Contains("3_way", StringComparison.OrdinalIgnoreCase) ||
                market.Key.Contains("3way", StringComparison.OrdinalIgnoreCase))
            {
                if (margin == 0)
                {
                    return SignalOutcome.Reverted;
                }

                var homeWon = margin > 0;
                var homeWasFavorite = closingLine < 0;

                return homeWon == homeWasFavorite ? SignalOutcome.Stable : SignalOutcome.Reverted;
            }

            if (market.Key.Contains("double_chance", StringComparison.OrdinalIgnoreCase))
            {
                return SignalOutcome.Stable;
            }

            return SignalOutcome.Stable;
        }

        #endregion

        #region Score Extraction

        private static (int HomeScore, int AwayScore)? GetScores(ScoreEvent game)
        {
            var homeScoreEntry = game.Scores?.FirstOrDefault(s => s.Name == game.HomeTeam)?.Score;
            var awayScoreEntry = game.Scores?.FirstOrDefault(s => s.Name == game.AwayTeam)?.Score;

            if (homeScoreEntry is null || awayScoreEntry is null)
                return null;

            if (!int.TryParse(homeScoreEntry, out var homeScore) ||
                !int.TryParse(awayScoreEntry, out var awayScore))
                return null;

            return (homeScore, awayScore);
        }

        private static (int HomeScore, int AwayScore)? GetPeriodScores(
            ScoreEvent game,
            GamePeriod period,
            List<GameInfo> sportGames)
        {
            // Find matching game from sport-specific data
            var matchingGame = sportGames.FirstOrDefault(g =>
                g.HomeTeam?.Equals(game.HomeTeam, StringComparison.OrdinalIgnoreCase) == true &&
                g.AwayTeam?.Equals(game.AwayTeam, StringComparison.OrdinalIgnoreCase) == true);

            if (matchingGame is null)
                return null;

            // Period scores require sport-specific data that our unified GameInfo doesn't have
            // For now, we use the full game score from our unified model
            // A future enhancement could add period scoring to ISportClient
            return period switch
            {
                GamePeriod.FullGame => (matchingGame.HomeScore ?? 0, matchingGame.AwayScore ?? 0),
                // Period-specific scores would require extending GameInfo with period data
                _ => null
            };
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

        #endregion
    }
}
