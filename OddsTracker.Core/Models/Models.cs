using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace OddsTracker.Core.Models
{
    public record ChartResult(
        byte[] ImageData,
        string Title,
        string Description,
        string? Analysis = null
    );

    public record OddsQueryResult(
        bool Success,
        List<ChartResult>? Charts,
        string? ErrorMessage,
        OddsQuery? ParsedQuery,
        string? GameDescription
    );

    /// <summary>
    /// Represents a unique market (game + market type combination)
    /// </summary>
    public record MarketKey(
        string EventId,
        string HomeTeam,
        string AwayTeam,
        MarketType MarketType,
        DateTime CommenceTime
    )
    {
        public string Key => $"{EventId}:{MarketType}";
    }

    /// <summary>
    /// Wrapper for caching closing line values.
    /// </summary>
    public class ClosingLineWrapper
    {
        public decimal ClosingLine { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Captures the current state of odds across all books for a market
    /// </summary>
    public class MarketFingerprint
    {
        public string FingerprintId { get; set; } = Guid.NewGuid().ToString();
        public MarketKey Market { get; set; } = null!;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Movement metrics
        public decimal ConsensusLine { get; set; }
        public decimal PreviousConsensusLine { get; set; }
        public decimal DeltaMagnitude => Math.Abs(ConsensusLine - PreviousConsensusLine);

        // Velocity (points per hour)
        public decimal Velocity { get; set; }

        // First mover detection
        public string? FirstMoverBook { get; set; }
        public BookmakerTier FirstMoverType { get; set; }
        public DateTime? FirstMoveTime { get; set; }

        // Retail lag (how long before retail books follow)
        public TimeSpan? RetailLag { get; set; }

        // Book breakdown
        public List<BookSnapshot> BookSnapshots { get; set; } = new();

        // Confirmation count
        public int ConfirmingBooks => BookSnapshots.Count(b =>
            Math.Abs(b.Line - ConsensusLine) < 0.5m);

        // Stability tracking
        public DateTime? LastReversalTime { get; set; }
        public TimeSpan StabilityWindow => LastReversalTime.HasValue
            ? DateTime.UtcNow - LastReversalTime.Value
            : DateTime.UtcNow - Timestamp;

        // Hash for change detection
        public string ContentHash { get; set; } = string.Empty;

        public bool HasMaterialChange(MarketFingerprint? previous)
        {
            if (previous == null) return true;

            // Material change = line moved 0.5+ points OR new first mover
            return DeltaMagnitude >= 0.5m
                || FirstMoverBook != previous.FirstMoverBook
                || ContentHash != previous.ContentHash;
        }
    }

    public class BookSnapshot
    {
        public string BookmakerName { get; set; } = string.Empty;
        public string BookmakerKey { get; set; } = string.Empty;
        public BookmakerTier BookType { get; set; }
        public decimal Line { get; set; }
        public decimal? HomeOdds { get; set; }  // For game lines: home team odds / For props: Over odds
        public decimal? AwayOdds { get; set; }  // For game lines: away team odds / For props: Under odds
        public DateTime Timestamp { get; set; }

        // For player props
        public string? PlayerName { get; set; }
        public string? OutcomeName { get; set; }  // "Over" or "Under" for props
    }

    /// <summary>
    /// Confidence score with breakdown
    /// </summary>
    public class ConfidenceScore
    {
        public string MarketKey { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Overall score 0-100
        public int Score { get; set; }
        public ConfidenceLevel Level => Score switch
        {
            >= 80 => ConfidenceLevel.High,
            >= 50 => ConfidenceLevel.Medium,
            _ => ConfidenceLevel.Low
        };

        // Component scores (each 0-25)
        public int FirstMoverScore { get; set; }      // Who moved first (sharp = high)
        public int VelocityScore { get; set; }        // Speed of movement
        public int ConfirmationScore { get; set; }    // Number of books confirming
        public int StabilityScore { get; set; }       // No reversal window

        // Metadata
        public string Explanation { get; set; } = string.Empty;
        public static bool IsCacheable => true; // Deterministic, always cacheable
    }

    /// <summary>
    /// Alert to be dispatched
    /// </summary>
    public class MarketAlert
    {
        public string AlertId { get; set; } = Guid.NewGuid().ToString();
        public MarketFingerprint Fingerprint { get; set; } = null!;
        public ConfidenceScore Confidence { get; set; } = null!;
        public AlertType Type { get; set; }
        public AlertPriority Priority { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Routing
        public List<AlertChannel> TargetChannels { get; set; } = new();
        public bool SendDM { get; set; }

        // Deduplication
        public string DedupeKey => $"{Fingerprint.Market.Key}:{Type}:{Confidence.Level}";
        public DateTime? LastAlertTime { get; set; }

        // Cooldown check
        public bool IsInCooldown(TimeSpan cooldownPeriod)
        {
            return LastAlertTime.HasValue &&
                   DateTime.UtcNow - LastAlertTime.Value < cooldownPeriod;
        }
    }

    /// <summary>
    /// Historical signal for performance tracking
    /// </summary>
    public class SignalSnapshot
    {
        public long Id { get; set; }
        public string EventId { get; set; } = string.Empty;
        public MarketType MarketType { get; set; }
        public DateTime SignalTime { get; set; }
        public DateTime GameTime { get; set; }

        // Signal data at time of alert
        public decimal LineAtSignal { get; set; }
        public ConfidenceLevel ConfidenceAtSignal { get; set; }
        public int ConfidenceScoreAtSignal { get; set; }
        public string FirstMoverBook { get; set; } = string.Empty;
        public BookmakerTier FirstMoverType { get; set; }

        // Outcome tracking (filled after game)
        public decimal? ClosingLine { get; set; }
        public SignalOutcome? Outcome { get; set; }
        public decimal? LineMovement => ClosingLine.HasValue
            ? ClosingLine.Value - LineAtSignal
            : null;
    }

    /// <summary>
    /// Aggregated performance stats
    /// </summary>
    public class PerformanceStats
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public int TotalSignals { get; set; }
        public int ExtendedCount { get; set; }
        public int RevertedCount { get; set; }
        public int StableCount { get; set; }

        public decimal ExtensionRate => TotalSignals > 0
            ? (decimal)ExtendedCount / TotalSignals * 100
            : 0;

        // By confidence bucket
        public Dictionary<ConfidenceLevel, BucketStats> ByConfidence { get; set; } = new();

        // By first mover type
        public Dictionary<BookmakerTier, BucketStats> ByFirstMover { get; set; } = new();
    }

    public class BucketStats
    {
        public int Total { get; set; }
        public int Extended { get; set; }
        public int Reverted { get; set; }
        public decimal ExtensionRate => Total > 0 ? (decimal)Extended / Total * 100 : 0;
    }

    /// <summary>
    /// User subscription info
    /// </summary>
    public class UserSubscription
    {
        public ulong DiscordUserId { get; set; }
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public SubscriptionTier Tier { get; set; } = SubscriptionTier.Starter;
        public DateTime? SubscriptionStart { get; set; }
        public DateTime? SubscriptionEnd { get; set; }
        public DateTime? GracePeriodEnd { get; set; }

        // Usage tracking
        public int QueriesUsedToday { get; set; }
        public DateTime LastQueryDate { get; set; }

        public bool IsActive => Tier != SubscriptionTier.Starter &&
            (SubscriptionEnd == null || SubscriptionEnd > DateTime.UtcNow ||
             GracePeriodEnd > DateTime.UtcNow);

        public int DailyQueryLimit => Tier switch
        {
            SubscriptionTier.Starter => 10,
            SubscriptionTier.Core => 50,
            SubscriptionTier.Sharp => int.MaxValue,
            _ => 10
        };

        public int HistoricalDaysAllowed => Tier switch
        {
            SubscriptionTier.Starter => 1,
            SubscriptionTier.Core => 7,
            SubscriptionTier.Sharp => 30,
            _ => 1
        };

        public bool CanAccessSharpChannel => Tier == SubscriptionTier.Sharp;
        public bool CanReceiveDM => Tier == SubscriptionTier.Sharp;
        public bool CanReceiveAlerts => Tier >= SubscriptionTier.Core;
    }

    /// <summary>
    /// Rate limit tracking
    /// </summary>
    public class RateLimitEntry
    {
        public ulong UserId { get; set; }
        public int RequestCount { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }

        public bool IsExceeded(int limit) => RequestCount >= limit;

        public void Increment()
        {
            if (DateTime.UtcNow > WindowEnd)
            {
                // Reset window
                RequestCount = 1;
                WindowStart = DateTime.UtcNow;
                WindowEnd = WindowStart.Date.AddDays(1);
            }
            else
            {
                RequestCount++;
            }
        }
    }

        /// <summary>
        /// A snapshot of odds at a specific point in time
        /// </summary>
        public record OddsSnapshot(
        DateTime Timestamp,
        OddsEvent Event
    );

    /// <summary>
    /// An event (game) with odds from multiple bookmakers
    /// </summary>
    public record OddsEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("sport_key")]
        public string SportKey { get; init; } = "";

        [JsonPropertyName("sport_title")]
        public string SportTitle { get; init; } = "";

        [JsonPropertyName("commence_time")]
        public DateTime CommenceTime { get; init; }

        [JsonPropertyName("home_team")]
        public string HomeTeam { get; init; } = "";

        [JsonPropertyName("away_team")]
        public string AwayTeam { get; init; } = "";

        [JsonPropertyName("bookmakers")]
        public List<Bookmaker> Bookmakers { get; init; } = new();
    }

    /// <summary>
    /// A bookmaker with their odds for various markets
    /// </summary>
    public record Bookmaker
    {
        [JsonPropertyName("key")]
        public string Key { get; init; } = "";

        [JsonPropertyName("title")]
        public string Title { get; init; } = "";

        [JsonPropertyName("last_update")]
        public DateTime LastUpdate { get; init; }

        [JsonPropertyName("markets")]
        public List<Market> Markets { get; init; } = new();
    }

    /// <summary>
    /// A betting market (h2h, spreads, totals, etc.)
    /// </summary>
    public record Market
    {
        [JsonPropertyName("key")]
        public string Key { get; init; } = "";

        [JsonPropertyName("last_update")]
        public DateTime? LastUpdate { get; init; }

        [JsonPropertyName("outcomes")]
        public List<Outcome> Outcomes { get; init; } = new();
    }

    /// <summary>
    /// An outcome within a market
    /// </summary>
    public record Outcome
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("price")]
        public int Price { get; init; }

        /// <summary>
        /// For spreads: the point value (e.g., -3.5)
        /// For totals: the total points line (e.g., 47.5)
        /// For player props: the line (e.g., 21.5 pass completions)
        /// </summary>
        [JsonPropertyName("point")]
        public decimal? Point { get; init; }

        /// <summary>
        /// For player props: the player name (e.g., "Josh Allen")
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    /// <summary>
    /// Basic event info (without odds)
    /// </summary>
    public record SportEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("sport_key")]
        public string SportKey { get; init; } = "";

        [JsonPropertyName("sport_title")]
        public string SportTitle { get; init; } = "";

        [JsonPropertyName("commence_time")]
        public DateTime CommenceTime { get; init; }

        [JsonPropertyName("home_team")]
        public string HomeTeam { get; init; } = "";

        [JsonPropertyName("away_team")]
        public string AwayTeam { get; init; } = "";
    }

    /// <summary>
    /// Event with score information
    /// </summary>
    public record ScoreEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("sport_key")]
        public string SportKey { get; init; } = "";

        [JsonPropertyName("sport_title")]
        public string SportTitle { get; init; } = "";

        [JsonPropertyName("commence_time")]
        public DateTime CommenceTime { get; init; }

        [JsonPropertyName("completed")]
        public bool Completed { get; init; }

        [JsonPropertyName("home_team")]
        public string HomeTeam { get; init; } = "";

        [JsonPropertyName("away_team")]
        public string AwayTeam { get; init; } = "";

        [JsonPropertyName("scores")]
        public List<TeamScore>? Scores { get; init; }

        [JsonPropertyName("last_update")]
        public DateTime? LastUpdate { get; init; }
    }

    public record TeamScore
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("score")]
        public string Score { get; init; } = "";
    }

    /// <summary>
    /// Historical odds response wrapper (for all games)
    /// </summary>
    public record HistoricalOddsResponse
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; init; }

        [JsonPropertyName("previous_timestamp")]
        public DateTime? PreviousTimestamp { get; init; }

        [JsonPropertyName("next_timestamp")]
        public DateTime? NextTimestamp { get; init; }

        [JsonPropertyName("data")]
        public List<OddsEvent> Data { get; init; } = new();
    }

    /// <summary>
    /// Historical odds response wrapper (for single event)
    /// </summary>
    public record HistoricalEventOddsResponse
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; init; }

        [JsonPropertyName("previous_timestamp")]
        public DateTime? PreviousTimestamp { get; init; }

        [JsonPropertyName("next_timestamp")]
        public DateTime? NextTimestamp { get; init; }

        [JsonPropertyName("data")]
        public OddsEvent? Data { get; init; }
    }

    /// <summary>
    /// Info about a player's team
    /// </summary>
    public class PlayerTeamInfo
    {
        public string PlayerName { get; set; } = string.Empty;
        public string TeamFullName { get; set; } = string.Empty;
        public string TeamKey { get; set; } = string.Empty;  // e.g., "BUF"
        public string Position { get; set; } = string.Empty;  // e.g., "QB"
    }

    public class WebhookPayload
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("embeds")]
        public List<WebhookEmbed> Embeds { get; set; } = [];
    }

    public class WebhookEmbed
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("color")]
        public int Color { get; set; }

        [JsonPropertyName("fields")]
        public List<WebhookField> Fields { get; set; } = [];

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
    }

    public class WebhookField
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("inline")]
        public bool Inline { get; set; }
    }

    /// <summary>
    /// Custom timer info class for Azure Functions isolated worker
    /// </summary>
    public class MyTimerInfo
    {
        public MyScheduleStatus? ScheduleStatus { get; set; }
        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }
        public DateTime Next { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Response from the /events/{eventId}/markets endpoint
    /// </summary>
    public record EventMarketsResponse
    {
        [JsonProperty("id")]
        public string Id { get; init; } = "";

        [JsonProperty("sport_key")]
        public string SportKey { get; init; } = "";

        [JsonProperty("sport_title")]
        public string SportTitle { get; init; } = "";

        [JsonProperty("commence_time")]
        public DateTime CommenceTime { get; init; }

        [JsonProperty("home_team")]
        public string HomeTeam { get; init; } = "";

        [JsonProperty("away_team")]
        public string AwayTeam { get; init; } = "";

        [JsonProperty("bookmakers")]
        public List<BookmakerMarkets> Bookmakers { get; init; } = [];
    }

    /// <summary>
    /// Bookmaker with available markets (no odds, just market keys)
    /// </summary>
    public record BookmakerMarkets
    {
        [JsonProperty("key")]
        public string Key { get; init; } = "";

        [JsonProperty("title")]
        public string Title { get; init; } = "";

        [JsonProperty("markets")]
        public List<MarketInfo> Markets { get; init; } = [];
    }

    /// <summary>
    /// Market availability info
    /// </summary>
    public record MarketInfo
    {
        [JsonProperty("key")]
        public string Key { get; init; } = "";

        [JsonProperty("last_update")]
        public DateTime LastUpdate { get; init; }
    }
}