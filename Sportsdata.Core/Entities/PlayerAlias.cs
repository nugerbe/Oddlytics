// ============================================================================
// OddsTracker.Core - Entities
// File: Entities/PlayerAlias.cs
// ============================================================================

namespace Sportsdata.Core.Entities;

/// <summary>
/// Represents alternate names/nicknames for players
/// Used for entity resolution from user queries
/// </summary>
public class PlayerAlias
{
    public int PlayerAliasId { get; set; }
    public int PlayerId { get; set; }
    public required string Alias { get; set; }
    public required string AliasType { get; set; } // Nickname, Spelling, Former
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Player Player { get; set; } = null!;
}

/// <summary>
/// Common alias types for players
/// </summary>
public static class PlayerAliasTypes
{
    public const string Nickname = "Nickname";
    public const string Spelling = "Spelling";
    public const string Former = "Former";
}
