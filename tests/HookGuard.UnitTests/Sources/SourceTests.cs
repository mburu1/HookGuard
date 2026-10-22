using FluentAssertions;
using HookGuard.Domain.Sources;

namespace HookGuard.UnitTests.Sources;

public class SourceTests
{
    private static readonly Guid SourceId = Guid.Parse("0191f0a0-0000-7000-8000-000000000002");
    private static readonly Guid TenantId = Guid.Parse("0191f0a0-0000-7000-8000-000000000003");

    [Fact]
    public void Defaults_to_the_timestamp_body_hmac_scheme_and_a_five_minute_tolerance()
    {
        var source = Create();

        source.SignatureScheme.Should().Be(SignatureScheme.TimestampBodyHmacSha256);
        source.Tolerance.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Keeps_the_secret_in_plaintext_in_memory()
    {
        // Encryption at rest is an Infrastructure concern; the domain never sees ciphertext.
        Create(secret: "plaintext-secret-in-memory-only").SigningSecret
            .Should().Be("plaintext-secret-in-memory-only");
    }

    [Fact]
    public void Rejects_an_empty_tenant()
    {
        var act = () => Create(tenantId: Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_an_empty_id()
    {
        var act = () => Create(id: Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name)
    {
        var act = () => Create(name: name);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("123456789012345")]
    public void Rejects_a_signing_secret_that_is_too_short_to_be_secret(string secret)
    {
        var act = () => Create(secret: secret);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(1801)]
    public void Rejects_a_tolerance_outside_the_permitted_window(int toleranceSeconds)
    {
        var act = () => Create(tolerance: TimeSpan.FromSeconds(toleranceSeconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Trims_the_name()
    {
        Create(name: "  M-Pesa Daraja  ").Name.Should().Be("M-Pesa Daraja");
    }

    [Fact]
    public void Resolves_a_known_scheme_name_regardless_of_case()
    {
        SignatureScheme.FromName("TIMESTAMP-BODY-HMAC-SHA256").Should().Be(SignatureScheme.TimestampBodyHmacSha256);
    }

    [Fact]
    public void Rejects_an_unknown_scheme_name()
    {
        var act = () => SignatureScheme.FromName("carrier-pigeon");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Source Create(
        Guid? id = null,
        Guid? tenantId = null,
        string name = "M-Pesa Daraja",
        string secret = "test-signing-secret-32-characters-long",
        SignatureScheme? scheme = null,
        TimeSpan? tolerance = null) =>
        new(
            id ?? SourceId,
            tenantId ?? TenantId,
            name,
            secret,
            scheme ?? SignatureScheme.TimestampBodyHmacSha256,
            tolerance ?? TimeSpan.FromMinutes(5));
}
