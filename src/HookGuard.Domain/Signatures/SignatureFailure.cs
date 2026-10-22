namespace HookGuard.Domain.Signatures;

/// <summary>
/// Why a signature was rejected. Every value maps to the same <c>401</c> response; the distinction
/// exists for the audit log and for tests to assert a precise reason.
/// </summary>
public enum SignatureFailure
{
    None = 0,
    MissingTimestamp,
    MissingSignature,
    SignatureMismatch,
    MalformedSignature,
    MalformedTimestamp,
    TimestampOutsideTolerance,
}
