using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddsTracker.Core.Interfaces;
using OddsTracker.Core.Models;

namespace OddsTracker
{
    /// <summary>
    /// Discord bot service for handling user interactions.
    /// </summary>
    public class DiscordBotService : BackgroundService
    {
        private readonly DiscordSocketClient _client;
        private readonly IOddsOrchestrator _orchestrator;
        private readonly ISubscriptionManager _subscriptionManager;
        private readonly ILogger<DiscordBotService> _logger;
        private readonly DiscordBotOptions _options;

        public DiscordBotService(
            IOptions<DiscordBotOptions> options,
            IOddsOrchestrator orchestrator,
            ISubscriptionManager subscriptionManager,
            ILogger<DiscordBotService> logger)
        {
            _options = options.Value;
            _orchestrator = orchestrator;
            _subscriptionManager = subscriptionManager;
            _logger = logger;

            if (string.IsNullOrEmpty(_options.Token))
                throw new InvalidOperationException("DiscordToken not configured");

            var socketConfig = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds |
                                GatewayIntents.GuildMessages |
                                GatewayIntents.GuildMembers |
                                GatewayIntents.MessageContent |
                                GatewayIntents.DirectMessages
            };

            _client = new DiscordSocketClient(socketConfig);
            _client.Log += LogAsync;
            _client.MessageReceived += MessageReceivedAsync;
            _client.Ready += ReadyAsync;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _client.LoginAsync(TokenType.Bot, _options.Token);
            await _client.StartAsync();

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _client.StopAsync();
            await base.StopAsync(cancellationToken);
        }

        private Task ReadyAsync()
        {
            _logger.LogInformation("Discord bot is connected as {User}", _client.CurrentUser.Username);
            _logger.LogInformation("Guild: {GuildId}, Channels configured: Bot={Bot}, Alerts={Alerts}, Sharp={Sharp}",
                _options.GuildId,
                _options.OddsBotChannelId,
                _options.OddsAlertsChannelId,
                _options.SharpSignalsChannelId);
            return Task.CompletedTask;
        }

        private Task LogAsync(LogMessage log)
        {
            var logLevel = log.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Debug,
                LogSeverity.Debug => LogLevel.Trace,
                _ => LogLevel.Information
            };

            _logger.Log(logLevel, log.Exception, "[Discord] {Message}", log.Message);
            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            var isMentioned = message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id);
            var isDm = message.Channel is IPrivateChannel;

            if (!isMentioned && !isDm) return;

            var content = message.Content;
            if (isMentioned)
            {
                content = content.Replace($"<@{_client.CurrentUser.Id}>", "").Trim();
                content = content.Replace($"<@!{_client.CurrentUser.Id}>", "").Trim();
            }

            // Get user's subscription tier
            var userId = message.Author.Id;
            var subscription = await _subscriptionManager.GetOrCreateSubscriptionAsync(userId);

            // Check rate limit
            if (!await _subscriptionManager.CanPerformQueryAsync(userId))
            {
                var limit = GetQueryLimit(subscription.Tier);
                await ReplyToMessageAsync(message,
                    $"⚠️ You've reached your daily query limit ({limit} queries for {subscription.Tier} tier).\n" +
                    "Upgrade your subscription for more queries!");
                return;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                await ReplyToMessageAsync(message, GetHelpMessage(subscription.Tier));
                return;
            }

            _logger.LogInformation("Query from {User} (Tier: {Tier}): {Content}",
                message.Author.Username, subscription.Tier, content);

            using var typing = message.Channel.EnterTypingState();

            try
            {
                var result = await _orchestrator.ProcessQueryAsync(content, subscription.Tier);

                // Record the query
                await _subscriptionManager.RecordQueryAsync(userId);

                if (!result.Success || result.Charts is null || result.Charts.Count == 0)
                {
                    await ReplyToMessageAsync(message, result.ErrorMessage ?? "Something went wrong.");
                    return;
                }

                var isFirstChart = true;
                foreach (var chart in result.Charts)
                {
                    using var stream = new MemoryStream(chart.ImageData);

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle($"📊 {chart.Title}")
                        .WithDescription(isFirstChart ? result.GameDescription : chart.Description)
                        .WithColor(GetTierColor(subscription.Tier))
                        .WithImageUrl($"attachment://{GetSafeFilename(chart.Title)}.png")
                        .WithFooter($"Data from The Odds API • {subscription.Tier} tier")
                        .WithCurrentTimestamp();

                    if (!string.IsNullOrEmpty(chart.Analysis))
                    {
                        var analysisText = chart.Analysis.Length > 1024
                            ? chart.Analysis[..1021] + "..."
                            : chart.Analysis;
                        embedBuilder.AddField("📈 Analysis", analysisText);
                    }

                    var embed = embedBuilder.Build();
                    var filename = $"{GetSafeFilename(chart.Title)}.png";

                    await message.Channel.SendFileAsync(
                        stream,
                        filename,
                        embed: embed,
                        messageReference: new MessageReference(message.Id)
                    );

                    isFirstChart = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                await ReplyToMessageAsync(message, "Sorry, something went wrong processing your request.");
            }
        }

        public async Task SendAlertAsync(MarketAlert alert)
        {
            var embed = BuildAlertEmbed(alert);

            // Send to appropriate channels based on alert targets
            foreach (var channel in alert.TargetChannels)
            {
                var channelId = channel switch
                {
                    AlertChannel.CoreGeneral => _options.OddsAlertsChannelId,
                    AlertChannel.SharpOnly => _options.SharpSignalsChannelId,
                    _ => 0UL
                };

                if (channelId == 0) continue;

                var discordChannel = _client.GetChannel(channelId) as ITextChannel;
                if (discordChannel is null)
                {
                    _logger.LogWarning("Channel {ChannelId} not found for alert", channelId);
                    continue;
                }

                try
                {
                    await discordChannel.SendMessageAsync(embed: embed);
                    _logger.LogInformation("Alert sent to channel {Channel}: {Type}",
                        discordChannel.Name, alert.Type);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send alert to channel {ChannelId}", channelId);
                }
            }

            // Send DM if flagged
            if (alert.SendDM)
            {
                await SendAlertDMsAsync(alert, embed);
            }
        }

        private async Task SendAlertDMsAsync(MarketAlert alert, Embed embed)
        {
            // Get all Sharp tier users who should receive DMs
            var guild = _client.GetGuild(_options.GuildId);
            if (guild is null)
            {
                _logger.LogWarning("Guild {GuildId} not found", _options.GuildId);
                return;
            }

            var sharpRole = guild.GetRole(_options.SharpRoleId);
            if (sharpRole is null)
            {
                _logger.LogWarning("Sharp role {RoleId} not found", _options.SharpRoleId);
                return;
            }

            foreach (var member in sharpRole.Members)
            {
                try
                {
                    var dmChannel = await member.CreateDMChannelAsync();
                    await dmChannel.SendMessageAsync(
                        text: $"🚨 **{GetAlertTypeEmoji(alert.Type)} {alert.Type} Alert**",
                        embed: embed);
                    _logger.LogDebug("DM sent to {User}", member.Username);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to DM user {UserId}", member.Id);
                }
            }
        }

        public async Task SendDMAsync(ulong userId, string message, Embed? embed = null)
        {
            try
            {
                var user = await _client.GetUserAsync(userId);
                if (user is null)
                {
                    _logger.LogWarning("User {UserId} not found", userId);
                    return;
                }

                var dmChannel = await user.CreateDMChannelAsync();
                await dmChannel.SendMessageAsync(text: message, embed: embed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send DM to user {UserId}", userId);
            }
        }

        public async Task AssignRoleAsync(ulong userId, SubscriptionTier tier)
        {
            var guild = _client.GetGuild(_options.GuildId);
            if (guild is null)
            {
                _logger.LogWarning("Guild {GuildId} not found", _options.GuildId);
                return;
            }

            var member = guild.GetUser(userId);
            if (member is null)
            {
                _logger.LogWarning("Member {UserId} not found in guild", userId);
                return;
            }

            // Get all tier roles
            var starterRole = guild.GetRole(_options.StarterRoleId);
            var coreRole = guild.GetRole(_options.CoreRoleId);
            var sharpRole = guild.GetRole(_options.SharpRoleId);

            // Remove all tier roles first
            var rolesToRemove = new List<SocketRole>();
            if (starterRole is not null && member.Roles.Contains(starterRole))
                rolesToRemove.Add(starterRole);
            if (coreRole is not null && member.Roles.Contains(coreRole))
                rolesToRemove.Add(coreRole);
            if (sharpRole is not null && member.Roles.Contains(sharpRole))
                rolesToRemove.Add(sharpRole);

            if (rolesToRemove.Count > 0)
                await member.RemoveRolesAsync(rolesToRemove);

            // Add the appropriate role
            var roleToAdd = tier switch
            {
                SubscriptionTier.Starter => starterRole,
                SubscriptionTier.Core => coreRole,
                SubscriptionTier.Sharp => sharpRole,
                _ => starterRole
            };

            if (roleToAdd is not null)
            {
                await member.AddRoleAsync(roleToAdd);
                _logger.LogInformation("Assigned {Role} role to user {UserId}", tier, userId);
            }
        }

        private static Embed BuildAlertEmbed(MarketAlert alert)
        {
            var emoji = GetAlertTypeEmoji(alert.Type);
            var color = GetAlertPriorityColor(alert.Priority);
            var market = alert.Fingerprint.Market;

            // Format market type as readable name
            var marketName = market.MarketType.DisplayName;

            // Format game as "Away @ Home"
            var gameName = $"{market.AwayTeam} @ {market.HomeTeam}";

            var builder = new EmbedBuilder()
                .WithTitle($"{emoji} {alert.Type}")
                .WithColor(color)
                .WithDescription(alert.Confidence.Explanation)
                .WithCurrentTimestamp();

            // Game info first
            builder.AddField("Game", gameName, inline: false);
            builder.AddField("Market", marketName, inline: true);
            builder.AddField("Confidence", $"{alert.Confidence.Score}/100 ({alert.Confidence.Level})", inline: true);
            builder.AddField("Priority", alert.Priority.ToString(), inline: true);

            if (!string.IsNullOrEmpty(alert.Fingerprint.FirstMoverBook))
            {
                builder.AddField("First Mover",
                    $"{alert.Fingerprint.FirstMoverBook} ({alert.Fingerprint.FirstMoverType})",
                    inline: true);
            }

            builder.AddField("Consensus Line", alert.Fingerprint.ConsensusLine.ToString("F1"), inline: true);
            builder.AddField("Movement", $"{alert.Fingerprint.DeltaMagnitude:F1} pts", inline: true);

            // Add game time if available
            if (market.CommenceTime > DateTime.MinValue)
            {
                var timeUntilGame = market.CommenceTime - DateTime.UtcNow;
                var gameTimeStr = timeUntilGame.TotalHours switch
                {
                    < 1 => $"Starts in {timeUntilGame.TotalMinutes:F0} min",
                    < 24 => $"Starts in {timeUntilGame.TotalHours:F1} hrs",
                    _ => market.CommenceTime.ToString("MMM d, h:mm tt") + " UTC"
                };
                builder.AddField("Game Time", gameTimeStr, inline: true);
            }

            return builder.Build();
        }

        private static string GetAlertTypeEmoji(AlertType type) => type switch
        {
            AlertType.SharpActivity => "🦈",
            AlertType.ConfidenceEscalation => "📈",
            AlertType.ConsensusFormed => "🤝",
            AlertType.NewMovement => "🔄",
            AlertType.Reversal => "↩️",
            _ => "🔔"
        };

        private static Color GetAlertPriorityColor(AlertPriority priority) => priority switch
        {
            AlertPriority.Urgent => Color.Red,
            AlertPriority.High => Color.Orange,
            AlertPriority.Normal => Color.Blue,
            _ => Color.LightGrey
        };

        private static Color GetTierColor(SubscriptionTier tier) => tier switch
        {
            SubscriptionTier.Sharp => Color.Gold,
            SubscriptionTier.Core => Color.Blue,
            _ => Color.LightGrey
        };

        private static int GetQueryLimit(SubscriptionTier tier) => tier switch
        {
            SubscriptionTier.Starter => 10,
            SubscriptionTier.Core => 50,
            SubscriptionTier.Sharp => int.MaxValue,
            _ => 10
        };

        private static string GetHelpMessage(SubscriptionTier tier)
        {
            var baseHelp =
                "Hey! Ask me about NFL odds movement. Try something like:\n" +
                "• `Show me Chiefs spread movement`\n" +
                "• `Bills at Chiefs moneyline`\n" +
                "• `Eagles vs Cowboys total over the past week`\n" +
                "• `49ers spread last 5 days`";

            return tier switch
            {
                SubscriptionTier.Sharp =>
                    baseHelp + "\n\n🦈 **Sharp tier**: Unlimited queries, DM alerts, full historical access",
                SubscriptionTier.Core =>
                    baseHelp + "\n\n💙 **Core tier**: 50 queries/day, movement alerts, 7-day history",
                _ =>
                    baseHelp + "\n\n⭐ **Starter tier**: 10 queries/day, 1-day history\n" +
                    "Upgrade for more features!"
            };
        }

        private static async Task ReplyToMessageAsync(SocketMessage originalMessage, string text)
        {
            await originalMessage.Channel.SendMessageAsync(
                text,
                messageReference: new MessageReference(originalMessage.Id)
            );
        }

        private static string GetSafeFilename(string title)
        {
            var safe = title
                .Replace(" ", "_")
                .Replace("/", "-")
                .Replace("\\", "-")
                .Replace("(", "")
                .Replace(")", "")
                .ToLowerInvariant();

            return string.Concat(safe.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
        }
    }
}