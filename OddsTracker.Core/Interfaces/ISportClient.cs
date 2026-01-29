namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Sport-agnostic interface for SportsData API operations.
    /// Each sport (NFL, NBA, MLB, NHL) implements this with their specific client.
    /// </summary>
    public interface ISportClient
    {
        /// <summary>
        /// The sport key this client handles (e.g., "americanfootball_nfl", "basketball_nba")
        /// </summary>
        string SportKey { get; }

        /// <summary>
        /// Get all teams for this sport
        /// </summary>
        Task<List<TeamInfo>> GetTeamsAsync();

        /// <summary>
        /// Get all active players for this sport
        /// </summary>
        Task<List<PlayerInfo>> GetPlayersAsync();

        /// <summary>
        /// Get all stadiums/venues for this sport
        /// </summary>
        Task<List<StadiumInfo>> GetStadiumsAsync();

        /// <summary>
        /// Get the current season identifier
        /// </summary>
        Task<int> GetCurrentSeasonAsync();

        /// <summary>
        /// Get games/scores for a season
        /// </summary>
        Task<List<GameInfo>> GetSeasonGamesAsync(int season);

        /// <summary>
        /// Get games/scores for the current season
        /// </summary>
        Task<List<GameInfo>> GetCurrentSeasonGamesAsync();
    }

    /// <summary>
    /// Unified team info across all sports
    /// </summary>
    public class TeamInfo
    {
        public int TeamId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int? StadiumId { get; set; }
        public string? Conference { get; set; }
        public string? Division { get; set; }
    }

    /// <summary>
    /// Unified player info across all sports
    /// </summary>
    public class PlayerInfo
    {
        public int PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }
        public string? Team { get; set; }
        public string? Position { get; set; }
        public string? Status { get; set; }
        public int? Jersey { get; set; }
    }

    /// <summary>
    /// Unified stadium/venue info across all sports
    /// </summary>
    public class StadiumInfo
    {
        public int StadiumId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public int? Capacity { get; set; }
        public string? Surface { get; set; }
        public bool? IsDome { get; set; }
    }

    /// <summary>
    /// Unified game info across all sports
    /// </summary>
    public class GameInfo
    {
        public int GameId { get; set; }
        public int Season { get; set; }
        public string? SeasonType { get; set; }
        public string? Status { get; set; }
        public DateTime? DateTime { get; set; }
        public string? HomeTeam { get; set; }
        public string? AwayTeam { get; set; }
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public int? StadiumId { get; set; }
        public bool IsCompleted { get; set; }
    }
}
