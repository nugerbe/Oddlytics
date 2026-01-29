using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sportsdata.Core.Entities;

namespace Sportsdata.Core.Data.Configurations;

public class SportConfiguration : IEntityTypeConfiguration<Sport>
{
    public void Configure(EntityTypeBuilder<Sport> builder)
    {
        builder.ToTable("Sport");

        builder.HasKey(s => s.SportId);

        builder.Property(s => s.SportId)
            .ValueGeneratedOnAdd();

        builder.Property(s => s.Code)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.HasTeams)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(s => s.UpdatedDate)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // Indexes
        builder.HasIndex(s => s.Code)
            .IsUnique()
            .HasDatabaseName("UQ_Sport_Code");

        // Seed data
        builder.HasData(
            new Sport { SportId = 1, Code = "NFL", Name = "National Football League", HasTeams = true },
            new Sport { SportId = 2, Code = "NBA", Name = "National Basketball Association", HasTeams = true },
            new Sport { SportId = 3, Code = "MLB", Name = "Major League Baseball", HasTeams = true },
            new Sport { SportId = 4, Code = "NHL", Name = "National Hockey League", HasTeams = true },
            new Sport { SportId = 5, Code = "NCAAF", Name = "NCAA Football", HasTeams = true },
            new Sport { SportId = 6, Code = "NCAAB", Name = "NCAA Basketball", HasTeams = true },
            new Sport { SportId = 7, Code = "MMA/UFC", Name = "Mixed Martial Arts", HasTeams = false },
            new Sport { SportId = 8, Code = "PGA", Name = "PGA Golf", HasTeams = false },
            new Sport { SportId = 9, Code = "WNBA", Name = "Women's National Basketball Association", HasTeams = true },
            new Sport { SportId = 10, Code = "SOCCER", Name = "Soccer", HasTeams = true }
        );
    }
}
