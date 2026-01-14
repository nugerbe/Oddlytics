using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddsTracker.Core.Models;
using OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services
{
    /// <summary>
    /// Event-driven alert engine with deduplication, cooldowns, and tier-based routing.
    /// 
    /// <para><b>Deduplication:</b> 1-hour window (configurable). Same market + alert type + confidence level = duplicate.</para>
    /// <para><b>Cooldowns:</b> Urgent=2min, High=5min, Normal=15min (configurable).</para>
    /// <para><b>Tier Routing:</b> Starter=none, Core=CoreGeneral, Sharp=All+DM.</para>
    /// <para><b>Escalation:</b> Tracks previous confidence to only alert when crossing INTO High.</para>
    /// </summary>
    public class AlertEngine(
        ICacheService cache,
        ILogger<AlertEngine> logger,
        IOptions<AlertEngineOptions>? options = null) : IAlertEngine
    {
        private readonly AlertEngineOptions _options = options?.Value ?? new AlertEngineOptions();

        public async Task<MarketAlert?> EvaluateForAlertAsync(MarketFingerprint fingerprint, ConfidenceScore confidence)
        {
            var previousConfidence = await GetPreviousConfidenceLevelAsync(fingerprint.Market.Key);
            var alertType = DetermineAlertType(fingerprint, confidence, previousConfidence);

            if (alertType is null)
            {
                logger.LogDebug("No alert warranted for {Market}", fingerprint.Market.Key);
                await SaveConfidenceLevelAsync(fingerprint.Market.Key, confidence.Level);
                return null;
            }

            var alert = new MarketAlert
            {
                Fingerprint = fingerprint,
                Confidence = confidence,
                Type = alertType.Value,
                Priority = DeterminePriority(alertType.Value, confidence),
                TargetChannels = DetermineChannels(alertType.Value, confidence),
                SendDM = ShouldSendDM(alertType.Value, confidence),
                LastAlertTime = await GetLastAlertTimeAsync($"{fingerprint.Market.Key}:{alertType.Value}:{confidence.Level}")
            };

            return alert;
        }

        public async Task<bool> ShouldSendAlertAsync(MarketAlert alert)
        {
            if (await IsDuplicateAsync(alert))
            {
                logger.LogDebug("Alert deduplicated: {Key}", alert.DedupeKey);
                return false;
            }

            var cooldown = GetCooldownPeriod(alert.Priority);
            if (alert.IsInCooldown(cooldown))
            {
                logger.LogDebug("Alert in cooldown: {Key}, last sent {Time}", alert.DedupeKey, alert.LastAlertTime);
                return false;
            }

            return true;
        }

        public async Task MarkAlertSentAsync(MarketAlert alert)
        {
            var dedupeWindow = TimeSpan.FromMinutes(_options.DedupeWindowMinutes);

            await cache.SetAsync(
                $"alert:dedupe:{alert.DedupeKey}",
                new AlertDedupeEntry { AlertId = alert.AlertId },
                dedupeWindow);

            await cache.SetAsync(
                $"alert:lasttime:{alert.DedupeKey}",
                new AlertTimestampWrapper { Timestamp = DateTime.UtcNow },
                TimeSpan.FromHours(24));

            await SaveConfidenceLevelAsync(alert.Fingerprint.Market.Key, alert.Confidence.Level);

            logger.LogInformation(
                "Alert sent: {Type} for {Market} (Confidence: {Level})",
                alert.Type,
                alert.Fingerprint.Market.Key,
                alert.Confidence.Level);
        }

        private AlertType? DetermineAlertType(
            MarketFingerprint fingerprint,
            ConfidenceScore confidence,
            ConfidenceLevel? previousConfidence)
        {
            // 1. Sharp activity - highest priority
            if (fingerprint.FirstMoverType == BookmakerTier.Sharp &&
                fingerprint.DeltaMagnitude >= _options.MinDeltaForSharpAlert)
                return AlertType.SharpActivity;

            // 2. Confidence escalation - CROSSED into High
            if (confidence.Level == ConfidenceLevel.High &&
                previousConfidence.HasValue &&
                previousConfidence.Value != ConfidenceLevel.High)
                return AlertType.ConfidenceEscalation;

            // 3. Strong consensus formed
            if (fingerprint.ConfirmingBooks >= _options.MinBooksForConsensus &&
                confidence.Level >= ConfidenceLevel.Medium)
                return AlertType.ConsensusFormed;

            // 4. New significant movement
            if (fingerprint.DeltaMagnitude >= _options.MinDeltaForMovementAlert)
                return AlertType.NewMovement;

            // 5. Recent reversal
            if (fingerprint.LastReversalTime.HasValue &&
                (DateTime.UtcNow - fingerprint.LastReversalTime.Value).TotalMinutes < _options.ReversalWindowMinutes)
                return AlertType.Reversal;

            return null;
        }

        private static AlertPriority DeterminePriority(AlertType type, ConfidenceScore confidence) =>
            (type, confidence.Level) switch
            {
                (AlertType.SharpActivity, ConfidenceLevel.High) => AlertPriority.Urgent,
                (AlertType.SharpActivity, _) => AlertPriority.High,
                (AlertType.ConfidenceEscalation, _) => AlertPriority.High,
                (AlertType.ConsensusFormed, ConfidenceLevel.High) => AlertPriority.High,
                (AlertType.Reversal, _) => AlertPriority.High,
                _ => AlertPriority.Normal
            };

        private static List<AlertChannel> DetermineChannels(AlertType type, ConfidenceScore confidence)
        {
            List<AlertChannel> channels = [];

            if (type == AlertType.SharpActivity || confidence.Level == ConfidenceLevel.High)
                channels.Add(AlertChannel.SharpOnly);

            if (confidence.Level >= ConfidenceLevel.Medium)
                channels.Add(AlertChannel.CoreGeneral);

            return channels;
        }

        private static bool ShouldSendDM(AlertType type, ConfidenceScore confidence) =>
            type == AlertType.SharpActivity || confidence.Level == ConfidenceLevel.High;

        private TimeSpan GetCooldownPeriod(AlertPriority priority) =>
            priority switch
            {
                AlertPriority.Urgent => TimeSpan.FromMinutes(_options.UrgentCooldownMinutes),
                AlertPriority.High => TimeSpan.FromMinutes(_options.HighPriorityCooldownMinutes),
                _ => TimeSpan.FromMinutes(_options.DefaultCooldownMinutes)
            };

        private async Task<ConfidenceLevel?> GetPreviousConfidenceLevelAsync(string marketKey)
        {
            var cached = await cache.GetAsync<ConfidenceLevelWrapper>($"alert:prevconfidence:{marketKey}");
            return cached?.Level;
        }

        private async Task SaveConfidenceLevelAsync(string marketKey, ConfidenceLevel level) =>
            await cache.SetAsync(
                $"alert:prevconfidence:{marketKey}",
                new ConfidenceLevelWrapper { Level = level },
                TimeSpan.FromHours(24));

        private async Task<DateTime?> GetLastAlertTimeAsync(string dedupeKey)
        {
            var wrapper = await cache.GetAsync<AlertTimestampWrapper>($"alert:lasttime:{dedupeKey}");
            return wrapper?.Timestamp;
        }

        private async Task<bool> IsDuplicateAsync(MarketAlert alert)
        {
            var existing = await cache.GetAsync<AlertDedupeEntry>($"alert:dedupe:{alert.DedupeKey}");
            return existing is not null;
        }
    }

    // Cache wrapper classes for JSON serialization of value types
    public class ConfidenceLevelWrapper
    {
        public ConfidenceLevel Level { get; set; }
    }

    public class AlertTimestampWrapper
    {
        public DateTime Timestamp { get; set; }
    }

    public class AlertDedupeEntry
    {
        public string AlertId { get; set; } = string.Empty;
    }
}
