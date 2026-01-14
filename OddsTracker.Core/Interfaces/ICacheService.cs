namespace OddsTracker.Core.Interfaces
{
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;
        Task<byte[]?> GetBytesAsync(string key);
        Task SetBytesAsync(string key, byte[] value, TimeSpan? expiry = null);
        bool IsEnabled { get; }
    }
}
