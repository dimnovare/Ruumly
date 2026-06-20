using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class ContractService(
    RuumlyDbContext db,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<ContractService> logger) : IContractService
{
    private const int MaxSignatureSizeBytes = 500 * 1024; // 500 KB

    public async Task<string> RenderAsync(
        Guid templateId, Guid bookingId, CancellationToken ct = default)
    {
        var booking = await db.Bookings
            .Include(b => b.User)
            .Include(b => b.Listing)
                .ThenInclude(l => l.Location)
            .Include(b => b.Supplier)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct)
            ?? throw new KeyNotFoundException($"Booking {bookingId} not found.");

        var hours = await ResolvePaymentConditionHoursAsync(ct);

        // ── Platform-default fallback ────────────────────────────────────────────
        // The sentinel id is not a real ContractTemplate row; render the in-code default whose
        // body already includes {{payment_condition_clause}}. No supplier-mismatch check applies.
        if (templateId == PlatformDefaultContract.TemplateId)
        {
            var values = ContractTokenVocabulary.BuildValues(booking, paymentConditionHours: hours);
            return PlatformDefaultContract.RenderHtml(values);
        }

        var template = await db.ContractTemplates.FindAsync([templateId], ct)
            ?? throw new KeyNotFoundException($"Contract template {templateId} not found.");

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

        // L4: always bind the conditional-on-payment clause, even when the provider's HTML template
        // omits it. A signed-but-unpaid contract must state it binds no one. Mirror the docx path,
        // which appends the same clause via AppendClause.
        html += "<hr/><p><strong>Conditional upon payment:</strong> "
              + HttpUtility.HtmlEncode(ContractTokenVocabulary.PaymentConditionClause(hours))
              + "</p>";

        return html;
    }

    /// <summary>
    /// Reads the configured sign-then-pay window (hours) from PlatformSettings, falling back to 24.
    /// Same key the StaleBookingCleanupJob uses, so the contract clause and the auto-void safeguard
    /// stay in lockstep. Mirrors ContractController.ResolvePaymentConditionHoursAsync.
    /// </summary>
    private async Task<int> ResolvePaymentConditionHoursAsync(CancellationToken ct)
    {
        var value = await db.PlatformSettings
            .Where(s => s.Key == Jobs.StaleBookingCleanupJob.ExpiryHoursSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return value is not null && int.TryParse(value, out var hours) && hours > 0
            ? hours
            : 24;
    }

    /// <summary>
    /// Validates a canvas signature data URL: correct prefix, decodable base64, real
    /// PNG header, sane IHDR dimensions (rejects a 1x1/degenerate PNG), and a size
    /// ceiling. A full-size blank canvas (the type-your-name a11y path) still passes.
    /// </summary>
    private static void ValidateCanvasSignature(string dataUrl)
    {
        const string prefix = "data:image/png;base64,";
        if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException("SignatureDataUrl must be a data:image/png;base64,... string.");
        if (dataUrl.Length > MaxSignatureSizeBytes)
            throw new ArgumentException("Signature image is too large (max 500 KB).");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[prefix.Length..]); }
        catch (FormatException) { throw new ArgumentException("Signature is not valid base64 PNG data."); }

        ReadOnlySpan<byte> pngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(pngMagic))
            throw new ArgumentException("Signature must be a valid PNG image.");

        // IHDR width/height are big-endian 32-bit ints at byte offsets 16 and 20.
        int width  = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        if (width < 50 || height < 20)
            throw new ArgumentException("Signature image is too small to be a valid signature.");
    }

    /// <summary>
    /// Builds the appended signature block (drawn image + self-declared signer metadata)
    /// for the rendered contract snapshot. Explicitly labelled self-declared / not
    /// identity-verified, matching the free canvas method.
    /// </summary>
    private static string BuildSignatureBlock(SignContractRequest req, string? ip)
    {
        var name   = HttpUtility.HtmlEncode(req.TenantName);
        var idPart = string.IsNullOrWhiteSpace(req.TenantIdCode)
            ? ""
            : " (" + HttpUtility.HtmlEncode(req.TenantIdCode) + ")";
        var ipPart = string.IsNullOrWhiteSpace(ip) ? "" : ", IP " + HttpUtility.HtmlEncode(ip);
        var when   = DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm");
        // SignatureDataUrl is validated as a PNG data URL above; safe to embed.
        return "<hr/><div style=\"margin-top:24px\">"
             + "<p style=\"font-size:13px;color:#555;margin:0 0 6px\"><strong>Signature</strong> "
             + "(self-declared, not identity-verified):</p>"
             + "<img src=\"" + req.SignatureDataUrl + "\" alt=\"Signature\" "
             + "style=\"max-width:320px;border:1px solid #ddd;border-radius:6px;background:#fff\"/>"
             + "<p style=\"font-size:12px;color:#777;margin:8px 0 0\">Signed by " + name + idPart
             + ", " + when + " UTC" + ipPart + "</p></div>";
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

        // Validate the drawn signature image server-side (prefix + real PNG header +
        // sane dimensions + size ceiling). Rejects the 1x1 / arbitrary-bytes payload
        // while still accepting a full-size blank canvas (the type-your-name a11y path).
        ValidateCanvasSignature(req.SignatureDataUrl);

        // Render the contract with tenant-specific values substituted
        var rendered = await RenderAsync(req.ContractTemplateId, req.BookingId, ct);

        // Re-substitute tenant_name and tenant_id_code with the actual sign-time values.
        // HTML-encode to prevent injection if a template uses these in an HTML context.
        rendered = rendered
            .Replace("{{tenant_name}}",    HttpUtility.HtmlEncode(req.TenantName))
            .Replace("{{tenant_id_code}}", HttpUtility.HtmlEncode(req.TenantIdCode ?? ""));

        // Embed the actual drawn signature + signer metadata INTO the contract snapshot,
        // so the document a tenant/partner downloads visibly carries the signature
        // (previously the PNG was stored separately and never shown on the artifact).
        rendered += BuildSignatureBlock(req, ip);

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

        // Send confirmation email — never fail the signing transaction if email fails
        try
        {
            var booking = await db.Bookings
                .Include(b => b.User)
                .Include(b => b.Listing)
                .FirstOrDefaultAsync(b => b.Id == req.BookingId, ct);

            if (booking is not null && !string.IsNullOrWhiteSpace(tenantEmail))
            {
                var lang       = booking.User?.Language ?? "et";
                var t          = EmailTranslations.For(lang);
                var appUrl     = configuration["AppUrl"] ?? "https://ruumly.eu";
                var downloadUrl = $"{appUrl}/account?tab=bookings";
                var shortId    = booking.Id.ToString()[..8].ToUpper();
                var subject    = $"Ruumly — {(lang == "et" ? "Leping allkirjastatud" : lang == "ru" ? "Договор подписан" : lang == "lv" ? "Līgums parakstīts" : lang == "lt" ? "Sutartis pasirašyta" : "Contract signed")} #{shortId}";
                var body       =
                    $"{t.BookingConfirmGreeting} {req.TenantName},\n\n" +
                    (lang == "et" ? "Teie leping on edukalt allkirjastatud."
                   : lang == "ru" ? "Ваш договор успешно подписан."
                   : lang == "lv" ? "Jūsu līgums ir veiksmīgi parakstīts."
                   : lang == "lt" ? "Jūsų sutartis sėkmingai pasirašyta."
                   : "Your contract has been successfully signed.") +
                    $"\n\n{t.BookingConfirmService}: {booking.Listing?.Title ?? shortId}" +
                    $"\n\n{t.BookingStatusViewLink}: {downloadUrl}\n\n" +
                    $"Ruumly\ninfo@ruumly.eu";

                await emailSender.SendAsync(tenantEmail, subject, body);
                logger.LogInformation(
                    "Contract signed confirmation email sent to {Email} for booking {Id}",
                    tenantEmail, req.BookingId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send contract signed email to {Email} for booking {Id}",
                tenantEmail, req.BookingId);
            Sentry.SentrySdk.CaptureException(ex);
        }

        return signed;
    }
}
