using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Data.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Team");

        builder.HasKey(t => t.TeamId);

        // TeamId comes from SportsData.io, not auto-generated
        builder.Property(t => t.TeamId)
            .ValueGeneratedNever();

        builder.Property(t => t.Key)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.City)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Conference)
            .HasMaxLength(50);

        builder.Property(t => t.Division)
            .HasMaxLength(50);

        builder.Property(t => t.FullName)
            .HasMaxLength(100);

        // Coaching staff
        builder.Property(t => t.HeadCoach).HasMaxLength(50);
        builder.Property(t => t.OffensiveCoordinator).HasMaxLength(50);
        builder.Property(t => t.DefensiveCoordinator).HasMaxLength(50);
        builder.Property(t => t.SpecialTeamsCoach).HasMaxLength(50);

        // Schemes
        builder.Property(t => t.OffensiveScheme).HasMaxLength(10);
        builder.Property(t => t.DefensiveScheme).HasMaxLength(10);

        // Upcoming opponent
        builder.Property(t => t.UpcomingOpponent).HasMaxLength(10);

        // Colors
        builder.Property(t => t.PrimaryColor).HasMaxLength(6);
        builder.Property(t => t.SecondaryColor).HasMaxLength(6);
        builder.Property(t => t.TertiaryColor).HasMaxLength(6);
        builder.Property(t => t.QuaternaryColor).HasMaxLength(6);

        // URLs
        builder.Property(t => t.WikipediaLogoUrl).HasMaxLength(250);
        builder.Property(t => t.WikipediaWordMarkUrl).HasMaxLength(250);

        // DFS names
        builder.Property(t => t.DraftKingsName).HasMaxLength(50);
        builder.Property(t => t.FanDuelName).HasMaxLength(50);
        builder.Property(t => t.FantasyDraftName).HasMaxLength(50);
        builder.Property(t => t.YahooName).HasMaxLength(50);

        // Decimal precision for ADP
        builder.Property(t => t.AverageDraftPosition).HasPrecision(18, 2);
        builder.Property(t => t.AverageDraftPositionPpr).HasPrecision(18, 2);
        builder.Property(t => t.AverageDraftPosition2Qb).HasPrecision(18, 2);
        builder.Property(t => t.AverageDraftPositionDynasty).HasPrecision(18, 2);

        // Audit
        builder.Property(t => t.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(t => t.UpdatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // Relationships
        builder.HasOne(t => t.Sport)
            .WithMany(s => s.Teams)
            .HasForeignKey(t => t.SportId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Stadium)
            .WithMany(s => s.Teams)
            .HasForeignKey(t => t.StadiumId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes
        builder.HasIndex(t => t.SportId)
            .HasDatabaseName("IX_Team_SportId");

        builder.HasIndex(t => t.Key)
            .HasDatabaseName("IX_Team_Key");

        builder.HasIndex(t => t.Name)
            .HasDatabaseName("IX_Team_Name");

        builder.HasIndex(t => t.FullName)
            .HasDatabaseName("IX_Team_FullName");

        builder.HasIndex(t => t.City)
            .HasDatabaseName("IX_Team_City");

        builder.HasIndex(t => t.GlobalTeamId)
            .HasDatabaseName("IX_Team_GlobalTeamId");

        builder.HasIndex(t => new { t.SportId, t.Key })
            .HasDatabaseName("IX_Team_Sport_Key");
    }
}
