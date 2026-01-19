namespace OddsTracker.Core.Models
{
    #region Query Models

    /// <summary>
    /// Base class for odds queries
    /// </summary>
    public abstract class OddsQueryBase
    {
        public required MarketDefinition MarketDefinition { get; set; }
        public int DaysBack { get; set; } = 3;
        public string Sport { get; set; } = "americanfootball_nfl";

        public abstract string CacheKey { get; }
    }

    /// <summary>
    /// Query for game-level odds (spreads, totals, moneylines, etc.)
    /// </summary>
    public class GameOddsQuery : OddsQueryBase
    {
        public string HomeTeam { get; set; } = string.Empty;
        public string? AwayTeam { get; set; }

        public override string CacheKey =>
            $"odds:game:{HomeTeam.ToLowerInvariant()}:{AwayTeam?.ToLowerInvariant() ?? "any"}:{MarketDefinition.Key}:{DaysBack}";
    }

    /// <summary>
    /// Query for player prop odds
    /// </summary>
    public class PlayerOddsQuery : OddsQueryBase
    {
        public string PlayerName { get; set; } = string.Empty;
        public string? Team { get; set; }  // Optional - can be resolved from player lookup

        public override string CacheKey =>
            $"odds:player:{PlayerName.ToLowerInvariant()}:{Team?.ToLowerInvariant() ?? "any"}:{MarketDefinition.Key}:{DaysBack}";
    }

    #endregion

    #region Odds Result Models

    /// <summary>
    /// Base class for normalized odds data
    /// </summary>
    public abstract class OddsBase
    {
        public string EventId { get; set; } = string.Empty;
        public DateTime CommenceTime { get; set; }
        public required MarketDefinition MarketDefinition { get; set; }
        public List<BookSnapshotBase> Snapshots { get; set; } = [];

        /// <summary>
        /// Display-friendly description of the odds
        /// </summary>
        public abstract string Description { get; }
    }

    /// <summary>
    /// Game-level odds (spreads, totals, moneylines, halves, quarters, etc.)
    /// </summary>
    public class GameOdds : OddsBase
    {
        public string HomeTeam { get; set; } = string.Empty;
        public string AwayTeam { get; set; } = string.Empty;

        public override string Description =>
            $"{AwayTeam} @ {HomeTeam} - {MarketDefinition.DisplayName}";

        // Legacy compatibility
        public string GameId => EventId;
    }

    /// <summary>
    /// Player prop odds
    /// </summary>
    public class PlayerOdds : OddsBase
    {
        public string PlayerName { get; set; } = string.Empty;
        public string Team { get; set; } = string.Empty;
        public string? Opponent { get; set; }
        public string? Position { get; set; }

        public override string Description =>
            $"{PlayerName} ({Team}) - {MarketDefinition.DisplayName}";
    }

    #endregion

    #region Book Snapshots

    /// <summary>
    /// Base class for a point-in-time odds snapshot from a bookmaker
    /// </summary>
    public abstract class BookSnapshotBase
    {
        public string BookmakerName { get; set; } = string.Empty;
        public string BookmakerKey { get; set; } = string.Empty;
        public BookmakerTier BookType { get; set; }
        public decimal Line { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Snapshot for game-level odds (spreads, totals, moneylines)
    /// </summary>
    public class GameBookSnapshot : BookSnapshotBase
    {
        /// <summary>
        /// Home team odds (American format, e.g., -110)
        /// </summary>
        public decimal? HomeOdds { get; set; }

        /// <summary>
        /// Away team odds (American format, e.g., -110)
        /// </summary>
        public decimal? AwayOdds { get; set; }
    }

    /// <summary>
    /// Snapshot for player prop odds
    /// </summary>
    public class PlayerBookSnapshot : BookSnapshotBase
    {
        /// <summary>
        /// Player name (e.g., "Patrick Mahomes")
        /// </summary>
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>
        /// Over odds (American format)
        /// </summary>
        public decimal? OverOdds { get; set; }

        /// <summary>
        /// Under odds (American format)
        /// </summary>
        public decimal? UnderOdds { get; set; }

        /// <summary>
        /// The prop category (e.g., "Passing Yards", "Rushing TDs")
        /// </summary>
        public string PropType { get; set; } = string.Empty;
    }

    #endregion

    #region Supporting Types

    /// <summary>
    /// Available markets for an event
    /// </summary>
    public class EventMarkets
    {
        public string EventId { get; set; } = string.Empty;
        public string HomeTeam { get; set; } = string.Empty;
        public string AwayTeam { get; set; } = string.Empty;
        public DateTime CommenceTime { get; set; }
        public List<AvailableMarket> Markets { get; set; } = [];
    }

    public class AvailableMarket
    {
        public string BookmakerKey { get; set; } = string.Empty;
        public string BookmakerName { get; set; } = string.Empty;
        public string MarketKey { get; set; } = string.Empty;
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// Data point for chart generation
    /// </summary>
    public record ChartDataPoint(
        string Bookmaker,
        DateTime Timestamp,
        decimal Value
    );

    public record ChartData(
        string Title,
        string XAxisLabel,
        string YAxisLabel,
        List<ChartSeries> Series
    );

    public record ChartSeries(
        string Name,
        List<ChartDataPoint> Points
    );

    #endregion
}