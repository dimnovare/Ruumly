using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Services.Interfaces;

public interface ISupplierProfileService
{
    Task<SupplierProfileDto?> GetBySlugAsync(string slug, CancellationToken ct = default);
}
