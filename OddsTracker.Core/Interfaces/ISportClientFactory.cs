namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Factory for resolving sport-specific clients.
    /// Loads sport aliases from OddsTracker db and can check Sportsdata db for active sports.
    /// </summary>
    public interface ISportClientFactory
    {
        /// <summary>
        /// Initialize sport mappings from the database.
        /// Call this during application startup.
        /// </summary>
        /// <param name="checkSportsdataActive">Optional callback to check if sport is active in Sportsdata db</param>
        Task InitializeAsync(Func<string, Task<bool>>? checkSportsdataActive = null);

        /// <summary>
        /// Get the client for a specific sport by its key or alias
        /// </summary>
        /// <param name="sportKey">The sport key or alias (e.g., "americanfootball_nfl", "nfl", "football")</param>
        /// <returns>The sport client, or null if not supported</returns>
        ISportClient? GetClient(string sportKey);

        /// <summary>
        /// Get all supported sport keys (canonical keys, not aliases)
        /// </summary>
        IEnumerable<string> GetSupportedSports();

        /// <summary>
        /// Check if a sport key or alias is supported
        /// </summary>
        bool IsSupported(string sportKey);

        /// <summary>
        /// Resolve a keyword/alias to the canonical sport key
        /// </summary>
        /// <param name="keyword">The keyword or alias to resolve</param>
        /// <returns>The canonical sport key, or null if not found</returns>
        string? ResolveSportKey(string keyword);
    }
}
