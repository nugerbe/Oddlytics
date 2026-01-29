// ============================================================================
// OddsTracker.Core - Repositories
// File: Repositories/UnitOfWork.cs
// ============================================================================

using Microsoft.EntityFrameworkCore.Storage;
using Sportsdata.Core.Repositories.Interfaces;
using Sportsdata.Core.Data;

namespace Sportsdata.Core.Repositories;

/// <summary>
/// Unit of Work implementation for coordinating repository transactions
/// </summary>
public class UnitOfWork(SportsdataDbContext context) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    // Lazy initialization of repositories
    private ISportRepository? _sports;
    private IStadiumRepository? _stadiums;
    private ITeamRepository? _teams;
    private IPlayerRepository? _players;
    private ISportResolutionRepository? _sportResolution;

    public ISportRepository Sports => _sports ??= new SportRepository(context);
    public IStadiumRepository Stadiums => _stadiums ??= new StadiumRepository(context);
    public ITeamRepository Teams => _teams ??= new TeamRepository(context);
    public IPlayerRepository Players => _players ??= new PlayerRepository(context);
    public ISportResolutionRepository SportResolution => _sportResolution ??= new SportResolutionRepository(context);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction has been started.");
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _transaction?.Dispose();
                context.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
            }
            await context.DisposeAsync();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
