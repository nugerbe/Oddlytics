using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IClaudeService
    {
        Task<OddsQuery?> ParseQueryAsync(string userMessage);
        Task<string> AnalyzeOddsMovementAsync(NormalizedOdds odds, OddsQuery query, TeamSide side);
    }
}
