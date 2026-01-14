using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IChartService
    {
        Task<byte[]> GenerateChartAsync(List<NormalizedOdds> odds, OddsQuery query, TeamSide side = TeamSide.Home);
    }
}
