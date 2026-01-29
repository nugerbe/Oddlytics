namespace Sportsdata.Core.Entities;

/// <summary>
/// Represents alternate names/nicknames for teams
/// Used for entity resolution from user queries
/// </summary>
public class TeamAlias
{
    public int TeamAliasId { get; set; }
    public int TeamId { get; set; }
    public required string Alias { get; set; }
    public required string AliasType { get; set; } // Nickname, Abbreviation, City, Former
    public bool IsPrimary { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Team Team { get; set; } = null!;
}

/// <summary>
/// Common alias types for teams
/// </summary>
public static class TeamAliasTypes
{
    public const string Nickname = "Nickname";
    public const string Abbreviation = "Abbreviation";
    public const string City = "City";
    public const string Former = "Former";
}
