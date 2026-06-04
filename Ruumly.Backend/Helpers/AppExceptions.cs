namespace Ruumly.Backend.Helpers;

/// <summary>Thrown when a request is authenticated but not permitted (HTTP 403).</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>Thrown when a resource is not found (HTTP 404).</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Thrown when a resource already exists (HTTP 409).</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>
/// Thrown when an incoming webhook JWT signature fails validation.
/// Callers should return HTTP 400 — this is not a transient error and should not be retried.
/// </summary>
public class WebhookSignatureException(string message) : Exception(message);

/// <summary>
/// Thrown by the payment-initiation path when a booking has no completed signed contract.
/// Sign-then-pay: a customer cannot reach Montonio payment without a signed rental agreement.
/// Callers should return HTTP 409 with the machine-readable <see cref="Code"/>.
/// </summary>
public class ContractNotSignedException(string message) : Exception(message)
{
    /// <summary>Machine-readable error code for the frontend.</summary>
    public const string Code = "CONTRACT_NOT_SIGNED";
}
