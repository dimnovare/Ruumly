namespace Ruumly.Backend.DTOs.Responses;

/// <summary>
/// What an UNAUTHENTICATED visitor to /{lang}/claim/{slug} is allowed to see:
/// only facts already printed on the public partner page. No contact email, no
/// phone, and no hint (masked or otherwise) about which address is on file.
/// </summary>
public record PublicClaimProfileDto(
    string Slug,
    string Name,
    string? City,
    string Country,
    List<string> ServiceTypes,
    bool IsClaimed);

/// <summary>
/// The single, constant answer to "send me the link" — returned for a matching
/// address, a non-matching address and an address belonging to a different
/// supplier alike. If this ever varies by case, the endpoint becomes an oracle
/// for the scraped contact data it is meant to protect.
/// </summary>
public record ClaimRequestAcceptedDto(bool Received, string Message);

/// <summary>
/// The values a verified claimant may edit, plus their claim session. Only ever
/// returned to a caller holding a valid magic-link or session token.
/// </summary>
public record ClaimEditableProfileDto(
    string Slug,
    string Name,
    string? ContactPhone,
    string? ContactEmail,
    string? WebsiteUrl,
    string? Address,
    string? City,
    string Country,
    List<string> ServiceTypes,
    string? Description,
    decimal? PriceFrom,
    string? PriceUnit,
    string? PriceNote,
    DateTime? ClaimedAt);

/// <summary>Result of redeeming a magic link: the scoped session and the form's initial values.</summary>
public record ClaimSessionDto(
    string SessionToken,
    DateTime ExpiresAt,
    ClaimEditableProfileDto Profile);
