using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HookGuard.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("deliveries");

        builder.HasKey(delivery => delivery.Id);

        builder.Property(delivery => delivery.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(delivery => delivery.EventId).HasColumnName("event_id").IsRequired();

        builder.Property(delivery => delivery.DestinationId).HasColumnName("destination_id").IsRequired();

        // Stored as the name rather than an ordinal: the partial index below filters on it, and a
        // renamed member would otherwise silently change the meaning of stored rows.
        builder.Property(delivery => delivery.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired()
            .HasConversion(Conversions.StatusName);

        builder.Property(delivery => delivery.AttemptCount).HasColumnName("attempt_count").IsRequired();

        builder.Property(delivery => delivery.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();

        // Null while nothing holds the row, which is what makes "nobody's attempt" and "somebody's
        // attempt, started at this instant" two different states rather than one.
        builder.Property(delivery => delivery.ClaimedAt).HasColumnName("claimed_at");

        builder.Property(delivery => delivery.LastError).HasColumnName("last_error").HasMaxLength(2000);

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(delivery => delivery.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Destination>()
            .WithMany()
            .HasForeignKey(delivery => delivery.DestinationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The scheduler's due-work query: only Pending rows can be waiting for a time to pass, so
        // the index stays small no matter how much history accumulates.
        builder.HasIndex(delivery => delivery.NextAttemptAt)
            .HasDatabaseName("ix_deliveries_due")
            .HasFilter("status IN ('Pending')");

        // The visibility timeout's query, indexed the same way and for the same reason: only rows an
        // attempt is holding can be stuck, and those are a vanishing fraction of the table.
        builder.HasIndex(delivery => delivery.ClaimedAt)
            .HasDatabaseName("ix_deliveries_in_flight")
            .HasFilter("status IN ('InFlight')");
    }
}
