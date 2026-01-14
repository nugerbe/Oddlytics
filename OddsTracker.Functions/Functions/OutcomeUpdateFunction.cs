using Microsoft.Azure.Functions.Worker;
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
                throw; // Re-throw to mark function as failed
            }
        }

        private async Task UpdateOutcomesAsync()
        {
            // Get recently completed games (last 24 hours)
            var scores = await oddsClient.GetNflScoresAsync(daysFrom: 1);

            var completedGames = scores
                .Where(s => s.Completed == true)
                .ToList();

            if (completedGames.Count == 0)
            {
                logger.LogDebug("No completed games to process");
                return;
            }

            logger.LogInformation("Processing {Count} completed games", completedGames.Count);

            var processed = 0;
            var updated = 0;

            foreach (var game in completedGames)
            {
                try
                {
                    var gameUpdates = await ProcessCompletedGameAsync(game);
                    processed++;
                    updated += gameUpdates;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error processing game {EventId}", game.Id);
                }
            }

            logger.LogInformation(
                "OutcomeUpdate completed: {Processed} games processed, {Updated} outcomes updated",
                processed, updated);
        }

        private async Task<int> ProcessCompletedGameAsync(ScoreEvent game)
        {
            var updatedCount = 0;

            foreach (var marketType in new[] { MarketType.Spread, MarketType.Total, MarketType.Moneyline })
            {
                var closingKey = $"closingline:{game.Id}:{marketType}";
                var closingLineWrapper = await cache.GetAsync<ClosingLineWrapper>(closingKey);

                if (closingLineWrapper is null)
                {
                    continue;
                }

                // Determine outcome based on actual result vs closing line
                var outcome = DetermineOutcome(game, marketType, closingLineWrapper.ClosingLine);

                if (outcome.HasValue)
                {
                    await historicalTracker.UpdateOutcomeAsync(
                        game.Id ?? string.Empty,
                        marketType,
                        closingLineWrapper.ClosingLine,
                        outcome.Value);

                    logger.LogInformation(
                        "Updated outcome for {Game} {Market}: {Outcome}",
                        game.Id,
                        marketType,
                        outcome.Value);

                    // Remove the closing line from cache (processed)
                    await cache.RemoveAsync(closingKey);
                    updatedCount++;
                }
            }

            return updatedCount;
        }

        private static SignalOutcome? DetermineOutcome(ScoreEvent game, MarketType marketType, decimal closingLine)
        {
            var homeScoreEntry = game.Scores?.FirstOrDefault(s => s.Name == game.HomeTeam)?.Score;
            var awayScoreEntry = game.Scores?.FirstOrDefault(s => s.Name == game.AwayTeam)?.Score;

            if (homeScoreEntry is null || awayScoreEntry is null)
                return null;

            if (!int.TryParse(homeScoreEntry, out var homeScore) || !int.TryParse(awayScoreEntry, out var awayScore))
                return null;

            var totalScore = homeScore + awayScore;
            var margin = homeScore - awayScore;

            return marketType switch
            {
                MarketType.Spread => DetermineSpreadOutcome(margin, closingLine),
                MarketType.Total => DetermineTotalOutcome(totalScore, closingLine),
                MarketType.Moneyline => DetermineMoneylineOutcome(margin, closingLine),
                _ => null
            };
        }

        private static SignalOutcome DetermineSpreadOutcome(int margin, decimal closingLine)
        {
            // closingLine is home team spread (e.g., -3.5 means home favored by 3.5)
            var homeCovered = margin > (double)(-closingLine);
            return homeCovered ? SignalOutcome.Extended : SignalOutcome.Reverted;
        }

        private static SignalOutcome DetermineTotalOutcome(int totalScore, decimal closingLine)
        {
            var wentOver = totalScore > (double)closingLine;
            return wentOver ? SignalOutcome.Extended : SignalOutcome.Reverted;
        }

        private static SignalOutcome DetermineMoneylineOutcome(int margin, decimal closingLine)
        {
            var homeWon = margin > 0;
            var homeWasFavorite = closingLine < 0;

            if (homeWon && homeWasFavorite)
                return SignalOutcome.Stable;
            if (!homeWon && !homeWasFavorite)
                return SignalOutcome.Stable;

            return SignalOutcome.Reverted;
        }
    }
}