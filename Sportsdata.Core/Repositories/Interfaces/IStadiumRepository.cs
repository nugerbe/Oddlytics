using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Repositories.Interfaces;

public interface IStadiumRepository : IRepository<Stadium>
{
    Task<Stadium?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<Stadium>> GetByCityAsync(string city, CancellationToken cancellationToken = default);
    Task<IEnumerable<Stadium>> GetByStateAsync(string state, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Upserts a stadium - updates if exists, inserts if not
    /// </summary>
    Task<Stadium> UpsertAsync(Stadium stadium, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Bulk upsert stadiums from API
    /// </summary>
    Task<int> BulkUpsertAsync(IEnumerable<Stadium> stadiums, CancellationToken cancellationToken = default);
}
