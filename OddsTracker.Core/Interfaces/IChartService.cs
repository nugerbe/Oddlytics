using OddsTracker.Core.Models;

namespace OddsTracker.Core.Interfaces
{
    public interface IChartService
    {
        Task<byte[]> GenerateChartAsync(List<OddsBase> odds, OddsQueryBase query, TeamSide side = TeamSide.Home);
    }
}
