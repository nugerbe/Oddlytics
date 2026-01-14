using Microsoft.EntityFrameworkCore;
using OddsTracker.Core.Models;

namespace OddsTracker.Core.Data
{
    public class OddsTrackerDbContext(DbContextOptions<OddsTrackerDbContext> options) : DbContext(options)
    {
        public DbSet<SignalSnapshotEntity> SignalSnapshots => Set<SignalSnapshotEntity>();
        public DbSet<UserSubscriptionEntity> UserSubscriptions => Set<UserSubscriptionEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
        }
    }
}