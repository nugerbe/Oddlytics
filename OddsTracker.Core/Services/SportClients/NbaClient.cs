using FantasyData.Api.Client;
using FantasyData.Api.Client.Model.NBA;

namespace OddsTracker.Core.Services.SportClients
{
    public class NbaClient(string apiKey) : Interfaces.ISportClient
    {
        private readonly NBAv3ScoresClient _scoresClient = new(apiKey);

        public string SportKey => "basketball_nba";

        public async Task<List<Interfaces.TeamInfo>> GetTeamsAsync()
        {
            var teams = await _scoresClient.GetTeamProfilesAllAsync();
            return teams.Select(t => new Interfaces.TeamInfo
            {
                TeamId = t.TeamID,
                Key = t.Key ?? string.Empty,
                City = t.City ?? string.Empty,
                Name = t.Name ?? string.Empty,
                FullName = $"{t.City} {t.Name}",
                StadiumId = t.StadiumID,
                Conference = t.Conference,
                Division = t.Division
            }).ToList();
        }

        public async Task<List<Interfaces.PlayerInfo>> GetPlayersAsync()
        {
            var players = await _scoresClient.GetPlayerDetailsByActiveAsync();
            return players.Select(p => new Interfaces.PlayerInfo
            {
                PlayerId = p.PlayerID,
                Name = $"{p.FirstName} {p.LastName}",
                ShortName = p.LastName,
                Team = p.Team,
                Position = p.Position,
                Status = p.Status,
                Jersey = p.Jersey
            }).ToList();
        }

        public async Task<List<Interfaces.StadiumInfo>> GetStadiumsAsync()
        {
            var stadiums = await _scoresClient.GetStadiumsAsync();
            return stadiums.Select(s => new Interfaces.StadiumInfo
            {
                StadiumId = s.StadiumID,
                Name = s.Name ?? string.Empty,
                City = s.City,
                State = s.State,
                Country = s.Country,
                Capacity = s.Capacity,
                IsDome = true // NBA arenas are indoor
            }).ToList();
        }

        public async Task<int> GetCurrentSeasonAsync()
        {
            var season = await _scoresClient.GetSeasonCurrentAsync();
            return season.Year;
        }

        public async Task<List<Interfaces.GameInfo>> GetSeasonGamesAsync(int season)
        {
            var games = await _scoresClient.GetSchedulesAsync(season.ToString());
            return games.Select(MapGame).ToList();
        }

        public async Task<List<Interfaces.GameInfo>> GetCurrentSeasonGamesAsync()
        {
            var season = await GetCurrentSeasonAsync();
            return await GetSeasonGamesAsync(season);
        }

        private static Interfaces.GameInfo MapGame(Game g) => new()
        {
            GameId = g.GameID,
            Season = g.Season,
            SeasonType = g.SeasonType.ToString(),
            Status = g.Status,
            DateTime = g.DateTime,
            HomeTeam = g.HomeTeam,
            AwayTeam = g.AwayTeam,
            HomeScore = g.HomeTeamScore,
            AwayScore = g.AwayTeamScore,
            StadiumId = g.StadiumID,
            IsCompleted = g.IsClosed
        };
    }
}
