namespace Ruumly.Backend.DTOs.Requests;

public record CreatePaidFeatureRequestRequest(
    Guid PaidFeatureId,
    Guid? ListingId,
    Guid? LocationId,
    string? Message);

public record ActivatePaidFeatureRequest(
    DateTime? StartsAt,
    DateTime? EndsAt,
    string? AdminNotes);

public record DeclinePaidFeatureRequest(
    string? AdminNotes);

// Admin catalog management (create / edit the optional add-on storefront).
// Category/Scope are case-insensitive enum names ("Visibility"/"Listing"/…).
public record UpsertPaidFeatureRequest(
    string? Code,
    string? Name,
    string? Description,
    string? Category,
    string? Scope,
    decimal? PriceAmount,
    string? PriceCurrency,
    string? BillingInterval,
    bool? IsActive,
    int? SortOrder);
