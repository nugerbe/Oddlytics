namespace OddsTracker.Core.Models
{
    /// <summary>
    /// Parsed user intent from natural language query
    /// </summary>
    public class OddsQuery
    {
        public string HomeTeam { get; set; } = string.Empty;
        public string? AwayTeam { get; set; }
        public MarketType MarketType { get; set; } = MarketType.Spread;
        public int DaysBack { get; set; } = 3;
        public string? Sport { get; set; } = "americanfootball_nfl";
        public string? PlayerName { get; set; }  // For player props

        public string CacheKey => $"odds:{HomeTeam.ToLowerInvariant()}:{AwayTeam?.ToLowerInvariant() ?? "any"}:{MarketType}:{PlayerName?.ToLowerInvariant() ?? "game"}:{DaysBack}";
    }

    /// <summary>
    /// Market types supported by the system.
    /// Mapped to The Odds API market keys.
    /// </summary>
    public enum MarketType
    {
        // === GAME LINES (Starter tier) ===
        Spread,             // spreads
        Total,              // totals
        Moneyline,          // h2h

        // === HALF/QUARTER LINES (Core tier) ===
        SpreadH1,           // spreads_h1
        SpreadH2,           // spreads_h2
        SpreadQ2,           // spreads_q2
        SpreadQ3,           // spreads_q3
        SpreadQ4,           // spreads_q4
        TotalH1,            // totals_h1
        TotalH2,            // totals_h2
        TotalQ2,            // totals_q2
        TotalQ3,            // totals_q3
        TotalQ4,            // totals_q4
        MoneylineH1,        // h2h_h1
        MoneylineH2,        // h2h_h2
        MoneylineQ2,        // h2h_q2
        MoneylineQ3,        // h2h_q3
        MoneylineQ4,        // h2h_q4

        // === ALTERNATES (Core tier) ===
        AlternateSpread,    // alternate_spreads
        AlternateTotal,     // alternate_totals
        AlternateTeamTotal, // alternate_team_totals

        // === PLAYER PROPS (Sharp tier) ===
        PlayerAnytimeTD,        // player_anytime_td
        PlayerFirstTD,          // player_1st_td
        PlayerLastTD,           // player_last_td
        PlayerPassYards,        // player_pass_yds
        PlayerPassTDs,          // player_pass_tds
        PlayerPassAttempts,     // player_pass_attempts
        PlayerPassCompletions,  // player_pass_completions
        PlayerPassInterceptions,// player_pass_interceptions
        PlayerRushYards,        // player_rush_yds
        PlayerRushAttempts,     // player_rush_attempts
        PlayerReceptions,       // player_receptions
        PlayerReceivingYards,   // player_reception_yds
        PlayerRushReceivingYards, // player_rush_reception_yds
        PlayerTotalTDs,         // player_tds_over
        PlayerSacks,            // player_sacks
        PlayerTacklesAssists,   // player_tackles_assists

        // === OTHER (Core tier) ===
        TeamTotal,          // team_totals
        OddEven,            // odd_even
        Overtime,           // overtime
        ThreeWayH1,         // h2h_3_way_h1
    }

    /// <summary>
    /// Helper class for market type operations
    /// </summary>
    public static class MarketTypeExtensions
    {
        /// <summary>
        /// Get the API key for a market type
        /// </summary>
        public static string ToApiKey(this MarketType marketType) => marketType switch
        {
            // Game lines
            MarketType.Spread => "spreads",
            MarketType.Total => "totals",
            MarketType.Moneyline => "h2h",

            // Half/Quarter
            MarketType.SpreadH1 => "spreads_h1",
            MarketType.SpreadH2 => "spreads_h2",
            MarketType.SpreadQ2 => "spreads_q2",
            MarketType.SpreadQ3 => "spreads_q3",
            MarketType.SpreadQ4 => "spreads_q4",
            MarketType.TotalH1 => "totals_h1",
            MarketType.TotalH2 => "totals_h2",
            MarketType.TotalQ2 => "totals_q2",
            MarketType.TotalQ3 => "totals_q3",
            MarketType.TotalQ4 => "totals_q4",
            MarketType.MoneylineH1 => "h2h_h1",
            MarketType.MoneylineH2 => "h2h_h2",
            MarketType.MoneylineQ2 => "h2h_q2",
            MarketType.MoneylineQ3 => "h2h_q3",
            MarketType.MoneylineQ4 => "h2h_q4",

            // Alternates
            MarketType.AlternateSpread => "alternate_spreads",
            MarketType.AlternateTotal => "alternate_totals",
            MarketType.AlternateTeamTotal => "alternate_team_totals",

            // Player props
            MarketType.PlayerAnytimeTD => "player_anytime_td",
            MarketType.PlayerFirstTD => "player_1st_td",
            MarketType.PlayerLastTD => "player_last_td",
            MarketType.PlayerPassYards => "player_pass_yds",
            MarketType.PlayerPassTDs => "player_pass_tds",
            MarketType.PlayerPassAttempts => "player_pass_attempts",
            MarketType.PlayerPassCompletions => "player_pass_completions",
            MarketType.PlayerPassInterceptions => "player_pass_interceptions",
            MarketType.PlayerRushYards => "player_rush_yds",
            MarketType.PlayerRushAttempts => "player_rush_attempts",
            MarketType.PlayerReceptions => "player_receptions",
            MarketType.PlayerReceivingYards => "player_reception_yds",
            MarketType.PlayerRushReceivingYards => "player_rush_reception_yds",
            MarketType.PlayerTotalTDs => "player_tds_over",
            MarketType.PlayerSacks => "player_sacks",
            MarketType.PlayerTacklesAssists => "player_tackles_assists",

            // Other
            MarketType.TeamTotal => "team_totals",
            MarketType.OddEven => "odd_even",
            MarketType.Overtime => "overtime",
            MarketType.ThreeWayH1 => "h2h_3_way_h1",

            _ => "spreads"
        };

        /// <summary>
        /// Parse API key to market type
        /// </summary>
        public static MarketType? FromApiKey(string apiKey) => apiKey switch
        {
            "spreads" => MarketType.Spread,
            "totals" => MarketType.Total,
            "h2h" => MarketType.Moneyline,
            "spreads_h1" => MarketType.SpreadH1,
            "spreads_h2" => MarketType.SpreadH2,
            "spreads_q2" => MarketType.SpreadQ2,
            "spreads_q3" => MarketType.SpreadQ3,
            "spreads_q4" => MarketType.SpreadQ4,
            "totals_h1" => MarketType.TotalH1,
            "totals_h2" => MarketType.TotalH2,
            "totals_q2" => MarketType.TotalQ2,
            "totals_q3" => MarketType.TotalQ3,
            "totals_q4" => MarketType.TotalQ4,
            "h2h_h1" => MarketType.MoneylineH1,
            "h2h_h2" => MarketType.MoneylineH2,
            "h2h_q2" => MarketType.MoneylineQ2,
            "h2h_q3" => MarketType.MoneylineQ3,
            "h2h_q4" => MarketType.MoneylineQ4,
            "alternate_spreads" => MarketType.AlternateSpread,
            "alternate_totals" => MarketType.AlternateTotal,
            "alternate_team_totals" => MarketType.AlternateTeamTotal,
            "player_anytime_td" => MarketType.PlayerAnytimeTD,
            "player_1st_td" => MarketType.PlayerFirstTD,
            "player_last_td" => MarketType.PlayerLastTD,
            "player_pass_yds" => MarketType.PlayerPassYards,
            "player_pass_tds" => MarketType.PlayerPassTDs,
            "player_pass_attempts" => MarketType.PlayerPassAttempts,
            "player_pass_completions" => MarketType.PlayerPassCompletions,
            "player_pass_interceptions" => MarketType.PlayerPassInterceptions,
            "player_rush_yds" => MarketType.PlayerRushYards,
            "player_rush_attempts" => MarketType.PlayerRushAttempts,
            "player_receptions" => MarketType.PlayerReceptions,
            "player_reception_yds" => MarketType.PlayerReceivingYards,
            "player_rush_reception_yds" => MarketType.PlayerRushReceivingYards,
            "player_tds_over" => MarketType.PlayerTotalTDs,
            "player_sacks" => MarketType.PlayerSacks,
            "player_tackles_assists" => MarketType.PlayerTacklesAssists,
            "team_totals" => MarketType.TeamTotal,
            "odd_even" => MarketType.OddEven,
            "overtime" => MarketType.Overtime,
            "h2h_3_way_h1" => MarketType.ThreeWayH1,
            _ => null
        };

        /// <summary>
        /// Get the minimum subscription tier required for a market type
        /// </summary>
        public static SubscriptionTier RequiredTier(this MarketType marketType) => marketType switch
        {
            // Starter tier - basic game lines
            MarketType.Spread => SubscriptionTier.Starter,
            MarketType.Total => SubscriptionTier.Starter,
            MarketType.Moneyline => SubscriptionTier.Starter,

            // Core tier - half/quarter/alternates
            MarketType.SpreadH1 or MarketType.SpreadH2 or
            MarketType.SpreadQ2 or MarketType.SpreadQ3 or MarketType.SpreadQ4 or
            MarketType.TotalH1 or MarketType.TotalH2 or
            MarketType.TotalQ2 or MarketType.TotalQ3 or MarketType.TotalQ4 or
            MarketType.MoneylineH1 or MarketType.MoneylineH2 or
            MarketType.MoneylineQ2 or MarketType.MoneylineQ3 or MarketType.MoneylineQ4 or
            MarketType.AlternateSpread or MarketType.AlternateTotal or MarketType.AlternateTeamTotal or
            MarketType.TeamTotal or MarketType.OddEven or MarketType.Overtime or MarketType.ThreeWayH1
                => SubscriptionTier.Core,

            // Sharp tier - player props
            MarketType.PlayerAnytimeTD or MarketType.PlayerFirstTD or MarketType.PlayerLastTD or
            MarketType.PlayerPassYards or MarketType.PlayerPassTDs or
            MarketType.PlayerPassAttempts or MarketType.PlayerPassCompletions or MarketType.PlayerPassInterceptions or
            MarketType.PlayerRushYards or MarketType.PlayerRushAttempts or
            MarketType.PlayerReceptions or MarketType.PlayerReceivingYards or
            MarketType.PlayerRushReceivingYards or MarketType.PlayerTotalTDs or
            MarketType.PlayerSacks or MarketType.PlayerTacklesAssists
                => SubscriptionTier.Sharp,

            _ => SubscriptionTier.Starter
        };

        /// <summary>
        /// Get display name for market type
        /// </summary>
        public static string ToDisplayName(this MarketType marketType) => marketType switch
        {
            MarketType.Spread => "Spread",
            MarketType.Total => "Total (O/U)",
            MarketType.Moneyline => "Moneyline",
            MarketType.SpreadH1 => "1st Half Spread",
            MarketType.SpreadH2 => "2nd Half Spread",
            MarketType.SpreadQ2 => "2nd Quarter Spread",
            MarketType.SpreadQ3 => "3rd Quarter Spread",
            MarketType.SpreadQ4 => "4th Quarter Spread",
            MarketType.TotalH1 => "1st Half Total",
            MarketType.TotalH2 => "2nd Half Total",
            MarketType.TotalQ2 => "2nd Quarter Total",
            MarketType.TotalQ3 => "3rd Quarter Total",
            MarketType.TotalQ4 => "4th Quarter Total",
            MarketType.MoneylineH1 => "1st Half Moneyline",
            MarketType.MoneylineH2 => "2nd Half Moneyline",
            MarketType.MoneylineQ2 => "2nd Quarter Moneyline",
            MarketType.MoneylineQ3 => "3rd Quarter Moneyline",
            MarketType.MoneylineQ4 => "4th Quarter Moneyline",
            MarketType.AlternateSpread => "Alternate Spread",
            MarketType.AlternateTotal => "Alternate Total",
            MarketType.AlternateTeamTotal => "Alternate Team Total",
            MarketType.PlayerAnytimeTD => "Anytime TD Scorer",
            MarketType.PlayerFirstTD => "First TD Scorer",
            MarketType.PlayerLastTD => "Last TD Scorer",
            MarketType.PlayerPassYards => "Passing Yards",
            MarketType.PlayerPassTDs => "Passing TDs",
            MarketType.PlayerPassAttempts => "Pass Attempts",
            MarketType.PlayerPassCompletions => "Pass Completions",
            MarketType.PlayerPassInterceptions => "Interceptions Thrown",
            MarketType.PlayerRushYards => "Rushing Yards",
            MarketType.PlayerRushAttempts => "Rush Attempts",
            MarketType.PlayerReceptions => "Receptions",
            MarketType.PlayerReceivingYards => "Receiving Yards",
            MarketType.PlayerRushReceivingYards => "Rush + Receiving Yards",
            MarketType.PlayerTotalTDs => "Total TDs",
            MarketType.PlayerSacks => "Sacks",
            MarketType.PlayerTacklesAssists => "Tackles + Assists",
            MarketType.TeamTotal => "Team Total",
            MarketType.OddEven => "Odd/Even",
            MarketType.Overtime => "Overtime",
            MarketType.ThreeWayH1 => "1st Half 3-Way",
            _ => marketType.ToString()
        };

        /// <summary>
        /// Check if market type is a player prop
        /// </summary>
        public static bool IsPlayerProp(this MarketType marketType) => marketType switch
        {
            MarketType.PlayerAnytimeTD or MarketType.PlayerFirstTD or MarketType.PlayerLastTD or
            MarketType.PlayerPassYards or MarketType.PlayerPassTDs or
            MarketType.PlayerPassAttempts or MarketType.PlayerPassCompletions or MarketType.PlayerPassInterceptions or
            MarketType.PlayerRushYards or MarketType.PlayerRushAttempts or
            MarketType.PlayerReceptions or MarketType.PlayerReceivingYards or
            MarketType.PlayerRushReceivingYards or MarketType.PlayerTotalTDs or
            MarketType.PlayerSacks or MarketType.PlayerTacklesAssists => true,
            _ => false
        };

        /// <summary>
        /// Get market category for grouping
        /// </summary>
        public static MarketCategory GetCategory(this MarketType marketType) => marketType switch
        {
            MarketType.Spread or MarketType.Total or MarketType.Moneyline => MarketCategory.GameLines,

            MarketType.SpreadH1 or MarketType.SpreadH2 or
            MarketType.TotalH1 or MarketType.TotalH2 or
            MarketType.MoneylineH1 or MarketType.MoneylineH2 or MarketType.ThreeWayH1 => MarketCategory.HalfLines,

            MarketType.SpreadQ2 or MarketType.SpreadQ3 or MarketType.SpreadQ4 or
            MarketType.TotalQ2 or MarketType.TotalQ3 or MarketType.TotalQ4 or
            MarketType.MoneylineQ2 or MarketType.MoneylineQ3 or MarketType.MoneylineQ4 => MarketCategory.QuarterLines,

            MarketType.AlternateSpread or MarketType.AlternateTotal or MarketType.AlternateTeamTotal => MarketCategory.Alternates,

            MarketType.PlayerAnytimeTD or MarketType.PlayerFirstTD or MarketType.PlayerLastTD or
            MarketType.PlayerTotalTDs => MarketCategory.PlayerTD,

            MarketType.PlayerPassYards or MarketType.PlayerPassTDs or
            MarketType.PlayerPassAttempts or MarketType.PlayerPassCompletions or
            MarketType.PlayerPassInterceptions => MarketCategory.PlayerPassing,

            MarketType.PlayerRushYards or MarketType.PlayerRushAttempts => MarketCategory.PlayerRushing,

            MarketType.PlayerReceptions or MarketType.PlayerReceivingYards => MarketCategory.PlayerReceiving,

            MarketType.PlayerRushReceivingYards => MarketCategory.PlayerCombo,

            MarketType.PlayerSacks or MarketType.PlayerTacklesAssists => MarketCategory.PlayerDefense,

            MarketType.TeamTotal or MarketType.OddEven or MarketType.Overtime => MarketCategory.Other,

            _ => MarketCategory.GameLines
        };
    }

    public enum MarketCategory
    {
        GameLines,
        HalfLines,
        QuarterLines,
        Alternates,
        PlayerTD,
        PlayerPassing,
        PlayerRushing,
        PlayerReceiving,
        PlayerCombo,
        PlayerDefense,
        Other
    }

    /// <summary>
    /// Normalized odds data for charting and fingerprinting
    /// </summary>
    public class NormalizedOdds
    {
        public string EventId { get; set; } = string.Empty;
        public string HomeTeam { get; set; } = string.Empty;
        public string AwayTeam { get; set; } = string.Empty;
        public DateTime CommenceTime { get; set; }
        public MarketType MarketType { get; set; }
        public string? PlayerName { get; set; }  // For player props
        public List<BookSnapshot> Snapshots { get; set; } = [];

        // Legacy compatibility - maps to EventId
        public string GameId => EventId;
    }

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
        public MarketType? MarketType => MarketTypeExtensions.FromApiKey(MarketKey);
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
}