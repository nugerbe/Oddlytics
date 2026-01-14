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
}
