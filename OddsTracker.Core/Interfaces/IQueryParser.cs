using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IQueryParser
    {
        Task<OddsQueryBase?> TryParseAsync(string userMessage);
    }
}
