using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Repositories.Interfaces;

public interface ITeamRepository : IRepository<Team>
{
    // Basic lookups
    Task<Team?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<Team?> GetByKeyAndSportAsync(string key, int sportId, CancellationToken cancellationToken = default);
    Task<Team?> GetByGlobalTeamIdAsync(int globalTeamId, CancellationToken cancellationToken = default);
    
    // Sport-based queries
    Task<IEnumerable<Team>> GetBySportAsync(int sportId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetBySportCodeAsync(string sportCode, CancellationToken cancellationToken = default);
    
    // Search
    Task<IEnumerable<Team>> SearchByNameAsync(string searchTerm, int? sportId = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetByConferenceAsync(int sportId, string conference, CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetByDivisionAsync(int sportId, string division, CancellationToken cancellationToken = default);
    
    // Include related data
    Task<Team?> GetWithPlayersAsync(int teamId, CancellationToken cancellationToken = default);
    Task<Team?> GetWithAliasesAsync(int teamId, CancellationToken cancellationToken = default);
    Task<Team?> GetWithAllRelatedAsync(int teamId, CancellationToken cancellationToken = default);
    
    // Sync operations (for Azure Function)
    Task<Team> UpsertAsync(Team team, CancellationToken cancellationToken = default);
    Task<int> BulkUpsertAsync(IEnumerable<Team> teams, CancellationToken cancellationToken = default);
    
    // Alias operations
    Task<Team?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default);
    Task AddAliasAsync(int teamId, string alias, string aliasType, CancellationToken cancellationToken = default);
}
