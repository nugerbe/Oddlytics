using Microsoft.EntityFrameworkCore;
using Sportsdata.Core.Models;
using Sportsdata.Core.Repositories.Interfaces;
using Sportsdata.Core.Data;

namespace Sportsdata.Core.Repositories;

/// <summary>
/// Repository for resolving search terms to sports/teams/players
/// Uses stored procedure for complex resolution logic
/// </summary>
public class SportResolutionRepository(SportsdataDbContext context) : ISportResolutionRepository
{
    public async Task<List<SportResolutionResult>> ResolveAsync(
        string searchTerm,
        string? entityTypeHint = null,
        string? sportHint = null,
        CancellationToken cancellationToken = default)
    {
        return await context.ResolveSportFromEntityAsync(
            searchTerm, 
            entityTypeHint, 
            sportHint, 
            cancellationToken);
    }

    public async Task<SportResolutionResult?> ResolveBestMatchAsync(
        string searchTerm,
        string? entityTypeHint = null,
        string? sportHint = null,
        CancellationToken cancellationToken = default)
    {
        var results = await ResolveAsync(searchTerm, entityTypeHint, sportHint, cancellationToken);
        return results.FirstOrDefault();
    }

    public async Task<List<SportResolutionResult>> ResolveHighConfidenceAsync(
        string searchTerm,
        string? entityTypeHint = null,
        string? sportHint = null,
        CancellationToken cancellationToken = default)
    {
        var results = await ResolveAsync(searchTerm, entityTypeHint, sportHint, cancellationToken);
        return [.. results.Where(r => r.IsHighConfidence)];
    }

    public async Task<Dictionary<string, SportResolutionResult?>> ResolveBulkAsync(
        IEnumerable<string> searchTerms,
        string? sportHint = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, SportResolutionResult?>();
        
        foreach (var term in searchTerms.Distinct())
        {
            var resolution = await ResolveBestMatchAsync(term, null, sportHint, cancellationToken);
            results[term] = resolution;
        }

        return results;
    }
}
