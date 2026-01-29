using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportsdata.Core.Data;
using Sportsdata.Core.Repositories;
using Sportsdata.Core.Repositories.Interfaces;

namespace Sportsdata.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sportsdata Core services to the DI container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">The database connection string</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSportsdataCore(
        this IServiceCollection services,
        string connectionString)
    {
        // Add DbContext
        services.AddDbContext<SportsdataDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                sqlOptions.CommandTimeout(60);
            });
        });

        // Add repositories
        services.AddSportsdataRepositories();

        return services;
    }

    /// <summary>
    /// Adds Sportsdata Core services with a DbContext factory (for Azure Functions)
    /// </summary>
    public static IServiceCollection AddSportsdataCoreWithFactory(
        this IServiceCollection services,
        string connectionString)
    {
        // Add DbContext factory for better connection management in serverless
        services.AddDbContextFactory<SportsdataDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                sqlOptions.CommandTimeout(120); // Longer timeout for sync operations
            });
        });

        // Add scoped DbContext from factory
        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<SportsdataDbContext>>().CreateDbContext());

        // Add repositories
        services.AddSportsdataRepositories();

        return services;
    }

    /// <summary>
    /// Adds only the repositories (when DbContext is configured elsewhere)
    /// </summary>
    public static IServiceCollection AddSportsdataRepositories(this IServiceCollection services)
    {
        services.AddScoped<ISportRepository, SportRepository>();
        services.AddScoped<IStadiumRepository, StadiumRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<ISportResolutionRepository, SportResolutionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
