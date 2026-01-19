using Microsoft.EntityFrameworkCore;
using static OddsTracker.Core.Models.Entities;

namespace OddsTracker.Core.Data
{
    public class OddsTrackerDbContext(DbContextOptions<OddsTrackerDbContext> options) : DbContext(options)
    {
        public DbSet<SignalSnapshotEntity> SignalSnapshots => Set<SignalSnapshotEntity>();
        public DbSet<UserSubscriptionEntity> UserSubscriptions => Set<UserSubscriptionEntity>();
        public DbSet<SportEntity> Sports => Set<SportEntity>();
        public DbSet<MarketDefinitionEntity> MarketDefinitions => Set<MarketDefinitionEntity>();
        public DbSet<SportMarketEntity> SportMarkets => Set<SportMarketEntity>();
        public DbSet<BookmakerEntity> Bookmakers => Set<BookmakerEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BookmakerEntity>(entity =>
            {
                entity.ToTable("Bookmakers");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Key).IsUnique();
                entity.HasIndex(e => e.Tier);
                entity.HasIndex(e => e.RequiredTier);
                entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
                entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Tier).HasMaxLength(20).IsRequired();
                entity.Property(e => e.RequiredTier).HasMaxLength(20).IsRequired();
                entity.Property(e => e.Region).HasMaxLength(50).IsRequired();
            });

            modelBuilder.Entity<SignalSnapshotEntity>(entity =>
            {
                entity.ToTable("SignalSnapshots");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.EventId)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.LineAtSignal)
                    .HasPrecision(5, 2);

                entity.Property(e => e.ClosingLine)
                    .HasPrecision(5, 2);

                entity.Property(e => e.FirstMoverBook)
                    .HasMaxLength(100);

                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(e => e.EventId);
                entity.HasIndex(e => e.SignalTime);
                entity.HasIndex(e => e.GameTime);
                entity.HasIndex(e => new { e.Outcome, e.GameTime })
                    .HasDatabaseName("IX_SignalSnapshots_PendingOutcomes");
            });

            modelBuilder.Entity<UserSubscriptionEntity>(entity =>
            {
                entity.ToTable("UserSubscriptions");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.StripeCustomerId)
                    .HasMaxLength(100);

                entity.Property(e => e.StripeSubscriptionId)
                    .HasMaxLength(100);

                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(e => e.DiscordUserId)
                    .IsUnique();

                entity.HasIndex(e => e.StripeCustomerId);
            });

            modelBuilder.Entity<SportEntity>(entity =>
            {
                entity.ToTable("Sports");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Key).IsUnique();
                entity.HasIndex(e => e.Category);
                entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
                entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Category).HasMaxLength(50).IsRequired();
                entity.Property(e => e.PeriodType).HasMaxLength(50).IsRequired();
            });

            // MarketDefinition
            modelBuilder.Entity<MarketDefinitionEntity>(entity =>
            {
                entity.ToTable("MarketDefinitions");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Key).IsUnique();
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.IsPlayerProp);
                entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
                entity.Property(e => e.DisplayName).HasMaxLength(150).IsRequired();
                entity.Property(e => e.Category).HasMaxLength(50).IsRequired();
                entity.Property(e => e.OutcomeType).HasMaxLength(50).IsRequired();
                entity.Property(e => e.RequiredTier).HasMaxLength(20).IsRequired();
                entity.Property(e => e.Period).HasMaxLength(50);
                entity.Property(e => e.Description).HasMaxLength(500);
            });


            modelBuilder.Entity<SportMarketEntity>(entity =>
            {
                entity.ToTable("SportMarkets");
                entity.HasKey(e => new { e.SportId, e.MarketDefinitionId });

                entity.HasOne(e => e.Sport)
                    .WithMany(s => s.SportMarkets)
                    .HasForeignKey(e => e.SportId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.MarketDefinition)
                    .WithMany(m => m.SportMarkets)
                    .HasForeignKey(e => e.MarketDefinitionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.MarketDefinitionId);
            });
        }
    }
}