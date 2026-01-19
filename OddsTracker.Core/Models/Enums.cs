namespace OddsTracker.Core.Models
{
    public enum TeamSide
    {
        Home,
        Away
    }

    public enum BookmakerTier
    {
        Retail,  // Starter+
        Market,  // Core+
        Sharp    // Sharp only
    }

    public enum ConfidenceLevel
    {
        Low,
        Medium,
        High
    }

    public enum AlertType
    {
        NewMovement,           // First significant movement detected
        ConfidenceEscalation,  // Confidence crossed threshold
        SharpActivity,         // Sharp book moved first
        ConsensusFormed,       // Multiple books aligned
        Reversal               // Line moved back
    }

    public enum AlertPriority
    {
        Normal,
        High,
        Urgent
    }

    public enum AlertChannel
    {
        CoreGeneral,      // #odds-alerts (Core + Sharp)
        SharpOnly,        // #sharp-signals (Sharp only)
        DirectMessage     // DM to Sharp subscribers
    }

    public enum SignalOutcome
    {
        Extended,    // Line continued moving in same direction
        Reverted,    // Line moved back
        Stable       // Line stayed within 0.5 points
    }

    public enum SubscriptionTier
    {
        Starter = 0,
        Core = 1,
        Sharp = 2
    }

    /// <summary>
    /// Sport categories (stored as string in DB for flexibility)
    /// </summary>
    public enum SportCategory
    {
        Football,
        Basketball,
        Baseball,
        Hockey,
        Soccer,
        Golf,
        Tennis,
        MMA,
        Boxing,
        Other
    }

    /// <summary>
    /// Period structure for different sports
    /// </summary>
    public enum GamePeriodType
    {
        None,       // No periods (golf, tennis)
        Halves,     // Soccer, college basketball
        Quarters,   // NFL, NBA
        Periods,    // Hockey (3 periods)
        Innings,    // Baseball
        Sets,       // Tennis
        Rounds      // Boxing, MMA
    }

    /// <summary>
    /// Specific game period for market definitions
    /// </summary>
    public enum GamePeriod
    {
        FullGame,
        FirstHalf,
        SecondHalf,
        FirstQuarter,
        SecondQuarter,
        ThirdQuarter,
        FourthQuarter,
        FirstPeriod,
        SecondPeriod,
        ThirdPeriod,
        Overtime
    }

    /// <summary>
    /// Outcome types for market definitions
    /// </summary>
    public enum OutcomeType
    {
        TeamBased,      // Home/Away outcomes (spreads, moneylines)
        OverUnder,      // Over/Under outcomes (totals, player props)
        YesNo,          // Yes/No outcomes (anytime TD, overtime)
        Named           // Named outcomes (first TD scorer, etc.)
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
}