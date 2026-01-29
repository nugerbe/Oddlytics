using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Data.Configurations;

public class PlayerAliasConfiguration : IEntityTypeConfiguration<PlayerAlias>
{
    public void Configure(EntityTypeBuilder<PlayerAlias> builder)
    {
        builder.ToTable("PlayerAlias");

        builder.HasKey(pa => pa.PlayerAliasId);

        builder.Property(pa => pa.PlayerAliasId)
            .ValueGeneratedOnAdd();

        builder.Property(pa => pa.Alias)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pa => pa.AliasType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(pa => pa.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // Relationships
        builder.HasOne(pa => pa.Player)
            .WithMany(p => p.Aliases)
            .HasForeignKey(pa => pa.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(pa => pa.Alias)
            .HasDatabaseName("IX_PlayerAlias_Alias");

        builder.HasIndex(pa => pa.PlayerId)
            .HasDatabaseName("IX_PlayerAlias_PlayerId");
    }
}
