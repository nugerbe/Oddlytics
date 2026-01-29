namespace Sportsdata.Core.Entities;

/// <summary>
/// Represents a sports team
/// Maps to SportsData.io Team endpoint
/// </summary>
public class Team
{
    public int TeamId { get; set; }
    public int SportId { get; set; }
    public required string Key { get; set; } // Abbreviation (PHI, NE, KC)
    public int PlayerId { get; set; } // Fantasy DST PlayerID
    public required string City { get; set; }
    public required string Name { get; set; } // Mascot (Eagles, Patriots)
    public string? Conference { get; set; }
    public string? Division { get; set; }
    public string? FullName { get; set; }
    public int? StadiumId { get; set; }
    public int? ByeWeek { get; set; }
    
    // Draft positions
    public decimal? AverageDraftPosition { get; set; }
    public decimal? AverageDraftPositionPpr { get; set; }
    public decimal? AverageDraftPosition2Qb { get; set; }
    public decimal? AverageDraftPositionDynasty { get; set; }
    
    // Coaching staff
    public string? HeadCoach { get; set; }
    public string? OffensiveCoordinator { get; set; }
    public string? DefensiveCoordinator { get; set; }
    public string? SpecialTeamsCoach { get; set; }
    
    // Schemes
    public string? OffensiveScheme { get; set; }
    public string? DefensiveScheme { get; set; }
    
    // Upcoming game info
    public int? UpcomingSalary { get; set; }
    public string? UpcomingOpponent { get; set; }
    public int? UpcomingOpponentRank { get; set; }
    public int? UpcomingOpponentPositionRank { get; set; }
    public int? UpcomingFanDuelSalary { get; set; }
    public int? UpcomingDraftKingsSalary { get; set; }
    public int? UpcomingYahooSalary { get; set; }
    
    // Branding (not licensed for public use)
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? TertiaryColor { get; set; }
    public string? QuaternaryColor { get; set; }
    public string? WikipediaLogoUrl { get; set; }
    public string? WikipediaWordMarkUrl { get; set; }
    
    // Global/External IDs
    public int? GlobalTeamId { get; set; }
    public string? DraftKingsName { get; set; }
    public int? DraftKingsPlayerId { get; set; }
    public string? FanDuelName { get; set; }
    public int? FanDuelPlayerId { get; set; }
    public string? FantasyDraftName { get; set; }
    public int? FantasyDraftPlayerId { get; set; }
    public string? YahooName { get; set; }
    public int? YahooPlayerId { get; set; }
    
    // Audit
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Sport Sport { get; set; } = null!;
    public virtual Stadium? Stadium { get; set; }
    public virtual ICollection<Player> Players { get; set; } = [];
    public virtual ICollection<TeamAlias> Aliases { get; set; } = [];
}
