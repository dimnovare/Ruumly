namespace Ruumly.Backend.DTOs.Responses;

/// <summary>Provider display name — the only party detail the quote page shows.</summary>
public sealed record PublicQuoteProviderDto(string Name);

/// <summary>
/// What the provider is quoting for. Deliberately excludes ALL customer PII
/// (name/email/phone) — only the structured ask the customer submitted.
/// </summary>
public sealed record PublicQuoteLeadDto(
    string Category, string City, string? ToCity, DateTime? NeedDate, string? Details,
    /// <summary>
    /// How many photos the customer attached. The page renders that many
    /// &lt;img&gt; tags pointed at /api/quote/{token}/photos/{index} — indexes,
    /// never storage keys, so the private-bucket layout is not published to
    /// anyone holding a quote token.
    /// </summary>
    int PhotoCount = 0);

/// <summary>The provider's already-submitted quote (prefill for "update your quote").</summary>
public sealed record PublicQuoteExistingDto(
    decimal? Amount, string? Unit, string? Availability, string? Note);

/// <summary>
/// GET /api/quote/{token} — the public quote page payload. <c>Closed</c> means
/// the underlying request has ended (booked elsewhere, dismissed or unmatched):
/// the page should show a closed state, and a submit would be rejected 409.
/// </summary>
public sealed record PublicQuoteDto(
    PublicQuoteProviderDto Provider,
    PublicQuoteLeadDto Lead,
    string Currency,
    bool AlreadySubmitted,
    PublicQuoteExistingDto? Existing,
    bool Closed);

/// <summary>POST /api/quote/{token} — thank-you echo of the stored quote.</summary>
public sealed record QuoteSubmittedDto(
    bool Ok, decimal Amount, string? Unit, string? Availability, string? Note);
