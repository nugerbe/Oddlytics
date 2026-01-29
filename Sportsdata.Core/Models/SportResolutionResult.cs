namespace Sportsdata.Core.Models;

/// <summary>
/// Result from sport entity resolution
/// Returned by usp_ResolveSportFromEntity stored procedure
/// </summary>
public class SportResolutionResult
{
    public required string EntityType { get; set; } // Team or Player
    public required string EntityName { get; set; }
    public required string SportCode { get; set; }
    public required string SportName { get; set; }
    public int SportId { get; set; }
    public int? TeamId { get; set; }
    public int? PlayerId { get; set; }
    public required string MatchType { get; set; }
    public decimal Confidence { get; set; }
    
    /// <summary>
    /// Returns true if this is a high-confidence match (>= 0.85)
    /// </summary>
    public bool IsHighConfidence => Confidence >= 0.85m;
    
    /// <summary>
    /// Returns true if this is a team match
    /// </summary>
    public bool IsTeam => EntityType.Equals("Team", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// Returns true if this is a player match
    /// </summary>
    public bool IsPlayer => EntityType.Equals("Player", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Entity type enumeration
/// </summary>
public enum EntityType
{
    Team,
    Player
}

/// <summary>
/// Match type enumeration for resolution results
/// </summary>
public enum MatchType
{
    ExactKeyMatch,
    AliasMatch,
    ExactNameMatch,
    ExactPlayerMatch,
    PlayerAliasMatch,
    LastNameMatch,
    PartialTeamMatch,
    PartialPlayerMatch,
    NoMatch
}
