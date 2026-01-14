namespace OddsTracker.Core.Interfaces
{
    /// <summary>
    /// Discord client interface for sending messages
    /// </summary>
    public interface IDiscordClient
    {
        Task SendMessageAsync(ulong channelId, string message);
        Task SendDMAsync(ulong userId, string message);
    }
}
