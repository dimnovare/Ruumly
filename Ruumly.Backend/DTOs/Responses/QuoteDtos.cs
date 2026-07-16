namespace Ruumly.Backend.DTOs.Responses;

/// <summary>Provider display name — the only party detail the quote page shows.</summary>
public sealed record PublicQuoteProviderDto(string Name);

/// <summary>
/// What the provider is quoting for. Deliberately excludes ALL customer PII
/// (name/email/phone) — only the structured ask the customer submitted.
/// </summary>
public sealed record PublicQuoteLeadDto(
    string Category, string City, string? ToCity, DateTime? NeedDate, string? Details);

/// <summary>The provider's already-submitted quote (prefill for "update your quote").</summary>
public sealed record PublicQuoteExistingDto(
    decimal? Amount, string? Unit, string? Availability, string? Note);

/// <summary>GET /api/quote/{token} — the public quote page payload.</summary>
public sealed record PublicQuoteDto(
    PublicQuoteProviderDto Provider,
    PublicQuoteLeadDto Lead,
    string Currency,
    bool AlreadySubmitted,
    PublicQuoteExistingDto? Existing);

/// <summary>POST /api/quote/{token} — thank-you echo of the stored quote.</summary>
public sealed record QuoteSubmittedDto(
    bool Ok, decimal Amount, string? Unit, string? Availability, string? Note);
