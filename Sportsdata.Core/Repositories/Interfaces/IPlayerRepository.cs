// ============================================================================
// OddsTracker.Core - Repositories
// File: Repositories/Interfaces/IPlayerRepository.cs
// ============================================================================

using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Repositories.Interfaces;

public interface IPlayerRepository : IRepository<Player>
{
    // Basic lookups
    Task<Player?> GetByGlobalPlayerIdAsync(int globalPlayerId, CancellationToken cancellationToken = default);
    
    // Sport-based queries
    Task<IEnumerable<Player>> GetBySportAsync(int sportId, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<IEnumerable<Player>> GetBySportCodeAsync(string sportCode, bool activeOnly = true, CancellationToken cancellationToken = default);
    
    // Team-based queries
    Task<IEnumerable<Player>> GetByTeamIdAsync(int teamId, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<IEnumerable<Player>> GetByTeamKeyAsync(string teamKey, bool activeOnly = true, CancellationToken cancellationToken = default);
    
    // Position-based queries
    Task<IEnumerable<Player>> GetByPositionAsync(int sportId, string position, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<IEnumerable<Player>> GetByFantasyPositionAsync(int sportId, string fantasyPosition, bool activeOnly = true, CancellationToken cancellationToken = default);
    
    // Search
    Task<IEnumerable<Player>> SearchByNameAsync(string searchTerm, int? sportId = null, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<IEnumerable<Player>> SearchByLastNameAsync(string lastName, int? sportId = null, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<Player?> GetByFullNameAsync(string firstName, string lastName, int? sportId = null, CancellationToken cancellationToken = default);
    
    // Include related data
    Task<Player?> GetWithTeamAsync(int playerId, CancellationToken cancellationToken = default);
    Task<Player?> GetWithAliasesAsync(int playerId, CancellationToken cancellationToken = default);
    Task<Player?> GetWithAllRelatedAsync(int playerId, CancellationToken cancellationToken = default);
    
    // Injury queries
    Task<IEnumerable<Player>> GetInjuredPlayersAsync(int sportId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Player>> GetByInjuryStatusAsync(int sportId, string injuryStatus, CancellationToken cancellationToken = default);
    
    // Sync operations (for Azure Function)
    Task<Player> UpsertAsync(Player player, CancellationToken cancellationToken = default);
    Task<int> BulkUpsertAsync(IEnumerable<Player> players, CancellationToken cancellationToken = default);
    
    // Alias operations
    Task<Player?> GetByAliasAsync(string alias, int? sportId = null, CancellationToken cancellationToken = default);
    Task AddAliasAsync(int playerId, string alias, string aliasType, CancellationToken cancellationToken = default);
    
    // Deactivation (for players no longer in API)
    Task<int> DeactivatePlayersNotInListAsync(int sportId, IEnumerable<int> activePlayerIds, CancellationToken cancellationToken = default);
}
