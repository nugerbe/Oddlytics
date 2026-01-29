// ============================================================================
// OddsTracker.Core - Entities
// File: Entities/Stadium.cs
// ============================================================================

namespace Sportsdata.Core.Entities;

/// <summary>
/// Represents a stadium/venue where games are played
/// Maps to SportsData.io Stadium endpoint
/// </summary>
public class Stadium
{
    public int StadiumId { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public string? State { get; set; }
    public required string Country { get; set; }
    public int? Capacity { get; set; }
    public string? PlayingSurface { get; set; }
    public decimal? GeoLat { get; set; }
    public decimal? GeoLong { get; set; }
    public string? Type { get; set; } // Outdoor, Dome, RetractableDome
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Team> Teams { get; set; } = [];
}
