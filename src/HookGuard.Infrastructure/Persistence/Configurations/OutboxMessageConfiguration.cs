using HookGuard.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HookGuard.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(64).IsRequired();

        // Opaque to the database on purpose: the payload is the contract between the relay and the
        // worker, and changing its shape must not require a migration.
        builder.Property(message => message.Payload).HasColumnName("payload").IsRequired();

        builder.Property(message => message.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(message => message.PublishedAt).HasColumnName("published_at");

        // The relay's query: unpublished messages in the order they were written. Publishing is
        // what empties this index, so it stays in proportion to the backlog rather than to history.
        builder.HasIndex(message => message.CreatedAt)
            .HasDatabaseName("ix_outbox_messages_unpublished")
            .HasFilter("published_at IS NULL");
    }
}
