using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Repositories.Interfaces;

public interface ISportRepository : IRepository<Sport>
{
    Task<Sport?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IEnumerable<Sport>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<int?> GetSportIdByCodeAsync(string code, CancellationToken cancellationToken = default);
}
