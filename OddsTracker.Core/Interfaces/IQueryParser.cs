using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IQueryParser
    {
        Task<OddsQuery?> TryParseAsync(string userMessage);
    }
}
