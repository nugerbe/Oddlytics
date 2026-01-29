using FantasyData.Api.Client;
using FantasyData.Api.Client.Model.NFLv3;
using Interfaces = OddsTracker.Core.Interfaces;

namespace OddsTracker.Core.Services.SportClients
{
    public class NflClient(string apiKey) : Interfaces.ISportClient
    {
        private readonly NFLv3ScoresClient _scoresClient = new(apiKey);

        public string SportKey => "americanfootball_nfl";

        public async Task<List<Interfaces.TeamInfo>> GetTeamsAsync()
        {
            var teams = await _scoresClient.GetTeamProfilesAllAsync();
            return [.. teams.Select(t => new Interfaces.TeamInfo
            {
                TeamId = t.TeamID,
                Key = t.Key ?? string.Empty,
                City = t.City ?? string.Empty,
                Name = t.Name ?? string.Empty,
                FullName = t.FullName ?? string.Empty,
                StadiumId = t.StadiumID,
                Conference = t.Conference,
                Division = t.Division
            })];
        }

        public async Task<List<Interfaces.PlayerInfo>> GetPlayersAsync()
        {
            var players = await _scoresClient.GetPlayerDetailsAllAsync();
            return [.. players.Select(p => new Interfaces.PlayerInfo
            {
                PlayerId = p.PlayerID,
                Name = p.Name ?? string.Empty,
                ShortName = p.ShortName,
                Team = p.Team,
                Position = p.Position,
                Status = p.Status,
                Jersey = p.Number
            })];
        }

        public async Task<List<Interfaces.StadiumInfo>> GetStadiumsAsync()
        {
            var stadiums = await _scoresClient.GetStadiumsAsync();
            return [.. stadiums.Select(s => new Interfaces.StadiumInfo
            {
                StadiumId = s.StadiumID,
                Name = s.Name ?? string.Empty,
                City = s.City,
                State = s.State,
                Country = s.Country,
                Capacity = s.Capacity,
                Surface = s.PlayingSurface,
                IsDome = s.Type?.Contains("Dome", StringComparison.OrdinalIgnoreCase)
            })];
        }

        public async Task<int> GetCurrentSeasonAsync()
        {
            var season = await _scoresClient.GetSeasonCurrentAsync();
            return season ?? DateTime.UtcNow.Year;
        }

        public async Task<List<Interfaces.GameInfo>> GetSeasonGamesAsync(int season)
        {
            var games = await _scoresClient.GetGamesBySeasonLiveFinalAsync(season.ToString());
            return [.. games.Select(MapGame)];
        }

        public async Task<List<Interfaces.GameInfo>> GetCurrentSeasonGamesAsync()
        {
            var season = await GetCurrentSeasonAsync();
            return await GetSeasonGamesAsync(season);
        }

        private static Interfaces.GameInfo MapGame(Score g) => new()
        {
            // Score class uses ScoreID, not GameID
            GameId = g.ScoreID,
            Season = g.Season,
            SeasonType = g.SeasonType.ToString(),
            Status = g.Status,
            DateTime = g.Date, // Score uses Date property
            HomeTeam = g.HomeTeam,
            AwayTeam = g.AwayTeam,
            HomeScore = g.HomeScore,
            AwayScore = g.AwayScore,
            StadiumId = g.StadiumID,
            // Score uses IsOver, not IsGameOver
            IsCompleted = g.IsOver
        };
    }
}
