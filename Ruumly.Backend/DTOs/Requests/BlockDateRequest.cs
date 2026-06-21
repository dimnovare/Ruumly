namespace Ruumly.Backend.DTOs.Requests;

public record BlockDateRequest(string Date, string? Reason, System.Guid? ListingId = null);
