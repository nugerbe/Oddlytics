using Microsoft.EntityFrameworkCore;
using Sportsdata.Core.Entities;
using Sportsdata.Core.Repositories.Interfaces;
using Sportsdata.Core.Data;

namespace Sportsdata.Core.Repositories;

public class StadiumRepository(SportsdataDbContext context) : Repository<Stadium>(context), IStadiumRepository
{
    public async Task<Stadium?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    public async Task<IEnumerable<Stadium>> GetByCityAsync(string city, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(s => s.City == city)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Stadium>> GetByStateAsync(string state, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(s => s.State == state)
            .OrderBy(s => s.City)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Stadium> UpsertAsync(Stadium stadium, CancellationToken cancellationToken = default)
    {
        var existing = await DbSet.FindAsync([stadium.StadiumId], cancellationToken);
        
        if (existing == null)
        {
            await DbSet.AddAsync(stadium, cancellationToken);
            return stadium;
        }

        // Update existing
        Context.Entry(existing).CurrentValues.SetValues(stadium);
        existing.UpdatedDate = DateTime.UtcNow;
        return existing;
    }

    public async Task<int> BulkUpsertAsync(IEnumerable<Stadium> stadiums, CancellationToken cancellationToken = default)
    {
        var stadiumList = stadiums.ToList();
        var stadiumIds = stadiumList.Select(s => s.StadiumId).ToList();
        
        var existingStadiums = await DbSet
            .Where(s => stadiumIds.Contains(s.StadiumId))
            .ToDictionaryAsync(s => s.StadiumId, cancellationToken);

        var toAdd = new List<Stadium>();
        var now = DateTime.UtcNow;

        foreach (var stadium in stadiumList)
        {
            if (existingStadiums.TryGetValue(stadium.StadiumId, out var existing))
            {
                Context.Entry(existing).CurrentValues.SetValues(stadium);
                existing.UpdatedDate = now;
            }
            else
            {
                stadium.CreatedDate = now;
                stadium.UpdatedDate = now;
                toAdd.Add(stadium);
            }
        }

        if (toAdd.Count > 0)
        {
            await DbSet.AddRangeAsync(toAdd, cancellationToken);
        }

        return stadiumList.Count;
    }
}
