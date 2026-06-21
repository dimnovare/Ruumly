namespace Ruumly.Backend.DTOs.Requests;

public record ContactRequest(
    string Name,
    string Email,
    string Subject,
    string Message,
    string? Language = null
);

/// <summary>
/// Public "request a quote" from a listing detail page (moving/trailer).
/// Routed to the listing's partner as a <c>DemandLead</c>.
/// </summary>
public record QuoteLeadRequest(
    Guid ListingId,
    string Email,
    string? Name = null,
    string? Phone = null,
    string? City = null,
    string? Message = null,
    string? Language = null
);
