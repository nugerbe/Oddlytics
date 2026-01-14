namespace OddsTracker.Core.Models
{
    /// <summary>
    /// Configuration options for alert thresholds
    /// </summary>
    public class AlertEngineOptions
    {
        public int HighConfidenceThreshold { get; set; } = 80;
        public int MediumConfidenceThreshold { get; set; } = 50;
        public int DefaultCooldownMinutes { get; set; } = 15;
        public int HighPriorityCooldownMinutes { get; set; } = 5;
        public int UrgentCooldownMinutes { get; set; } = 2;
        public int DedupeWindowMinutes { get; set; } = 60;
        public decimal MinDeltaForSharpAlert { get; set; } = 0.5m;
        public decimal MinDeltaForMovementAlert { get; set; } = 1.0m;
        public int MinBooksForConsensus { get; set; } = 5;
        public int ReversalWindowMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Configuration options for cache TTLs.
    /// </summary>
    public class CacheOptions
    {
        public bool Enabled { get; set; } = false;
        public int DefaultTtlMinutes { get; set; } = 15;
        public int RawOddsTtlSeconds { get; set; } = 30;
        public int FingerprintTtlHours { get; set; } = 24;
        public int ConfidenceTtlMinutes { get; set; } = 5;
        public int AIExplanationTtlMinutes { get; set; } = 60;
        public int SubscriptionTtlMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Configuration options for confidence scoring thresholds.
    /// </summary>
    public class ConfidenceScoringOptions
    {
        // Component weights (each max this value, total max 4x)
        public int MaxComponentScore { get; set; } = 25;

        // First mover scores by book type
        public int SharpMoverScore { get; set; } = 25;
        public int MarketMoverScore { get; set; } = 15;
        public int RetailMoverScore { get; set; } = 5;

        // Velocity thresholds (points per hour)
        public decimal HighVelocityThreshold { get; set; } = 2.0m;
        public decimal MediumVelocityThreshold { get; set; } = 0.5m;

        // Confirmation thresholds (number of books)
        public int HighConfirmationThreshold { get; set; } = 5;
        public int MediumConfirmationThreshold { get; set; } = 3;

        // Stability thresholds (minutes since last reversal)
        public int HighStabilityMinutes { get; set; } = 60;
        public int MediumStabilityMinutes { get; set; } = 15;
    }

    public class DiscordBotOptions
    {
        public string Token { get; set; } = string.Empty;
        public ulong GuildId { get; set; }

        // Channel IDs
        public ulong OddsBotChannelId { get; set; }
        public ulong OddsAlertsChannelId { get; set; }
        public ulong SharpSignalsChannelId { get; set; }

        // Role IDs
        public ulong StarterRoleId { get; set; }
        public ulong CoreRoleId { get; set; }
        public ulong SharpRoleId { get; set; }
    }

    /// <summary>
    /// Configuration for historical tracking and tier-based access.
    /// </summary>
    public class HistoricalTrackerOptions
    {
        public int StarterHistoricalDays { get; set; } = 1;
        public int CoreHistoricalDays { get; set; } = 7;
        public int SharpHistoricalDays { get; set; } = 30;
        public decimal StableThreshold { get; set; } = 0.5m;
    }

    public class OddsPollerOptions
    {
        public bool Enabled { get; set; } = true;
        public int BaseIntervalSeconds { get; set; } = 60;
        public int ActiveGameIntervalSeconds { get; set; } = 30;
        public int MaxApiCallsPerMinute { get; set; } = 30;
        public List<string> Sports { get; set; } = ["americanfootball_nfl"];
        public List<string> Markets { get; set; } = ["spreads", "totals", "h2h"];
    }
}
