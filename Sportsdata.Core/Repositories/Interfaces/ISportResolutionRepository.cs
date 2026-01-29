using Sportsdata.Core.Models;

namespace Sportsdata.Core.Repositories.Interfaces;

/// <summary>
/// Repository for resolving search terms to sports/teams/players
/// </summary>
public interface ISportResolutionRepository
{
    /// <summary>
    /// Resolves a search term to sport/team/player entities
    /// </summary>
    /// <param name="searchTerm">The term to search for (player name, team name, abbreviation, etc.)</param>
    /// <param name="entityTypeHint">Optional hint: "Team" or "Player"</param>
    /// <param name="sportHint">Optional sport code hint (e.g., "NFL")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of resolution results ordered by confidence</returns>
    Task<List<SportResolutionResult>> ResolveAsync(
        string searchTerm,
        string? entityTypeHint = null,
        string? sportHint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a search term and returns only the best match
    /// </summary>
    Task<SportResolutionResult?> ResolveBestMatchAsync(
        string searchTerm,
        string? entityTypeHint = null,
        string? sportHint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a search term and returns only high-confidence matches (>= 0.85)
    /// </summary>
    Task<List<SportResolutionResult>> ResolveHighConfidenceAsync(
        string searchTerm,
        string? entityTypeHint = null,
        string? sportHint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk resolves multiple terms
    /// </summary>
    Task<Dictionary<string, SportResolutionResult?>> ResolveBulkAsync(
        IEnumerable<string> searchTerms,
        string? sportHint = null,
        CancellationToken cancellationToken = default);
}
