namespace Sportsdata.Core.Repositories.Interfaces;

/// <summary>
/// Unit of Work pattern interface for coordinating repository transactions
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    ISportRepository Sports { get; }
    IStadiumRepository Stadiums { get; }
    ITeamRepository Teams { get; }
    IPlayerRepository Players { get; }
    ISportResolutionRepository SportResolution { get; }

    /// <summary>
    /// Saves all pending changes to the database
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a new database transaction
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
