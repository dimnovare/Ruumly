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

/// <summary>
/// Public concierge intake ("tell us what you need, we find the partner").
/// Not tied to any listing — captured as a <c>DemandLead</c> with
/// <c>Source = "concierge"</c> and worked by the admin CRM.
/// Categories accepts the consumer-selectable slugs (case-insensitive):
/// "warehouse" | "moving" | "trailer" | "cleaning" | "vanrental".
/// "packing" and "insurance" are still recognised — they are live in indexed URLs
/// and old clients — but never produce a lead in their own category: packing is
/// routed to moving as an add-on, insurance falls back to Any. See
/// Constants/ServiceCategories.RetainedNotSoldSlugs.
/// </summary>
public record ConciergeRequest(
    string Email,
    string City,
    string? Name = null,
    string? Phone = null,
    List<string>? Categories = null,
    string? ToCity = null,
    DateTime? NeedDate = null,
    string? Details = null,
    string? Language = null
);
