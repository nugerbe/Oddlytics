using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Data.Configurations;

public class TeamAliasConfiguration : IEntityTypeConfiguration<TeamAlias>
{
    public void Configure(EntityTypeBuilder<TeamAlias> builder)
    {
        builder.ToTable("TeamAlias");

        builder.HasKey(ta => ta.TeamAliasId);

        builder.Property(ta => ta.TeamAliasId)
            .ValueGeneratedOnAdd();

        builder.Property(ta => ta.Alias)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ta => ta.AliasType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(ta => ta.IsPrimary)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(ta => ta.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // Relationships
        builder.HasOne(ta => ta.Team)
            .WithMany(t => t.Aliases)
            .HasForeignKey(ta => ta.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(ta => ta.Alias)
            .IsUnique()
            .HasDatabaseName("UQ_TeamAlias_Alias");

        builder.HasIndex(ta => ta.TeamId)
            .HasDatabaseName("IX_TeamAlias_TeamId");
    }
}
