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
