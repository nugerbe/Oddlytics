using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Sport-agnostic service for retrieving sports data (teams, players, games, etc.)
    /// </summary>
    public interface ISportsDataService
    {
        /// <summary>
        /// Initialize caches for a specific sport
        /// </summary>
        Task InitializeAsync(string sportKey);

        /// <summary>
        /// Get team aliases mapping (abbreviation -> full name) for a sport
        /// </summary>
        Task<Dictionary<string, string>> GetTeamAliasesAsync(string sportKey);

        /// <summary>
        /// Look up a player's current team by name for a specific sport
        /// </summary>
        Task<PlayerTeamInfo?> GetPlayerTeamAsync(string sportKey, string playerName);

        /// <summary>
        /// Get all games/scores for the current season of a sport
        /// </summary>
        Task<List<GameInfo>> GetSeasonGamesAsync(string sportKey);

        /// <summary>
        /// Get all stadiums for a sport
        /// </summary>
        Task<List<StadiumInfo>> GetStadiumsAsync(string sportKey);

        /// <summary>
        /// Get all teams for a sport
        /// </summary>
        Task<List<TeamInfo>> GetTeamsAsync(string sportKey);

        /// <summary>
        /// Get all players for a sport
        /// </summary>
        Task<List<PlayerInfo>> GetPlayersAsync(string sportKey);

        /// <summary>
        /// Check if a sport is supported
        /// </summary>
        bool IsSportSupported(string sportKey);

        /// <summary>
        /// Get all supported sport keys
        /// </summary>
        IEnumerable<string> GetSupportedSports();
    }
}
