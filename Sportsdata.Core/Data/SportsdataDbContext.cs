using Microsoft.EntityFrameworkCore;
using Sportsdata.Core.Entities;
using Sportsdata.Core.Models;

namespace Sportsdata.Core.Data;

public class SportsdataDbContext(DbContextOptions<SportsdataDbContext> options) : DbContext(options)
{
    // Entity DbSets
    public DbSet<Sport> Sports => Set<Sport>();
    public DbSet<Stadium> Stadiums => Set<Stadium>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamAlias> TeamAliases => Set<TeamAlias>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerAlias> PlayerAliases => Set<PlayerAlias>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SportsdataDbContext).Assembly);

        // Configure keyless entity for stored procedure results
        modelBuilder.Entity<SportResolutionResult>().HasNoKey();
    }

    /// <summary>
    /// Resolves a search term to a sport/team/player using the stored procedure
    /// </summary>
    public async Task<List<SportResolutionResult>> ResolveSportFromEntityAsync(
        string searchTerm,
        string? entityTypeHint = null,
        string? sportHint = null,
        CancellationToken cancellationToken = default)
    {
        return await Set<SportResolutionResult>()
            .FromSqlInterpolated($@"
                EXEC [dbo].[usp_ResolveSportFromEntity] 
                    @SearchTerm = {searchTerm},
                    @EntityTypeHint = {entityTypeHint},
                    @SportHint = {sportHint}")
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Override SaveChangesAsync to automatically set UpdatedDate
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Entity is Sport sport)
                sport.UpdatedDate = DateTime.UtcNow;
            else if (entry.Entity is Stadium stadium)
                stadium.UpdatedDate = DateTime.UtcNow;
            else if (entry.Entity is Team team)
                team.UpdatedDate = DateTime.UtcNow;
            else if (entry.Entity is Player player)
                player.UpdatedDate = DateTime.UtcNow;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
