using FantasyData.Api.Client.Model.NFLv3;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface ISportsDataService
    {
        Task<Dictionary<string, string>> GetTeamAliasesAsync();
        Task InitializeAsync();

        /// <summary>
        /// Look up a player's current team by name.
        /// Returns the team full name (e.g., "Buffalo Bills") or null if not found.
        /// </summary>
        Task<PlayerTeamInfo?> GetPlayerTeamAsync(string playerName);
        Task<List<Score>> GetSeasonScores();
    }
}