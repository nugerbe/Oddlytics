using FantasyData.Api.Client.Model.NFLv3;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Threading.Tasks;

namespace OddsTracker.Functions.Functions
{
    /// <summary>
    /// Timer-triggered function that updates signal outcomes after games complete.
    /// Runs every 15 minutes, checks for completed games, and updates historical records.
    /// </summary>
    public class OutcomeUpdateFunction(
        IOddsApiClient oddsClient,
        ISportsDataService sportsDataService,
        IMarketRepository marketRepository,
        IHistoricalTracker historicalTracker,
        IEnhancedCacheService cache,
        ILogger<OutcomeUpdateFunction> logger)
    {
        /// <summary>
        /// Timer trigger: runs every 15 minutes
        /// CRON: "0 */15 * * * *" = at minute 0, 15, 30, 45 of every hour
        /// </summary>
        [Function("OutcomeUpdate")]
        public async Task Run(
            [TimerTrigger("0 */15 * * * *")] MyTimerInfo timerInfo)
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
            var sports = await marketRepository.GetAllSportsAsync();
            var activeSports = sports.Where(s => s.IsActive).ToList();

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

                    // Get all non-player-prop, non-alternate markets for this sport
                    var sportMarkets = await marketRepository.GetMarketsForSportAsync(sport.Key);
                    var trackableMarkets = sportMarkets
                        .Where(m => !m.IsPlayerProp && !m.IsAlternate)
                        .ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

                    foreach (var game in completedGames)
                    {
                        try
                        {
                            var gameUpdates = await ProcessCompletedGameAsync(game, trackableMarkets);
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

        private async Task<int> ProcessCompletedGameAsync(
            ScoreEvent game,
            Dictionary<string, MarketDefinition> marketsByKey)
        {
            var updatedCount = 0;

            foreach (var (marketKey, market) in marketsByKey)
            {
                var closingCacheKey = $"closingline:{game.Id}:{marketKey}";
                var closingLineWrapper = await cache.GetAsync<ClosingLineWrapper>(closingCacheKey);

                if (closingLineWrapper is null)
                    continue;

                var outcome = await DetermineOutcome(game, market, closingLineWrapper.ClosingLine);

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

        private async Task<SignalOutcome?> DetermineOutcome(
            ScoreEvent game,
            MarketDefinition market,
            decimal closingLine)
        {
            // Get scores for full game
            var scores = GetScores(game);
            if (scores is null)
                return null;

            var (homeScore, awayScore) = scores.Value;

            // Handle period-specific markets
            if (market.Period is not null)
            {
                var sportsdataScores = await sportsDataService.GetSeasonScores();
                var periodScores = GetPeriodScores(game, (GamePeriod)market.Period, sportsdataScores);
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
            // Team totals use individual team score, not combined
            if (market.Key.Contains("team_total", StringComparison.OrdinalIgnoreCase))
            {
                // For team totals, closingLine is for a specific team
                // We'd need to know which team - for now treat as regular total
            }

            if (totalScore > closingLine)
                return SignalOutcome.Extended;  // Over hit
            if (totalScore < closingLine)
                return SignalOutcome.Reverted;  // Under hit

            return SignalOutcome.Stable;  // Push
        }

        private static SignalOutcome DetermineTeamBasedOutcome(int margin, decimal closingLine, MarketDefinition market)
        {
            // Spreads - margin vs point spread
            if (market.Key.Contains("spread", StringComparison.OrdinalIgnoreCase) ||
                market.Key.Contains("handicap", StringComparison.OrdinalIgnoreCase))
            {
                var adjustedMargin = margin + closingLine;

                if (adjustedMargin > 0)
                    return SignalOutcome.Extended;  // Home covered
                if (adjustedMargin < 0)
                    return SignalOutcome.Reverted;  // Away covered

                return SignalOutcome.Stable;  // Push
            }

            // Moneylines (h2h) - winner vs favorite
            if (market.Key.Contains("h2h", StringComparison.OrdinalIgnoreCase) ||
                market.Key.Contains("moneyline", StringComparison.OrdinalIgnoreCase))
            {
                var homeWon = margin > 0;
                var homeWasFavorite = closingLine < 0;

                // Tie handling (for sports that allow ties)
                if (margin == 0)
                    return SignalOutcome.Stable;

                if (homeWon == homeWasFavorite)
                    return SignalOutcome.Stable;  // Favorite won

                return SignalOutcome.Reverted;  // Upset
            }

            // Draw no bet
            if (market.Key.Contains("draw_no_bet", StringComparison.OrdinalIgnoreCase))
            {
                if (margin == 0)
                    return SignalOutcome.Stable;  // Draw = push

                var homeWon = margin > 0;
                var homeWasFavorite = closingLine < 0;

                return homeWon == homeWasFavorite ? SignalOutcome.Stable : SignalOutcome.Reverted;
            }

            // Default team-based (treat as moneyline)
            return margin > 0 ? SignalOutcome.Extended : SignalOutcome.Reverted;
        }

        private SignalOutcome DetermineYesNoOutcome(ScoreEvent game, decimal closingLine, MarketDefinition market)
        {
            // BTTS (Both Teams to Score)
            if (market.Key.Contains("btts", StringComparison.OrdinalIgnoreCase))
            {
                var scores = GetScores(game);
                if (scores is null)
                    return SignalOutcome.Stable;

                var bothScored = scores.Value.HomeScore > 0 && scores.Value.AwayScore > 0;

                // closingLine > 0 means "Yes" was favorite
                var yesBet = closingLine > 0;

                if (bothScored == yesBet)
                    return SignalOutcome.Extended;

                return SignalOutcome.Reverted;
            }

            // Overtime
            if (market.Key.Contains("overtime", StringComparison.OrdinalIgnoreCase))
            {
                // Would need OT data from scores API
                logger.LogDebug("Overtime outcome determination not implemented for {Market}", market.Key);
                return SignalOutcome.Stable;
            }

            // Odd/Even total
            if (market.Key.Contains("odd_even", StringComparison.OrdinalIgnoreCase))
            {
                var scores = GetScores(game);
                if (scores is null)
                    return SignalOutcome.Stable;

                var total = scores.Value.HomeScore + scores.Value.AwayScore;
                var isOdd = total % 2 == 1;

                // closingLine indicates which was favored
                return SignalOutcome.Stable;  // Hard to determine without knowing bet side
            }

            return SignalOutcome.Stable;
        }

        private static SignalOutcome DetermineNamedOutcome(int margin, decimal closingLine, MarketDefinition market)
        {
            // 3-way markets (includes draw)
            if (market.Key.Contains("3_way", StringComparison.OrdinalIgnoreCase) ||
                market.Key.Contains("3way", StringComparison.OrdinalIgnoreCase))
            {
                // For 3-way, closingLine might represent home odds
                // Draw outcome needs special handling
                if (margin == 0)
                {
                    // Draw occurred - typically this means signals on home/away reverted
                    return SignalOutcome.Reverted;
                }

                var homeWon = margin > 0;
                var homeWasFavorite = closingLine < 0;

                return homeWon == homeWasFavorite ? SignalOutcome.Stable : SignalOutcome.Reverted;
            }

            // Double chance (1X, X2, 12)
            if (market.Key.Contains("double_chance", StringComparison.OrdinalIgnoreCase))
            {
                // Would need to know which double chance bet
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

        /// <summary>
        /// Gets period-specific scores from FantasyData Score object.
        /// Matches game by team names and extracts quarter/half scores based on period.
        /// </summary>
        private static (int HomeScore, int AwayScore)? GetPeriodScores(
            ScoreEvent game,
            GamePeriod period,
            List<Score> detailedScores)
        {
            // Find matching game in detailed scores
            var matchingGame = detailedScores.FirstOrDefault(s =>
                (s.HomeTeam?.Equals(game.HomeTeam, StringComparison.OrdinalIgnoreCase) == true &&
                s.AwayTeam?.Equals(game.AwayTeam, StringComparison.OrdinalIgnoreCase) == true));

            if (matchingGame is null)
                return null;

            return period switch
            {
                // Quarter scores (NFL, NBA)
                GamePeriod.FirstQuarter => GetQuarterScore(matchingGame, 1),
                GamePeriod.SecondQuarter => GetQuarterScore(matchingGame, 2),
                GamePeriod.ThirdQuarter => GetQuarterScore(matchingGame, 3),
                GamePeriod.FourthQuarter => GetQuarterScore(matchingGame, 4),

                // Half scores (sum of quarters)
                GamePeriod.FirstHalf => SumQuarterScores(matchingGame, new[] { 1, 2 }),
                GamePeriod.SecondHalf => SumQuarterScores(matchingGame, new[] { 3, 4 }),

                // Period scores (NHL) - mapped to quarters for NFL data
                GamePeriod.FirstPeriod => GetQuarterScore(matchingGame, 1),
                GamePeriod.SecondPeriod => GetQuarterScore(matchingGame, 2),
                GamePeriod.ThirdPeriod => GetQuarterScore(matchingGame, 3),

                // Overtime
                GamePeriod.Overtime => (matchingGame.HomeScoreOvertime ?? 0, matchingGame.AwayScoreOvertime ?? 0),

                _ => null
            };
        }

        private static (int HomeScore, int AwayScore)? GetQuarterScore(Score game, int quarter)
        {
            return quarter switch
            {
                1 => (game.HomeScoreQuarter1 ?? 0, game.AwayScoreQuarter1 ?? 0),
                2 => (game.HomeScoreQuarter2 ?? 0, game.AwayScoreQuarter2 ?? 0),
                3 => (game.HomeScoreQuarter3 ?? 0, game.AwayScoreQuarter3 ?? 0),
                4 => (game.HomeScoreQuarter4 ?? 0, game.AwayScoreQuarter4 ?? 0),
                _ => null
            };
        }

        private static (int HomeScore, int AwayScore)? SumQuarterScores(Score game, int[] quarters)
        {
            var homeTotal = 0;
            var awayTotal = 0;

            foreach (var quarter in quarters)
            {
                var quarterScore = GetQuarterScore(game, quarter);
                if (quarterScore is null)
                    return null;

                homeTotal += quarterScore.Value.HomeScore;
                awayTotal += quarterScore.Value.AwayScore;
            }

            return (homeTotal, awayTotal);
        }

        #endregion
    }
}