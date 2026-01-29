using Microsoft.EntityFrameworkCore;
using Sportsdata.Core.Entities;
using Sportsdata.Core.Repositories.Interfaces;
using Sportsdata.Core.Data;

namespace Sportsdata.Core.Repositories;

public class PlayerRepository(SportsdataDbContext context) : Repository<Player>(context), IPlayerRepository
{
    #region Basic Lookups

    public async Task<Player?> GetByGlobalPlayerIdAsync(int globalPlayerId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Sport)
            .Include(p => p.TeamNavigation)
            .FirstOrDefaultAsync(p => p.GlobalPlayerId == globalPlayerId, cancellationToken);
    }

    #endregion

    #region Sport-based Queries

    public async Task<IEnumerable<Player>> GetBySportAsync(
        int sportId, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(p => p.SportId == sportId);
        
        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        return await query
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Player>> GetBySportCodeAsync(
        string sportCode, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(p => p.Sport)
            .Where(p => p.Sport.Code == sportCode);
        
        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        return await query
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Team-based Queries

    public async Task<IEnumerable<Player>> GetByTeamIdAsync(
        int teamId, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(p => p.TeamId == teamId);
        
        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        return await query
            .OrderBy(p => p.Position)
            .ThenBy(p => p.DepthOrder)
            .ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Player>> GetByTeamKeyAsync(
        string teamKey, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(p => p.Team == teamKey);
        
        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        return await query
            .OrderBy(p => p.Position)
            .ThenBy(p => p.DepthOrder)
            .ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Position-based Queries

    public async Task<IEnumerable<Player>> GetByPositionAsync(
        int sportId, 
        string position, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(p => p.SportId == sportId && p.Position == position);
        
        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        return await query
            .Include(p => p.TeamNavigation)
            .OrderBy(p => p.AverageDraftPosition ?? 999)
            .ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Player>> GetByFantasyPositionAsync(
        int sportId, 
        string fantasyPosition, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(p => p.SportId == sportId && p.FantasyPosition == fantasyPosition);
        
        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        return await query
            .Include(p => p.TeamNavigation)
            .OrderBy(p => p.AverageDraftPosition ?? 999)
            .ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Search

    public async Task<IEnumerable<Player>> SearchByNameAsync(
        string searchTerm, 
        int? sportId = null, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Include(p => p.Sport).Include(p => p.TeamNavigation).AsQueryable();

        if (sportId.HasValue)
        {
            query = query.Where(p => p.SportId == sportId.Value);
        }

        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        var upperSearch = searchTerm.ToUpperInvariant();
        
        return await query
            .Where(p => 
                (p.Name != null && p.Name.ToUpper().Contains(upperSearch)) ||
                (p.LastName != null && p.LastName.ToUpper().Contains(upperSearch)) ||
                (p.FirstName != null && p.FirstName.ToUpper().Contains(upperSearch)) ||
                (p.ShortName != null && p.ShortName.ToUpper().Contains(upperSearch)))
            .OrderBy(p => p.LastName == searchTerm ? 0 : 1)
            .ThenBy(p => p.AverageDraftPosition ?? 999)
            .ThenBy(p => p.LastName)
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Player>> SearchByLastNameAsync(
        string lastName, 
        int? sportId = null, 
        bool activeOnly = true, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Include(p => p.Sport).Include(p => p.TeamNavigation).AsQueryable();

        if (sportId.HasValue)
        {
            query = query.Where(p => p.SportId == sportId.Value);
        }

        if (activeOnly)
        {
            query = query.Where(p => p.Active == true);
        }

        return await query
            .Where(p => p.LastName != null && p.LastName.ToUpper() == lastName.ToUpperInvariant())
            .OrderBy(p => p.AverageDraftPosition ?? 999)
            .ThenBy(p => p.FirstName)
            .ToListAsync(cancellationToken);
    }

    public async Task<Player?> GetByFullNameAsync(
        string firstName, 
        string lastName, 
        int? sportId = null, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Include(p => p.Sport).Include(p => p.TeamNavigation).AsQueryable();

        if (sportId.HasValue)
        {
            query = query.Where(p => p.SportId == sportId.Value);
        }

        return await query
            .Where(p => 
                p.FirstName != null && p.FirstName.ToUpper() == firstName.ToUpperInvariant() &&
                p.LastName != null && p.LastName.ToUpper() == lastName.ToUpperInvariant())
            .OrderByDescending(p => p.Active)
            .ThenBy(p => p.AverageDraftPosition ?? 999)
            .FirstOrDefaultAsync(cancellationToken);
    }

    #endregion

    #region Include Related Data

    public async Task<Player?> GetWithTeamAsync(int playerId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Sport)
            .Include(p => p.TeamNavigation)
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);
    }

    public async Task<Player?> GetWithAliasesAsync(int playerId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Sport)
            .Include(p => p.Aliases)
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);
    }

    public async Task<Player?> GetWithAllRelatedAsync(int playerId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Sport)
            .Include(p => p.TeamNavigation)
            .Include(p => p.Aliases)
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);
    }

    #endregion

    #region Injury Queries

    public async Task<IEnumerable<Player>> GetInjuredPlayersAsync(
        int sportId, 
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.TeamNavigation)
            .Where(p => 
                p.SportId == sportId && 
                p.Active == true && 
                p.InjuryStatus != null && 
                p.InjuryStatus != "")
            .OrderBy(p => p.InjuryStatus)
            .ThenBy(p => p.Team)
            .ThenBy(p => p.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Player>> GetByInjuryStatusAsync(
        int sportId, 
        string injuryStatus, 
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.TeamNavigation)
            .Where(p => 
                p.SportId == sportId && 
                p.Active == true && 
                p.InjuryStatus == injuryStatus)
            .OrderBy(p => p.Team)
            .ThenBy(p => p.Position)
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Sync Operations

    public async Task<Player> UpsertAsync(Player player, CancellationToken cancellationToken = default)
    {
        var existing = await DbSet.FindAsync([player.PlayerId], cancellationToken);
        
        if (existing == null)
        {
            player.CreatedDate = DateTime.UtcNow;
            player.UpdatedDate = DateTime.UtcNow;
            await DbSet.AddAsync(player, cancellationToken);
            return player;
        }

        var createdDate = existing.CreatedDate;
        Context.Entry(existing).CurrentValues.SetValues(player);
        existing.CreatedDate = createdDate;
        existing.UpdatedDate = DateTime.UtcNow;
        return existing;
    }

    public async Task<int> BulkUpsertAsync(IEnumerable<Player> players, CancellationToken cancellationToken = default)
    {
        var playerList = players.ToList();
        var playerIds = playerList.Select(p => p.PlayerId).ToList();
        
        var existingPlayers = new Dictionary<int, Player>();
        foreach (var batch in playerIds.Chunk(2000))
        {
            var batchResults = await DbSet
                .Where(p => batch.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId, cancellationToken);
            
            foreach (var kvp in batchResults)
            {
                existingPlayers[kvp.Key] = kvp.Value;
            }
        }

        var toAdd = new List<Player>();
        var now = DateTime.UtcNow;

        foreach (var player in playerList)
        {
            if (existingPlayers.TryGetValue(player.PlayerId, out var existing))
            {
                var createdDate = existing.CreatedDate;
                Context.Entry(existing).CurrentValues.SetValues(player);
                existing.CreatedDate = createdDate;
                existing.UpdatedDate = now;
            }
            else
            {
                player.CreatedDate = now;
                player.UpdatedDate = now;
                toAdd.Add(player);
            }
        }

        if (toAdd.Count > 0)
        {
            await DbSet.AddRangeAsync(toAdd, cancellationToken);
        }

        return playerList.Count;
    }

    public async Task<int> DeactivatePlayersNotInListAsync(
        int sportId, 
        IEnumerable<int> activePlayerIds, 
        CancellationToken cancellationToken = default)
    {
        var activeIdSet = activePlayerIds.ToHashSet();
        
        var playersToDeactivate = await DbSet
            .Where(p => p.SportId == sportId && p.Active == true && !activeIdSet.Contains(p.PlayerId))
            .ToListAsync(cancellationToken);

        foreach (var player in playersToDeactivate)
        {
            player.Active = false;
            player.UpdatedDate = DateTime.UtcNow;
        }

        return playersToDeactivate.Count;
    }

    #endregion

    #region Alias Operations

    public async Task<Player?> GetByAliasAsync(
        string alias, 
        int? sportId = null, 
        CancellationToken cancellationToken = default)
    {
        var query = Context.PlayerAliases
            .Include(pa => pa.Player)
                .ThenInclude(p => p.Sport)
            .Include(pa => pa.Player)
                .ThenInclude(p => p.TeamNavigation)
            .Where(pa => pa.Alias.ToUpper() == alias.ToUpperInvariant());

        if (sportId.HasValue)
        {
            query = query.Where(pa => pa.Player.SportId == sportId.Value);
        }

        return await query
            .Select(pa => pa.Player)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAliasAsync(
        int playerId, 
        string alias, 
        string aliasType, 
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.PlayerAliases
            .AnyAsync(pa => pa.Alias.ToUpper() == alias.ToUpperInvariant(), cancellationToken);

        if (!exists)
        {
            await Context.PlayerAliases.AddAsync(new PlayerAlias
            {
                PlayerId = playerId,
                Alias = alias,
                AliasType = aliasType,
                CreatedDate = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    #endregion
}
