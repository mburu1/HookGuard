using System.Globalization;
using System.Text;
using FluentAssertions;
using HookGuard.Domain.Signatures;
using HookGuard.UnitTests.Fakes;

namespace HookGuard.UnitTests.Signatures;

public class SignatureVerifierTests
{
    private const string Secret = "test-signing-secret-32-characters-long";
    private const long Now = 1_800_000_000;
    private const string Body = """{"TransID":"QK123456","Amount":1500}""";

    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    [Fact]
    public void Accepts_a_fresh_payload_signed_with_the_source_secret()
    {
        var verification = Verify(SignedHeaders(Body, Now));

        verification.IsValid.Should().BeTrue();
        verification.Failure.Should().Be(SignatureFailure.None);
        verification.Skew.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Rejects_a_tampered_payload_signed_with_the_same_secret()
    {
        // The signature is valid for the original bytes and nothing else.
        var headers = SignedHeaders(Body, Now);

        Verify(headers, """{"TransID":"QK123456","Amount":9999}""")
            .Failure.Should().Be(SignatureFailure.SignatureMismatch);
    }

    [Fact]
    public void Rejects_a_payload_signed_with_the_wrong_secret()
    {
        var headers = SignedHeaders(Body, Now, "a-different-secret-of-similar-length");

        Verify(headers).Failure.Should().Be(SignatureFailure.SignatureMismatch);
    }

    [Fact]
    public void Rejects_a_signature_that_matches_every_byte_but_the_last()
    {
        // Proves the comparison is not a prefix match: a search that gets 63 of 64 hex characters
        // right still fails.
        var headers = SignedHeaders(Body, Now);
        var signature = headers[SignatureHeaders.Signature];
        headers[SignatureHeaders.Signature] = string.Concat(
            signature.AsSpan(0, signature.Length - 1),
            (signature[^1] == '0' ? "1" : "0").AsSpan());

        Verify(headers).Failure.Should().Be(SignatureFailure.SignatureMismatch);
    }

    [Fact]
    public void Accepts_an_uppercase_signature()
    {
        var headers = SignedHeaders(Body, Now);
        headers[SignatureHeaders.Signature] = headers[SignatureHeaders.Signature].ToUpperInvariant();

        Verify(headers).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(Now - 299)] // just inside the 5 minute window
    [InlineData(Now + 299)]
    public void Accepts_a_timestamp_inside_the_tolerance_window(long signedAt)
    {
        Verify(SignedHeaders(Body, signedAt)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Accepts_a_timestamp_exactly_on_the_tolerance_boundary()
    {
        Verify(SignedHeaders(Body, Now - 300)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(Now - 301)] // a replayed callback from six minutes ago
    [InlineData(Now + 301)] // a provider clock running ahead
    public void Rejects_a_timestamp_outside_the_tolerance_window(long signedAt)
    {
        Verify(SignedHeaders(Body, signedAt)).Failure.Should().Be(SignatureFailure.TimestampOutsideTolerance);
    }

    [Fact]
    public void Rejects_a_missing_timestamp_header()
    {
        var headers = SignedHeaders(Body, Now);
        headers.Remove(SignatureHeaders.Timestamp);

        Verify(headers).Failure.Should().Be(SignatureFailure.MissingTimestamp);
    }

    [Fact]
    public void Rejects_a_missing_signature_header()
    {
        var headers = SignedHeaders(Body, Now);
        headers.Remove(SignatureHeaders.Signature);

        Verify(headers).Failure.Should().Be(SignatureFailure.MissingSignature);
    }

    [Theory]
    [InlineData("not-hex-at-all")] // wrong alphabet
    [InlineData("abc")] // odd number of hex characters, cannot be bytes
    public void Rejects_a_malformed_signature(string signature)
    {
        var headers = SignedHeaders(Body, Now);
        headers[SignatureHeaders.Signature] = signature;

        Verify(headers).Failure.Should().Be(SignatureFailure.MalformedSignature);
    }

    [Fact]
    public void Rejects_a_well_formed_signature_of_the_wrong_length()
    {
        var headers = SignedHeaders(Body, Now);
        headers[SignatureHeaders.Signature] = "abcdef";

        Verify(headers).Failure.Should().Be(SignatureFailure.SignatureMismatch);
    }

    [Fact]
    public void Rejects_a_malformed_timestamp()
    {
        // Correctly signed, so authenticity is not the problem: the request is genuine but the
        // timestamp is not a Unix-seconds value. Reporting a timestamp failure rather than a
        // signature failure keeps the rejection actionable.
        const string Malformed = "yesterday";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SignatureHeaders.Timestamp] = Malformed,
            [SignatureHeaders.Signature] = SignatureVerifier.Sign(Secret, Malformed, Bytes(Body)),
        };

        Verify(headers).Failure.Should().Be(SignatureFailure.MalformedTimestamp);
    }

    [Fact]
    public void Finds_headers_regardless_of_case()
    {
        var headers = SignedHeaders(Body, Now);
        var uppercase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in headers)
        {
            uppercase[key.ToUpperInvariant()] = value;
        }

        Verify(uppercase).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Reports_how_long_ago_the_payload_was_signed()
    {
        var verification = Verify(SignedHeaders(Body, Now - 120));

        verification.Skew.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Signature_covers_the_exact_bytes_including_an_empty_body()
    {
        var headers = SignedHeaders(string.Empty, Now);

        Verify(headers, string.Empty).IsValid.Should().BeTrue();
        Verify(headers, "x").Failure.Should().Be(SignatureFailure.SignatureMismatch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_secret_argument(string secret)
    {
        var act = () => new SignatureVerifier(FakeClock.At(Now)).Verify(secret, SignedHeaders(Body, Now), Bytes(Body), Tolerance);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Produces_a_lowercase_hex_signature_over_timestamp_dot_body()
    {
        var timestamp = Now.ToString(CultureInfo.InvariantCulture);

        var signature = SignatureVerifier.Sign(Secret, timestamp, Bytes(Body));

        signature.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        signature.Should().Be(SignatureVerifier.Sign(Secret, timestamp, Bytes(Body)), "signing is deterministic");
        signature.Should().NotBe(SignatureVerifier.Sign(Secret, (Now + 1).ToString(CultureInfo.InvariantCulture), Bytes(Body)));
    }

    private static SignatureVerification Verify(IReadOnlyDictionary<string, string> headers, string body = Body) =>
        new SignatureVerifier(FakeClock.At(Now)).Verify(Secret, headers, Bytes(body), Tolerance);

    private static Dictionary<string, string> SignedHeaders(string body, long signedAt, string secret = Secret)
    {
        var timestamp = signedAt.ToString(CultureInfo.InvariantCulture);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SignatureHeaders.Timestamp] = timestamp,
            [SignatureHeaders.Signature] = SignatureVerifier.Sign(secret, timestamp, Bytes(body)),
        };
    }

    private static byte[] Bytes(string body) => Encoding.UTF8.GetBytes(body);
}
