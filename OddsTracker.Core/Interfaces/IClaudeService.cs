using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IClaudeService
    {
        Task<OddsQueryBase?> ParseQueryAsync(string userMessage);
        Task<string> AnalyzeOddsMovementAsync(OddsBase odds, OddsQueryBase query, TeamSide side);
    }
}
