using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OddsTracker.Core.Models
{
    public class Entities
    {
        /// <summary>
        /// Database entity for signal snapshots.
        /// </summary>
        public class SignalSnapshotEntity
        {
            public long Id { get; set; }
            public string EventId { get; set; } = string.Empty;
            [Required]
            [MaxLength(100)]
            public string MarketKey { get; set; } = string.Empty;
            public DateTime SignalTime { get; set; }
            public DateTime GameTime { get; set; }
            public decimal LineAtSignal { get; set; }
            public ConfidenceLevel ConfidenceAtSignal { get; set; }
            public int ConfidenceScoreAtSignal { get; set; }
            public string? FirstMoverBook { get; set; }
            public BookmakerTier FirstMoverType { get; set; }
            public decimal? ClosingLine { get; set; }
            public SignalOutcome? Outcome { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public SignalSnapshot ToModel() => new()
            {
                Id = Id,
                EventId = EventId,
                MarketKey = MarketKey,
                SignalTime = SignalTime,
                GameTime = GameTime,
                LineAtSignal = LineAtSignal,
                ConfidenceAtSignal = ConfidenceAtSignal,
                ConfidenceScoreAtSignal = ConfidenceScoreAtSignal,
                FirstMoverBook = FirstMoverBook ?? string.Empty,
                FirstMoverType = FirstMoverType,
                ClosingLine = ClosingLine,
                Outcome = Outcome
            };

            public static SignalSnapshotEntity FromModel(SignalSnapshot model) => new()
            {
                Id = model.Id,
                EventId = model.EventId,
                MarketKey = model.MarketKey,
                SignalTime = model.SignalTime,
                GameTime = model.GameTime,
                LineAtSignal = model.LineAtSignal,
                ConfidenceAtSignal = model.ConfidenceAtSignal,
                ConfidenceScoreAtSignal = model.ConfidenceScoreAtSignal,
                FirstMoverBook = model.FirstMoverBook,
                FirstMoverType = model.FirstMoverType,
                ClosingLine = model.ClosingLine,
                Outcome = model.Outcome
            };
        }

        /// <summary>
        /// Database entity for user subscriptions.
        /// </summary>
        public class UserSubscriptionEntity
        {
            public long Id { get; set; }
            public ulong DiscordUserId { get; set; }
            public string? StripeCustomerId { get; set; }
            public string? StripeSubscriptionId { get; set; }
            public SubscriptionTier Tier { get; set; } = SubscriptionTier.Starter;
            public DateTime? SubscriptionStart { get; set; }
            public DateTime? SubscriptionEnd { get; set; }
            public DateTime? GracePeriodEnd { get; set; }
            public int QueriesUsedToday { get; set; }
            public DateTime LastQueryDate { get; set; } = DateTime.UtcNow.Date;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public UserSubscription ToModel() => new()
            {
                DiscordUserId = DiscordUserId,
                StripeCustomerId = StripeCustomerId,
                StripeSubscriptionId = StripeSubscriptionId,
                Tier = Tier,
                SubscriptionStart = SubscriptionStart,
                SubscriptionEnd = SubscriptionEnd,
                GracePeriodEnd = GracePeriodEnd,
                QueriesUsedToday = QueriesUsedToday,
                LastQueryDate = LastQueryDate
            };

            public static UserSubscriptionEntity FromModel(UserSubscription model) => new()
            {
                DiscordUserId = model.DiscordUserId,
                StripeCustomerId = model.StripeCustomerId,
                StripeSubscriptionId = model.StripeSubscriptionId,
                Tier = model.Tier,
                SubscriptionStart = model.SubscriptionStart,
                SubscriptionEnd = model.SubscriptionEnd,
                GracePeriodEnd = model.GracePeriodEnd,
                QueriesUsedToday = model.QueriesUsedToday,
                LastQueryDate = model.LastQueryDate
            };
        }

        /// <summary>
        /// Database entity for sports (NFL, NBA, MLB, etc.)
        /// </summary>
        [Table("Sports")]
        public class SportEntity
        {
            [Key]
            public int Id { get; set; }

            [Required]
            [MaxLength(100)]
            public string Key { get; set; } = string.Empty;

            [Required]
            [MaxLength(100)]
            public string DisplayName { get; set; } = string.Empty;

            [Required]
            [MaxLength(50)]
            public string Category { get; set; } = string.Empty;

            [Required]
            [MaxLength(50)]
            public string PeriodType { get; set; } = string.Empty;

            public bool IsActive { get; set; } = true;

            [MaxLength(500)]
            public string? Keywords { get; set; }

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? UpdatedAt { get; set; }

            // Navigation
            public ICollection<SportMarketEntity> SportMarkets { get; set; } = [];

            public Sport ToModel() => new()
            {
                Id = Id,
                Key = Key,
                DisplayName = DisplayName,
                Category = Enum.TryParse<SportCategory>(Category, true, out var cat) ? cat : SportCategory.Other,
                PeriodType = Enum.TryParse<GamePeriodType>(PeriodType, true, out var pt) ? pt : GamePeriodType.None,
                IsActive = IsActive,
                AvailableMarkets = [.. SportMarkets
                    .Where(sm => sm.IsActive && sm.MarketDefinition?.IsActive == true)
                    .Select(sm => sm.MarketDefinition!.ToModel())]
            };
        }

        /// <summary>
        /// Database entity for market definitions (spreads, totals, player props, etc.)
        /// </summary>
        [Table("MarketDefinitions")]
        public class MarketDefinitionEntity
        {
            [Key]
            public int Id { get; set; }

            [Required]
            [MaxLength(100)]
            public string Key { get; set; } = string.Empty;

            [Required]
            [MaxLength(150)]
            public string DisplayName { get; set; } = string.Empty;

            [Required]
            [MaxLength(50)]
            public string Category { get; set; } = string.Empty;

            [Required]
            [MaxLength(50)]
            public string OutcomeType { get; set; } = string.Empty;

            [Required]
            [MaxLength(20)]
            public string RequiredTier { get; set; } = "Starter";

            public bool IsPlayerProp { get; set; }
            public bool IsAlternate { get; set; }

            [MaxLength(50)]
            public string? Period { get; set; }

            [MaxLength(500)]
            public string? Description { get; set; }

            public bool IsActive { get; set; } = true;

            [MaxLength(500)]
            public string? Keywords { get; set; }

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? UpdatedAt { get; set; }

            // Navigation
            public ICollection<SportMarketEntity> SportMarkets { get; set; } = [];

            public MarketDefinition ToModel()
            {
                return new()
                {
                    Id = Id,
                    Key = Key,
                    DisplayName = DisplayName,
                    Category = Enum.TryParse<MarketCategory>(Category, true, out var cat) ? cat : MarketCategory.Other,
                    OutcomeType = Enum.TryParse<OutcomeType>(OutcomeType, true, out var ot) ? ot : Models.OutcomeType.TeamBased,
                    RequiredTier = Enum.TryParse<SubscriptionTier>(RequiredTier, true, out var tier) ? tier : SubscriptionTier.Starter,
                    IsPlayerProp = IsPlayerProp,
                    IsAlternate = IsAlternate,
                    Period = string.IsNullOrEmpty(Period) ? null : Enum.TryParse<GamePeriod>(Period, true, out var gp) ? gp : null,
                    Description = Description
                };
            }
        }

        /// <summary>
        /// Junction table: which markets are available for which sports
        /// </summary>
        [Table("SportMarkets")]
        public class SportMarketEntity
        {
            public int SportId { get; set; }
            public int MarketDefinitionId { get; set; }
            public bool IsActive { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            // Navigation
            [ForeignKey(nameof(SportId))]
            public SportEntity? Sport { get; set; }

            [ForeignKey(nameof(MarketDefinitionId))]
            public MarketDefinitionEntity? MarketDefinition { get; set; }
        }

        /// <summary>
        /// Database entity for bookmakers
        /// </summary>
        [Table("Bookmakers")]
        public class BookmakerEntity
        {
            [Key]
            public int Id { get; set; }

            [Required]
            [MaxLength(100)]
            public string Key { get; set; } = string.Empty;

            [Required]
            [MaxLength(100)]
            public string DisplayName { get; set; } = string.Empty;

            [Required]
            [MaxLength(20)]
            public string RequiredTier { get; set; } = "Starter";

            [Required]
            [MaxLength(20)]
            public string Tier { get; set; } = "Retail";

            [Required]
            [MaxLength(50)]
            public string Region { get; set; } = "us";

            public bool IsActive { get; set; } = true;

            [MaxLength(500)]
            public string? Keywords { get; set; }

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? UpdatedAt { get; set; }

            public BookmakerInfo ToModel() => new()
            {
                Id = Id,
                Key = Key,
                DisplayName = DisplayName,
                RequiredTier = Enum.TryParse<SubscriptionTier>(RequiredTier, true, out var rt) ? rt : SubscriptionTier.Starter,
                Tier = Enum.TryParse<BookmakerTier>(Tier, true, out var bt) ? bt : BookmakerTier.Retail,
                Region = Region,
                IsActive = IsActive
            };
        }
    }
}
