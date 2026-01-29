using Microsoft.EntityFrameworkCore;
using Sportsdata.Core.Entities;
using Sportsdata.Core.Repositories.Interfaces;
using Sportsdata.Core.Data;

namespace Sportsdata.Core.Repositories;

public class TeamRepository(SportsdataDbContext context) : Repository<Team>(context), ITeamRepository
{
    #region Basic Lookups

    public async Task<Team?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(t => t.Sport)
            .FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
    }

    public async Task<Team?> GetByKeyAndSportAsync(string key, int sportId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(t => t.Sport)
            .FirstOrDefaultAsync(t => t.Key == key && t.SportId == sportId, cancellationToken);
    }

    public async Task<Team?> GetByGlobalTeamIdAsync(int globalTeamId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(t => t.Sport)
            .FirstOrDefaultAsync(t => t.GlobalTeamId == globalTeamId, cancellationToken);
    }

    #endregion

    #region Sport-based Queries

    public async Task<IEnumerable<Team>> GetBySportAsync(int sportId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(t => t.SportId == sportId)
            .OrderBy(t => t.City)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Team>> GetBySportCodeAsync(string sportCode, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(t => t.Sport)
            .Where(t => t.Sport.Code == sportCode)
            .OrderBy(t => t.City)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Search

    public async Task<IEnumerable<Team>> SearchByNameAsync(
        string searchTerm, 
        int? sportId = null, 
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Include(t => t.Sport).AsQueryable();

        if (sportId.HasValue)
        {
            query = query.Where(t => t.SportId == sportId.Value);
        }

        var upperSearch = searchTerm.ToUpperInvariant();
        
        return await query
            .Where(t => 
                t.Key.Contains(upperSearch, StringComparison.CurrentCultureIgnoreCase) ||
                t.Name.Contains(upperSearch, StringComparison.CurrentCultureIgnoreCase) ||
                t.City.Contains(upperSearch, StringComparison.CurrentCultureIgnoreCase) ||
                (t.FullName != null && t.FullName.Contains(upperSearch, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(t => t.Key == searchTerm ? 0 : 1) // Exact matches first
            .ThenBy(t => t.FullName)
            .Take(10)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Team>> GetByConferenceAsync(
        int sportId, 
        string conference, 
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(t => t.SportId == sportId && t.Conference == conference)
            .OrderBy(t => t.Division)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Team>> GetByDivisionAsync(
        int sportId, 
        string division, 
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(t => t.SportId == sportId && t.Division == division)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Include Related Data

    public async Task<Team?> GetWithPlayersAsync(int teamId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(t => t.Sport)
            .Include(t => t.Players.Where(p => p.Active == true))
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);
    }

    public async Task<Team?> GetWithAliasesAsync(int teamId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(t => t.Sport)
            .Include(t => t.Aliases)
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);
    }

    public async Task<Team?> GetWithAllRelatedAsync(int teamId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(t => t.Sport)
            .Include(t => t.Stadium)
            .Include(t => t.Aliases)
            .Include(t => t.Players.Where(p => p.Active == true))
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);
    }

    #endregion

    #region Sync Operations

    public async Task<Team> UpsertAsync(Team team, CancellationToken cancellationToken = default)
    {
        var existing = await DbSet.FindAsync([team.TeamId], cancellationToken);
        
        if (existing == null)
        {
            team.CreatedDate = DateTime.UtcNow;
            team.UpdatedDate = DateTime.UtcNow;
            await DbSet.AddAsync(team, cancellationToken);
            return team;
        }

        // Preserve created date, update the rest
        var createdDate = existing.CreatedDate;
        Context.Entry(existing).CurrentValues.SetValues(team);
        existing.CreatedDate = createdDate;
        existing.UpdatedDate = DateTime.UtcNow;
        return existing;
    }

    public async Task<int> BulkUpsertAsync(IEnumerable<Team> teams, CancellationToken cancellationToken = default)
    {
        var teamList = teams.ToList();
        var teamIds = teamList.Select(t => t.TeamId).ToList();
        
        var existingTeams = await DbSet
            .Where(t => teamIds.Contains(t.TeamId))
            .ToDictionaryAsync(t => t.TeamId, cancellationToken);

        var toAdd = new List<Team>();
        var now = DateTime.UtcNow;

        foreach (var team in teamList)
        {
            if (existingTeams.TryGetValue(team.TeamId, out var existing))
            {
                var createdDate = existing.CreatedDate;
                Context.Entry(existing).CurrentValues.SetValues(team);
                existing.CreatedDate = createdDate;
                existing.UpdatedDate = now;
            }
            else
            {
                team.CreatedDate = now;
                team.UpdatedDate = now;
                toAdd.Add(team);
            }
        }

        if (toAdd.Count > 0)
        {
            await DbSet.AddRangeAsync(toAdd, cancellationToken);
        }

        return teamList.Count;
    }

    #endregion

    #region Alias Operations

    public async Task<Team?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        return await Context.TeamAliases
            .Include(ta => ta.Team)
                .ThenInclude(t => t.Sport)
            .Where(ta => ta.Alias.Equals(alias, StringComparison.CurrentCultureIgnoreCase))
            .Select(ta => ta.Team)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAliasAsync(
        int teamId, 
        string alias, 
        string aliasType, 
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.TeamAliases
            .AnyAsync(ta => ta.Alias.Equals(alias, StringComparison.CurrentCultureIgnoreCase), cancellationToken);

        if (!exists)
        {
            await Context.TeamAliases.AddAsync(new TeamAlias
            {
                TeamId = teamId,
                Alias = alias,
                AliasType = aliasType,
                CreatedDate = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    #endregion
}
