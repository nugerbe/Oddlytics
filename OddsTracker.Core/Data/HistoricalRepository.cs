using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Data
{
    /// <summary>
    /// EF Core implementation of IHistoricalRepository using SQL Server.
    /// </summary>
    public class HistoricalRepository(OddsTrackerDbContext db, ILogger<HistoricalRepository> logger) : IHistoricalRepository
    {
        public async Task SaveSignalAsync(SignalSnapshot signal)
        {
            var entity = SignalSnapshotEntity.FromModel(signal);
            db.SignalSnapshots.Add(entity);
            await db.SaveChangesAsync();

            signal.Id = entity.Id;
            logger.LogDebug("Saved signal snapshot {Id} for event {EventId}", entity.Id, signal.EventId);
        }

        public async Task UpdateSignalAsync(SignalSnapshot signal)
        {
            var entity = await db.SignalSnapshots.FindAsync(signal.Id);

            if (entity is null)
            {
                logger.LogWarning("Signal snapshot {Id} not found for update", signal.Id);
                return;
            }

            entity.ClosingLine = signal.ClosingLine;
            entity.Outcome = signal.Outcome;

            await db.SaveChangesAsync();
            logger.LogDebug("Updated signal snapshot {Id} with outcome {Outcome}", signal.Id, signal.Outcome);
        }

        public async Task<List<SignalSnapshot>> GetSignalsForEventAsync(string eventId, MarketType marketType)
        {
            var entities = await db.SignalSnapshots
                .AsNoTracking()
                .Where(s => s.EventId == eventId && s.MarketType == marketType)
                .ToListAsync();

            return [.. entities.Select(e => e.ToModel())];
        }

        public async Task<List<SignalSnapshot>> GetSignalsInRangeAsync(DateTime from, DateTime to)
        {
            var entities = await db.SignalSnapshots
                .AsNoTracking()
                .Where(s => s.SignalTime >= from && s.SignalTime <= to)
                .OrderByDescending(s => s.SignalTime)
                .ToListAsync();

            return [.. entities.Select(e => e.ToModel())];
        }

        public async Task<List<SignalSnapshot>> GetPendingOutcomeSignalsAsync(DateTime gameTimeBefore)
        {
            var entities = await db.SignalSnapshots
                .AsNoTracking()
                .Where(s => s.Outcome == null && s.GameTime < gameTimeBefore)
                .OrderBy(s => s.GameTime)
                .ToListAsync();

            return [.. entities.Select(e => e.ToModel())];
        }
    }
}