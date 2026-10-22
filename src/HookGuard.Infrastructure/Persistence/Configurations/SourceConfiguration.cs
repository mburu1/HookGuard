using HookGuard.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HookGuard.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sources");

        builder.HasKey(source => source.Id);

        // Ids are generated at the edge (UUIDv7) so inserts keep index locality without a
        // database round trip; the provider must not substitute its own value.
        builder.Property(source => source.Id).HasColumnName("id").ValueGeneratedNever();

        // No foreign key yet: the tenants table arrives with authentication in phase 3.
        builder.Property(source => source.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(source => source.Name).HasColumnName("name").HasMaxLength(200).IsRequired();

        // Named _enc so that adding the protector in M3.4 is a data migration rather than a column
        // rename. Until then this column holds the secret in plaintext.
        builder.Property(source => source.SigningSecret)
            .HasColumnName("signing_secret_enc")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(source => source.SignatureScheme)
            .HasColumnName("signature_scheme")
            .HasMaxLength(64)
            .IsRequired()
            .HasConversion(Conversions.SchemeName);

        builder.Property(source => source.Tolerance)
            .HasColumnName("tolerance_seconds")
            .IsRequired()
            .HasConversion(Conversions.Seconds);
    }
}
