namespace Sportsdata.Core.Entities;

/// <summary>
/// Represents a sport category (NFL, NBA, MLB, etc.)
/// </summary>
public class Sport
{
    public int SportId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public bool HasTeams { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Team> Teams { get; set; } = [];
    public virtual ICollection<Player> Players { get; set; } = [];
}
