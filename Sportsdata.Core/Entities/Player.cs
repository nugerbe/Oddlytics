namespace Sportsdata.Core.Entities;

/// <summary>
/// Represents a player
/// Maps to SportsData.io Player/PlayerBasic endpoint
/// </summary>
public class Player
{
    public int PlayerId { get; set; }
    public int SportId { get; set; }
    public int? TeamId { get; set; }
    public string? Team { get; set; } // Team abbreviation (Key)
    
    // Basic info
    public int? Number { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Name { get; set; } // Full display name
    public string? ShortName { get; set; }
    public string? Position { get; set; }
    public string? PositionCategory { get; set; } // OFF, DEF, ST
    public string? FantasyPosition { get; set; }
    public string? Status { get; set; } // Active, Inactive, Practice Squad
    public bool? Active { get; set; }
    
    // Physical attributes
    public string? Height { get; set; }
    public int? HeightFeet { get; set; }
    public int? HeightInches { get; set; }
    public int? Weight { get; set; }
    
    // Personal info
    public DateTime? BirthDate { get; set; }
    public string? BirthDateString { get; set; }
    public int? Age { get; set; }
    public string? College { get; set; }
    public int? Experience { get; set; }
    public string? ExperienceString { get; set; }
    
    // Draft info
    public string? CollegeDraftTeam { get; set; }
    public int? CollegeDraftYear { get; set; }
    public int? CollegeDraftRound { get; set; }
    public int? CollegeDraftPick { get; set; }
    public bool? IsUndraftedFreeAgent { get; set; }
    
    // Depth chart
    public string? DepthPositionCategory { get; set; }
    public string? DepthPosition { get; set; }
    public int? DepthOrder { get; set; }
    public int? DepthDisplayOrder { get; set; }
    public int? FantasyPositionDepthOrder { get; set; }
    
    // Injury info
    public string? InjuryStatus { get; set; }
    public string? InjuryBodyPart { get; set; }
    public DateTime? InjuryStartDate { get; set; }
    public string? InjuryNotes { get; set; }
    public string? InjuryPractice { get; set; }
    public string? InjuryPracticeDescription { get; set; }
    public bool? DeclaredInactive { get; set; }
    
    // Current team info
    public string? CurrentTeam { get; set; }
    public string? CurrentStatus { get; set; }
    public int? ByeWeek { get; set; }
    public string? UpcomingGameOpponent { get; set; }
    public int? UpcomingGameWeek { get; set; }
    public int? UpcomingOpponentRank { get; set; }
    public int? UpcomingOpponentPositionRank { get; set; }
    
    // DFS Salaries
    public int? UpcomingSalary { get; set; }
    public int? UpcomingFanDuelSalary { get; set; }
    public int? UpcomingDraftKingsSalary { get; set; }
    public int? UpcomingYahooSalary { get; set; }
    
    // Draft positions
    public decimal? AverageDraftPosition { get; set; }
    
    // External IDs
    public int? GlobalTeamId { get; set; }
    public int? GlobalPlayerId { get; set; }
    public int? FantasyAlarmPlayerId { get; set; }
    public string? SportRadarPlayerId { get; set; }
    public int? RotoworldPlayerId { get; set; }
    public int? RotoWirePlayerId { get; set; }
    public int? StatsPlayerId { get; set; }
    public int? SportsDirectPlayerId { get; set; }
    public int? XmlTeamPlayerId { get; set; }
    public int? FanDuelPlayerId { get; set; }
    public int? DraftKingsPlayerId { get; set; }
    public int? YahooPlayerId { get; set; }
    public int? FantasyDraftPlayerId { get; set; }
    public int? UsaTodayPlayerId { get; set; }
    
    // DFS Names
    public string? FanDuelName { get; set; }
    public string? DraftKingsName { get; set; }
    public string? YahooName { get; set; }
    public string? FantasyDraftName { get; set; }
    
    // Photos
    public string? PhotoUrl { get; set; }
    public string? UsaTodayHeadshotUrl { get; set; }
    public string? UsaTodayHeadshotNoBackgroundUrl { get; set; }
    public DateTime? UsaTodayHeadshotUpdated { get; set; }
    public DateTime? UsaTodayHeadshotNoBackgroundUpdated { get; set; }
    
    // Audit
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Sport Sport { get; set; } = null!;
    public virtual Team? TeamNavigation { get; set; }
    public virtual ICollection<PlayerAlias> Aliases { get; set; } = [];
}
