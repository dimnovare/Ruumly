using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Services.Interfaces;

public interface IContractService
{
    /// <summary>
    /// Renders the HTML template for the given template + booking, substituting all
    /// supported variables. Returns raw HTML string — callers stream it as text/html.
    /// </summary>
    Task<string> RenderAsync(Guid templateId, Guid bookingId, CancellationToken ct = default);

    /// <summary>
    /// Signs a contract for the given booking. Idempotent: returns the existing
    /// SignedContract if one already exists for this booking.
    /// </summary>
    Task<SignedContract> SignAsync(
        SignContractRequest req,
        string              tenantEmail,
        string?             ip,
        CancellationToken   ct = default);
}
