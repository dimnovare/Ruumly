using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Ruumly.Backend.Identity;

/// <summary>
/// Orchestrates identity verification sessions for Ruumly's contract signing flow.
///
/// <para>
/// Sessions are stored in <see cref="IDistributedCache"/> (in-memory or Redis depending on
/// configuration) with a 5-minute TTL — no DB migration required. The service selects the
/// correct <see cref="IIdentityVerificationProvider"/> by method name and exposes simple
/// portal-friendly result types.
/// </para>
///
/// <para>
/// When Smart-ID / Mobile-ID env vars are absent, this service is not registered in DI at
/// all — controllers inject it as optional (<c>IdentityVerificationService? identityService</c>).
/// </para>
/// </summary>
public sealed class IdentityVerificationService
{
    private static readonly DistributedCacheEntryOptions SessionCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    private readonly IDistributedCache _cache;
    private readonly IEnumerable<IIdentityVerificationProvider> _providers;
    private readonly ILogger<IdentityVerificationService> _logger;

    public IdentityVerificationService(
        IDistributedCache cache,
        IEnumerable<IIdentityVerificationProvider> providers,
        ILogger<IdentityVerificationService> logger)
    {
        _cache = cache;
        _providers = providers;
        _logger = logger;
    }

    /// <summary>Returns <c>true</c> when Smart-ID is configured.</summary>
    public async Task<bool> IsSmartIdConfiguredAsync()
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == "smart-id");
        return provider is not null && await provider.IsConfiguredAsync();
    }

    /// <summary>Returns <c>true</c> when Mobile-ID is configured.</summary>
    public async Task<bool> IsMobileIdConfiguredAsync()
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == "mobile-id");
        return provider is not null && await provider.IsConfiguredAsync();
    }

    /// <summary>
    /// Starts a new identity verification session.
    /// Returns a <see cref="StartResult"/> carrying the session id and 4-digit verification code.
    /// </summary>
    public async Task<StartResult> StartAsync(
        Guid bookingId,
        string method,
        string personalCode,
        string country,
        string? phoneNumber,
        CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == method);
        if (provider is null)
        {
            throw new InvalidOperationException($"Unknown identity verification method: {method}");
        }

        if (!await provider.IsConfiguredAsync())
        {
            throw new InvalidOperationException(
                $"Identity provider '{method}' is not configured on this server.");
        }

        IdentitySession session;
        try
        {
            session = await provider.StartAuthenticationAsync(personalCode, country, phoneNumber, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to start {Method} session for booking {BookingId}.", method, bookingId);
            throw;
        }

        // Persist session data to distributed cache (5-minute TTL — same as provider session).
        var cached = new CachedSession(
            SessionId: session.SessionId,
            VerificationCode: session.VerificationCode,
            Provider: session.Provider,
            BookingId: bookingId,
            Status: "pending",
            VerifiedName: null,
            PersonalCode: null,
            StartedAt: DateTime.UtcNow,
            ExpiresAt: session.ExpiresAt);

        await _cache.SetStringAsync(
            CacheKey(session.SessionId),
            JsonSerializer.Serialize(cached),
            SessionCacheOptions,
            ct);

        _logger.LogInformation(
            "Identity verification session {SessionId} started (method={Method}, booking={BookingId}).",
            session.SessionId, method, bookingId);

        return new StartResult(
            SessionId: session.SessionId,
            VerificationCode: session.VerificationCode);
    }

    /// <summary>
    /// Polls the provider once and updates the cached session state.
    /// Returns a <see cref="PollResult"/> suitable for the frontend polling loop,
    /// or <c>null</c> when the session is not found (expired or never started).
    /// </summary>
    public async Task<PollResult?> PollAsync(string sessionId, CancellationToken ct = default)
    {
        var raw = await _cache.GetStringAsync(CacheKey(sessionId), ct);
        if (raw is null)
        {
            return null;
        }

        var cached = JsonSerializer.Deserialize<CachedSession>(raw);
        if (cached is null)
        {
            return null;
        }

        // Already in a terminal state — return cached outcome without calling the provider again.
        if (cached.Status is "completed" or "failed" or "expired")
        {
            return new PollResult(
                Status: cached.Status,
                VerifiedName: cached.VerifiedName,
                PersonalCode: cached.PersonalCode);
        }

        // Check TTL against provider expiry.
        if (DateTime.UtcNow > cached.ExpiresAt)
        {
            await UpdateCachedSession(cached with { Status = "expired" }, sessionId, ct);
            return new PollResult(Status: "expired");
        }

        var provider = _providers.FirstOrDefault(p => p.ProviderName == cached.Provider);
        if (provider is null)
        {
            await UpdateCachedSession(cached with { Status = "failed" }, sessionId, ct);
            return new PollResult(Status: "failed");
        }

        IdentityVerificationResult result;
        try
        {
            result = await provider.PollSessionAsync(sessionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Exception polling {Provider} session {SessionId}.", cached.Provider, sessionId);
            result = new IdentityVerificationResult(
                IdentityVerificationStatus.Failed, FailureReason: "Polling error");
        }

        return result.Status switch
        {
            IdentityVerificationStatus.Running or IdentityVerificationStatus.Pending =>
                new PollResult(Status: "pending"),

            IdentityVerificationStatus.Verified => await HandleVerified(cached, result, sessionId, ct),

            IdentityVerificationStatus.Failed =>
                await HandleTerminal(cached, "failed", sessionId, ct),

            IdentityVerificationStatus.Expired =>
                await HandleTerminal(cached, "expired", sessionId, ct),

            _ => new PollResult(Status: "pending"),
        };
    }

    /// <summary>
    /// Returns the cached session for a given session id, or <c>null</c> when not found.
    /// Used by the Sign endpoint to validate that a session is genuinely completed.
    /// </summary>
    public async Task<CachedSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var raw = await _cache.GetStringAsync(CacheKey(sessionId), ct);
        return raw is null ? null : JsonSerializer.Deserialize<CachedSession>(raw);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<PollResult> HandleVerified(
        CachedSession cached,
        IdentityVerificationResult result,
        string sessionId,
        CancellationToken ct)
    {
        var updated = cached with
        {
            Status = "completed",
            VerifiedName = result.VerifiedName,
            // We store the HMAC hash, not the raw personal code. Callers wanting to
            // display the personal code in the UI must pass it themselves — we never
            // echo it back after the initial StartAsync call.
            PersonalCode = null,
        };
        await UpdateCachedSession(updated, sessionId, ct);
        return new PollResult(
            Status: "completed",
            VerifiedName: result.VerifiedName,
            PersonalCode: null);
    }

    private async Task<PollResult> HandleTerminal(
        CachedSession cached,
        string status,
        string sessionId,
        CancellationToken ct)
    {
        await UpdateCachedSession(cached with { Status = status }, sessionId, ct);
        return new PollResult(Status: status);
    }

    private Task UpdateCachedSession(CachedSession updated, string sessionId, CancellationToken ct) =>
        _cache.SetStringAsync(
            CacheKey(sessionId),
            JsonSerializer.Serialize(updated),
            SessionCacheOptions,
            ct);

    private static string CacheKey(string sessionId) => $"identity:session:{sessionId}";
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Data persisted in distributed cache for an in-flight identity session.</summary>
public sealed record CachedSession(
    string SessionId,
    string VerificationCode,
    string Provider,
    Guid BookingId,
    /// <summary>"pending" | "completed" | "failed" | "expired"</summary>
    string Status,
    string? VerifiedName,
    /// <summary>Always null — we never cache the raw personal code.</summary>
    string? PersonalCode,
    DateTime StartedAt,
    DateTime ExpiresAt);

/// <param name="SessionId">Provider session id returned to the client for polling.</param>
/// <param name="VerificationCode">4-digit anti-phishing code to show the user.</param>
public sealed record StartResult(string SessionId, string VerificationCode);

/// <param name="Status">"pending" | "completed" | "failed" | "expired"</param>
/// <param name="VerifiedName">Full name from the certificate, null until completed.</param>
/// <param name="PersonalCode">Null — raw personal codes are never returned by the API.</param>
public sealed record PollResult(string Status, string? VerifiedName = null, string? PersonalCode = null);
