using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Data.Configurations;

public class StadiumConfiguration : IEntityTypeConfiguration<Stadium>
{
    public void Configure(EntityTypeBuilder<Stadium> builder)
    {
        builder.ToTable("Stadium");

        builder.HasKey(s => s.StadiumId);

        // StadiumId comes from SportsData.io, not auto-generated
        builder.Property(s => s.StadiumId)
            .ValueGeneratedNever();

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.City)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.State)
            .HasMaxLength(10);

        builder.Property(s => s.Country)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(s => s.PlayingSurface)
            .HasMaxLength(50);

        builder.Property(s => s.GeoLat)
            .HasPrecision(18, 8);

        builder.Property(s => s.GeoLong)
            .HasPrecision(18, 8);

        builder.Property(s => s.Type)
            .HasMaxLength(50);

        builder.Property(s => s.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(s => s.UpdatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // Indexes
        builder.HasIndex(s => s.Name)
            .HasDatabaseName("IX_Stadium_Name");

        builder.HasIndex(s => s.City)
            .HasDatabaseName("IX_Stadium_City");
    }
}
