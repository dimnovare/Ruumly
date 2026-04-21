namespace Ruumly.Backend.DTOs.Requests;

public record UpdateOrderLeadRequest(
    string?   LeadStatus    = null,
    DateTime? LastContactAt = null,
    string?   ProviderNotes = null
);
