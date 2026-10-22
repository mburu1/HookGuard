using HookGuard.Domain.Destinations;
using HookGuard.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HookGuard.Infrastructure.Persistence.Configurations;

internal sealed class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("destinations");

        builder.HasKey(destination => destination.Id);

        builder.Property(destination => destination.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(destination => destination.SourceId).HasColumnName("source_id").IsRequired();

        builder.Property(destination => destination.Url).HasColumnName("url").HasMaxLength(2048).IsRequired();

        // Named _enc so that adding the protector in M3.4 is a data migration rather than a column
        // rename. Until then this column holds the secret in plaintext.
        builder.Property(destination => destination.SigningSecret)
            .HasColumnName("signing_secret_enc")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(destination => destination.MaxAttempts)
            .HasColumnName("max_attempts")
            .IsRequired();

        builder.Property(destination => destination.Timeout)
            .HasColumnName("timeout_ms")
            .IsRequired()
            .HasConversion(Conversions.Milliseconds);

        builder.Property(destination => destination.IsActive).HasColumnName("is_active").IsRequired();

        // Deleting a source takes its routing configuration with it; a destination without its
        // source has nothing to route and no events of its own.
        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(destination => destination.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
