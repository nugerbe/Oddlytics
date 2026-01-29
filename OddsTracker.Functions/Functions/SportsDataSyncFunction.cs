using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using OddsTracker.Core.Interfaces;
using Sportsdata.Core.Entities;
using Sportsdata.Core.Repositories.Interfaces;
using System.Net;

namespace OddsTracker.Functions.Functions;

/// <summary>
/// Azure Function to sync sports data from FantasyData API
/// Runs monthly to update teams and players for any sport
/// </summary>
public class SportsDataSyncFunction(
    IUnitOfWork unitOfWork,
    ISportsDataService sportsDataService,
    ILogger<SportsDataSyncFunction> logger)
{
    /// <summary>
    /// Monthly sync of all active sports data
    /// Runs at 2 AM on the 1st of each month
    /// In DEBUG mode, also runs on startup for local testing
    /// </summary>
    [Function("SyncSportsData")]
    public async Task SyncSportsDataAsync(
#if DEBUG
        [TimerTrigger("0 0 2 1 * *", RunOnStartup = true)] TimerInfo timerInfo,
#else
        [TimerTrigger("0 0 2 1 * *")] TimerInfo timerInfo,
#endif
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting sports data sync at {Time}", DateTime.UtcNow);

        try
        {
            var activeSports = await unitOfWork.Sports.GetActiveAsync(cancellationToken);

            foreach (var sport in activeSports)
            {
                await SyncSportDataAsync(sport, cancellationToken);
            }

            logger.LogInformation("Sports data sync completed successfully at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during sports data sync");
            throw;
        }
    }

    /// <summary>
    /// Manual trigger for syncing a specific sport's data
    /// </summary>
    [Function("SyncSportsDataManual")]
    public async Task<HttpResponseData> SyncSportsDataManualAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sync/{sportCode}/{entityType?}")]
        HttpRequestData request,
        string sportCode,
        string? entityType,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Manual sync triggered for sport {SportCode}, entity type {EntityType}", sportCode, entityType ?? "all");

        var sport = await unitOfWork.Sports.GetByCodeAsync(sportCode, cancellationToken);
        if (sport is null)
        {
            logger.LogWarning("Sport {SportCode} not found", sportCode);
            var notFoundResponse = request.CreateResponse(HttpStatusCode.NotFound);
            await notFoundResponse.WriteStringAsync($"Sport '{sportCode}' not found");
            return notFoundResponse;
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            switch (entityType?.ToLower())
            {
                case "stadiums":
                    await SyncStadiumsAsync(sport.Code, cancellationToken);
                    break;
                case "teams":
                    if (sport.HasTeams)
                    {
                        await SyncTeamsAsync(sport, cancellationToken);
                    }
                    else
                    {
                        logger.LogInformation("Sport {SportCode} does not have teams, skipping team sync", sportCode);
                    }
                    break;
                case "players":
                    await SyncPlayersAsync(sport, cancellationToken);
                    break;
                default:
                    await SyncStadiumsAsync(sport.Code, cancellationToken);
                    if (sport.HasTeams)
                    {
                        await SyncTeamsAsync(sport, cancellationToken);
                    }
                    await SyncPlayersAsync(sport, cancellationToken);
                    break;
            }

            await unitOfWork.CommitTransactionAsync(cancellationToken);

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"Sync completed for sport '{sportCode}'");
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during manual sync for sport {SportCode}", sportCode);
            await unitOfWork.RollbackTransactionAsync(cancellationToken);

            var errorResponse = request.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error syncing sport '{sportCode}': {ex.Message}");
            return errorResponse;
        }
    }

    private async Task SyncSportDataAsync(Sport sport, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting data sync for sport {SportCode}", sport.Code);

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            // Sync stadiums first (teams depend on them)
            await SyncStadiumsAsync(sport.Code, cancellationToken);

            // Only sync teams if the sport has teams
            if (sport.HasTeams)
            {
                await SyncTeamsAsync(sport, cancellationToken);
            }
            else
            {
                logger.LogInformation("Sport {SportCode} does not have teams, skipping team sync", sport.Code);
            }

            // Sync players
            await SyncPlayersAsync(sport, cancellationToken);

            await unitOfWork.CommitTransactionAsync(cancellationToken);

            logger.LogInformation("Data sync completed for sport {SportCode}", sport.Code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during data sync for sport {SportCode}", sport.Code);
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task SyncStadiumsAsync(string sportCode, CancellationToken cancellationToken)
    {
        logger.LogInformation("Syncing stadiums for sport {SportCode}...", sportCode);

        var apiStadiums = await sportsDataService.GetStadiumsAsync(sportCode);

        var stadiums = apiStadiums.Select(s => new Stadium
        {
            StadiumId = s.StadiumId,
            Name = s.Name,
            City = s.City ?? "Unknown",
            State = s.State,
            Country = s.Country ?? "USA",
            Capacity = s.Capacity,
            PlayingSurface = s.Surface
        }).ToList();

        var syncedCount = await unitOfWork.Stadiums.BulkUpsertAsync(stadiums, cancellationToken);
        logger.LogInformation("Synced {Count} stadiums for sport {SportCode}", syncedCount, sportCode);
    }

    private async Task SyncTeamsAsync(Sport sport, CancellationToken cancellationToken)
    {
        logger.LogInformation("Syncing teams for sport {SportCode}...", sport.Code);

        var apiTeams = await sportsDataService.GetTeamsAsync(sport.Code);

        var teams = apiTeams.Select(t => new Team
        {
            TeamId = t.TeamId,
            SportId = sport.SportId,
            Key = t.Key,
            City = t.City,
            Name = t.Name,
            FullName = t.FullName,
            StadiumId = t.StadiumId,
            Conference = t.Conference,
            Division = t.Division
        }).ToList();

        var syncedCount = await unitOfWork.Teams.BulkUpsertAsync(teams, cancellationToken);

        // Also create default aliases for team abbreviations and names
        foreach (var team in teams)
        {
            await unitOfWork.Teams.AddAliasAsync(team.TeamId, team.Key, TeamAliasTypes.Abbreviation, cancellationToken);
            await unitOfWork.Teams.AddAliasAsync(team.TeamId, team.Name, TeamAliasTypes.Nickname, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Synced {Count} teams for sport {SportCode}", syncedCount, sport.Code);
    }

    private async Task SyncPlayersAsync(Sport sport, CancellationToken cancellationToken)
    {
        logger.LogInformation("Syncing players for sport {SportCode}...", sport.Code);

        var apiPlayers = await sportsDataService.GetPlayersAsync(sport.Code);

        var players = apiPlayers.Select(p => new Player
        {
            PlayerId = p.PlayerId,
            SportId = sport.SportId,
            Name = p.Name,
            ShortName = p.ShortName,
            Team = p.Team,
            Position = p.Position,
            Status = p.Status,
            Number = p.Jersey,
            Active = p.Status == "Active"
        }).ToList();

        var syncedCount = await unitOfWork.Players.BulkUpsertAsync(players, cancellationToken);

        // Deactivate players no longer in the API response
        var activePlayerIds = players.Select(p => p.PlayerId).ToList();
        var deactivatedCount = await unitOfWork.Players.DeactivatePlayersNotInListAsync(sport.SportId, activePlayerIds, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Synced {Count} players, deactivated {Deactivated} for sport {SportCode}", syncedCount, deactivatedCount, sport.Code);
    }
}
