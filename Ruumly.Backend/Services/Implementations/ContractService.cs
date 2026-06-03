using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class ContractService(RuumlyDbContext db) : IContractService
{
    private const int MaxSignatureSizeBytes = 500 * 1024; // 500 KB

    public async Task<string> RenderAsync(
        Guid templateId, Guid bookingId, CancellationToken ct = default)
    {
        var template = await db.ContractTemplates.FindAsync([templateId], ct)
            ?? throw new KeyNotFoundException($"Contract template {templateId} not found.");

        var booking = await db.Bookings
            .Include(b => b.User)
            .Include(b => b.Listing)
                .ThenInclude(l => l.Location)
            .Include(b => b.Supplier)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct)
            ?? throw new KeyNotFoundException($"Booking {bookingId} not found.");

        if (template.SupplierId != booking.SupplierId)
            throw new InvalidOperationException(
                "Contract template does not belong to the booking's supplier.");

        var listing  = booking.Listing;
        var user     = booking.User;
        var supplier = booking.Supplier;

        // Prefer location-linked address (avoids stale snapshot columns).
        var address = (listing.Location?.Address ?? listing.Address)
                    + ", "
                    + (listing.Location?.City ?? listing.City);

        var html = template.HtmlTemplate
            .Replace("{{tenant_name}}",    HttpUtility.HtmlEncode(user?.Name   ?? ""))
            .Replace("{{tenant_id_code}}", "")           // filled at sign-time by request
            .Replace("{{unit_title}}",     HttpUtility.HtmlEncode(listing.Title))
            .Replace("{{unit_address}}",   HttpUtility.HtmlEncode(address))
            .Replace("{{price}}",          "€" + listing.PriceFrom.ToString("0.##"))
            .Replace("{{price_unit}}",     HttpUtility.HtmlEncode(listing.PriceUnit))
            .Replace("{{start_date}}",     booking.StartDate.ToString("dd.MM.yyyy"))
            .Replace("{{signed_date}}",    DateTime.UtcNow.ToString("dd.MM.yyyy"))
            .Replace("{{supplier_name}}",  HttpUtility.HtmlEncode(supplier?.Name ?? ""));

        return html;
    }

    public async Task<SignedContract> SignAsync(
        SignContractRequest req,
        string              tenantEmail,
        string?             ip,
        CancellationToken   ct = default)
    {
        // Idempotent: return existing contract if already signed.
        var existing = await db.SignedContracts
            .FirstOrDefaultAsync(c => c.BookingId == req.BookingId, ct);
        if (existing is not null)
            return existing;

        // Validate signature data URL
        const string expectedPrefix = "data:image/png;base64,";
        if (!req.SignatureDataUrl.StartsWith(expectedPrefix, StringComparison.Ordinal))
            throw new ArgumentException("SignatureDataUrl must be a data:image/png;base64,... string.");
        if (req.SignatureDataUrl.Length > MaxSignatureSizeBytes)
            throw new ArgumentException("Signature image is too large (max 500 KB).");

        // Render the contract with tenant-specific values substituted
        var rendered = await RenderAsync(req.ContractTemplateId, req.BookingId, ct);

        // Re-substitute tenant_name and tenant_id_code with the actual sign-time values.
        // HTML-encode to prevent injection if a template uses these in an HTML context.
        rendered = rendered
            .Replace("{{tenant_name}}",    HttpUtility.HtmlEncode(req.TenantName))
            .Replace("{{tenant_id_code}}", HttpUtility.HtmlEncode(req.TenantIdCode ?? ""));

        // Compute SHA-256 of rendered HTML for tamper-evidence (canvas path).
        var renderedHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rendered));
        var renderedHash      = Convert.ToHexString(renderedHashBytes).ToLowerInvariant();

        var signed = new SignedContract
        {
            BookingId          = req.BookingId,
            ContractTemplateId = req.ContractTemplateId,
            RenderedHtml       = rendered,
            RenderedHtmlHash   = renderedHash,
            SignatureDataUrl   = req.SignatureDataUrl,
            TenantName         = req.TenantName,
            TenantIdCode       = req.TenantIdCode,
            TenantEmail        = tenantEmail,
            SignedFromIp       = ip,
            SigningMethod      = "canvas",
            Status             = "completed",
            SignedAt           = DateTime.UtcNow,
            CreatedAt          = DateTime.UtcNow,
        };

        db.SignedContracts.Add(signed);
        await db.SaveChangesAsync(ct);
        return signed;
    }
}
