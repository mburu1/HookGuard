using HookGuard.Domain.Events;
using HookGuard.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HookGuard.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("events");

        builder.HasKey(@event => @event.Id);

        builder.Property(@event => @event.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(@event => @event.SourceId).HasColumnName("source_id").IsRequired();

        builder.Property(@event => @event.DedupeKey)
            .HasColumnName("dedupe_key")
            .HasMaxLength(EventDedupeKey.MaxLength)
            .IsRequired();

        builder.Property(@event => @event.Headers)
            .HasColumnName("headers")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(Conversions.Headers, Conversions.HeadersComparer);

        builder.Property(@event => @event.Payload)
            .HasColumnName("payload")
            .HasColumnType("bytea")
            .IsRequired()
            .HasConversion(Conversions.Payload);

        builder.Property(@event => @event.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.Property(@event => @event.SignatureValid).HasColumnName("signature_valid").IsRequired();

        // Deleting a source takes its events with it: an event without its signing secret can never
        // be verified or replayed again, so keeping orphans would be a silent data problem.
        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(@event => @event.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // The deduplication guarantee. Enforced here rather than by a read-then-write check so that
        // two concurrent identical deliveries cannot both insert; the loser gets a unique violation
        // and is acknowledged as a duplicate.
        builder.HasIndex(@event => new { @event.SourceId, @event.DedupeKey })
            .IsUnique()
            .HasDatabaseName("ux_events_source_id_dedupe_key");
    }
}
