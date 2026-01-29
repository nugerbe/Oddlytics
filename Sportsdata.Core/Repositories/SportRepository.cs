using Microsoft.EntityFrameworkCore;
using Sportsdata.Core.Entities;
using Sportsdata.Core.Repositories.Interfaces;
using Sportsdata.Core.Data;

namespace Sportsdata.Core.Repositories;

public class SportRepository(SportsdataDbContext context) : Repository<Sport>(context), ISportRepository
{
    public async Task<Sport?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
    }

    public async Task<IEnumerable<Sport>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<int?> GetSportIdByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(s => s.Code == code)
            .Select(s => (int?)s.SportId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
