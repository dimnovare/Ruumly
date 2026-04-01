namespace Ruumly.Backend.DTOs.Requests;

public record ChangeTierRequest(string Tier, Guid? SupplierId = null);
