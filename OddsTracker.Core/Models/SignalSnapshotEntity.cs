namespace OddsTracker.Core.Models
{
    /// <summary>
    /// Database entity for signal snapshots.
    /// </summary>
    public class SignalSnapshotEntity
    {
        public long Id { get; set; }
        public string EventId { get; set; } = string.Empty;
        public MarketType MarketType { get; set; }
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
            MarketType = MarketType,
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
            MarketType = model.MarketType,
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
}
