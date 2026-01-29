using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Data.Configurations;

public class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Player");

        builder.HasKey(p => p.PlayerId);

        // PlayerId comes from SportsData.io, not auto-generated
        builder.Property(p => p.PlayerId)
            .ValueGeneratedNever();

        // Basic info
        builder.Property(p => p.Team).HasMaxLength(50);
        builder.Property(p => p.FirstName).HasMaxLength(50);
        builder.Property(p => p.LastName).HasMaxLength(50);
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.ShortName).HasMaxLength(50);
        builder.Property(p => p.Position).HasMaxLength(10);
        builder.Property(p => p.PositionCategory).HasMaxLength(10);
        builder.Property(p => p.FantasyPosition).HasMaxLength(10);
        builder.Property(p => p.Status).HasMaxLength(50);

        // Physical
        builder.Property(p => p.Height).HasMaxLength(10);

        // Personal
        builder.Property(p => p.BirthDateString).HasMaxLength(20);
        builder.Property(p => p.College).HasMaxLength(100);
        builder.Property(p => p.ExperienceString).HasMaxLength(20);

        // Draft
        builder.Property(p => p.CollegeDraftTeam).HasMaxLength(50);

        // Depth chart
        builder.Property(p => p.DepthPositionCategory).HasMaxLength(10);
        builder.Property(p => p.DepthPosition).HasMaxLength(10);

        // Injury
        builder.Property(p => p.InjuryStatus).HasMaxLength(50);
        builder.Property(p => p.InjuryBodyPart).HasMaxLength(50);
        builder.Property(p => p.InjuryNotes).HasMaxLength(500);
        builder.Property(p => p.InjuryPractice).HasMaxLength(50);
        builder.Property(p => p.InjuryPracticeDescription).HasMaxLength(100);

        // Current info
        builder.Property(p => p.CurrentTeam).HasMaxLength(50);
        builder.Property(p => p.CurrentStatus).HasMaxLength(50);
        builder.Property(p => p.UpcomingGameOpponent).HasMaxLength(10);

        // External IDs
        builder.Property(p => p.SportRadarPlayerId).HasMaxLength(50);

        // DFS Names
        builder.Property(p => p.FanDuelName).HasMaxLength(100);
        builder.Property(p => p.DraftKingsName).HasMaxLength(100);
        builder.Property(p => p.YahooName).HasMaxLength(100);
        builder.Property(p => p.FantasyDraftName).HasMaxLength(100);

        // Photos
        builder.Property(p => p.PhotoUrl).HasMaxLength(250);
        builder.Property(p => p.UsaTodayHeadshotUrl).HasMaxLength(250);
        builder.Property(p => p.UsaTodayHeadshotNoBackgroundUrl).HasMaxLength(250);

        // Decimal precision for ADP
        builder.Property(p => p.AverageDraftPosition).HasPrecision(18, 2);

        // Audit
        builder.Property(p => p.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(p => p.UpdatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // Relationships
        builder.HasOne(p => p.Sport)
            .WithMany(s => s.Players)
            .HasForeignKey(p => p.SportId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.TeamNavigation)
            .WithMany(t => t.Players)
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes
        builder.HasIndex(p => p.SportId)
            .HasDatabaseName("IX_Player_SportId");

        builder.HasIndex(p => p.Team)
            .HasDatabaseName("IX_Player_Team");

        builder.HasIndex(p => p.TeamId)
            .HasDatabaseName("IX_Player_TeamId");

        builder.HasIndex(p => p.Name)
            .HasDatabaseName("IX_Player_Name");

        builder.HasIndex(p => p.LastName)
            .HasDatabaseName("IX_Player_LastName");

        builder.HasIndex(p => p.FirstName)
            .HasDatabaseName("IX_Player_FirstName");

        builder.HasIndex(p => p.Position)
            .HasDatabaseName("IX_Player_Position");

        builder.HasIndex(p => p.FantasyPosition)
            .HasDatabaseName("IX_Player_FantasyPosition");

        builder.HasIndex(p => p.Status)
            .HasDatabaseName("IX_Player_Status");

        builder.HasIndex(p => p.Active)
            .HasFilter("[Active] = 1")
            .HasDatabaseName("IX_Player_Active");

        builder.HasIndex(p => p.GlobalPlayerId)
            .HasDatabaseName("IX_Player_GlobalPlayerId");

        builder.HasIndex(p => new { p.SportId, p.LastName })
            .HasDatabaseName("IX_Player_Sport_LastName");

        builder.HasIndex(p => new { p.SportId, p.Active })
            .HasDatabaseName("IX_Player_Sport_Active");

        builder.HasIndex(p => new { p.Team, p.Position })
            .HasDatabaseName("IX_Player_Team_Position");
    }
}
