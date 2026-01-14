using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;
using System.Net.Http.Json;

namespace OddsTracker.Core.Services
{
    public class WebhookDiscordAlertService : IDiscordAlertService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _webhookUrl;
        private readonly ILogger<WebhookDiscordAlertService> _logger;

        public WebhookDiscordAlertService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<WebhookDiscordAlertService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _webhookUrl = config["Discord:AlertWebhookUrl"];
            _logger = logger;

            if (string.IsNullOrEmpty(_webhookUrl))
            {
                _logger.LogWarning("Discord:AlertWebhookUrl not configured - alerts will be logged only");
            }
        }

        public async Task SendAlertAsync(MarketAlert alert)
        {
            var embed = BuildEmbed(alert);

            if (string.IsNullOrEmpty(_webhookUrl))
            {
                _logger.LogInformation(
                    "ALERT (no webhook): {Type} - {Market} - Confidence: {Score}",
                    alert.Type,
                    alert.Fingerprint.Market.Key,
                    alert.Confidence.Score);
                return;
            }

            var payload = new WebhookPayload
            {
                Username = "OddsTracker",
                Embeds = [embed]
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(_webhookUrl, payload);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation(
                    "Alert sent via webhook: {Type} for {Market}",
                    alert.Type,
                    alert.Fingerprint.Market.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send webhook alert");
                throw;
            }
        }

        private static WebhookEmbed BuildEmbed(MarketAlert alert)
        {
            var market = alert.Fingerprint.Market;
            var marketName = market.MarketType.ToDisplayName();
            var gameName = $"{market.AwayTeam} @ {market.HomeTeam}";

            var emoji = alert.Type switch
            {
                AlertType.SharpActivity => "🦈",
                AlertType.ConfidenceEscalation => "📈",
                AlertType.ConsensusFormed => "🤝",
                AlertType.NewMovement => "🔄",
                AlertType.Reversal => "↩️",
                _ => "🔔"
            };

            var color = alert.Priority switch
            {
                AlertPriority.Urgent => 0xFF0000,   // Red
                AlertPriority.High => 0xFFA500,     // Orange
                AlertPriority.Normal => 0x0000FF,   // Blue
                _ => 0xD3D3D3                        // Light grey
            };

            var fields = new List<WebhookField>
        {
            new() { Name = "Game", Value = gameName, Inline = false },
            new() { Name = "Market", Value = marketName, Inline = true },
            new() { Name = "Confidence", Value = $"{alert.Confidence.Score}/100 ({alert.Confidence.Level})", Inline = true },
            new() { Name = "Priority", Value = alert.Priority.ToString(), Inline = true }
        };

            if (!string.IsNullOrEmpty(alert.Fingerprint.FirstMoverBook))
            {
                fields.Add(new WebhookField
                {
                    Name = "First Mover",
                    Value = $"{alert.Fingerprint.FirstMoverBook} ({alert.Fingerprint.FirstMoverType})",
                    Inline = true
                });
            }

            fields.Add(new WebhookField
            {
                Name = "Consensus Line",
                Value = alert.Fingerprint.ConsensusLine.ToString("F1"),
                Inline = true
            });

            fields.Add(new WebhookField
            {
                Name = "Movement",
                Value = $"{alert.Fingerprint.DeltaMagnitude:F1} pts",
                Inline = true
            });

            if (market.CommenceTime > DateTime.MinValue)
            {
                var timeUntilGame = market.CommenceTime - DateTime.UtcNow;
                var gameTimeStr = timeUntilGame.TotalHours switch
                {
                    < 1 => $"Starts in {timeUntilGame.TotalMinutes:F0} min",
                    < 24 => $"Starts in {timeUntilGame.TotalHours:F1} hrs",
                    _ => market.CommenceTime.ToString("MMM d, h:mm tt") + " UTC"
                };
                fields.Add(new WebhookField { Name = "Game Time", Value = gameTimeStr, Inline = true });
            }

            return new WebhookEmbed
            {
                Title = $"{emoji} {alert.Type}",
                Description = alert.Confidence.Explanation,
                Color = color,
                Fields = fields,
                Timestamp = DateTime.UtcNow.ToString("o")
            };
        }
    }
}