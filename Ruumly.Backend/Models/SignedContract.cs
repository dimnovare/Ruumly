namespace Ruumly.Backend.Models;

public class SignedContract
{
    public Guid    Id                 { get; set; } = Guid.NewGuid();
    public Guid    BookingId          { get; set; }
    public Booking Booking            { get; set; } = null!;
    public Guid    ContractTemplateId { get; set; }

    /// <summary>Rendered HTML with all variables substituted — immutable snapshot.</summary>
    public string  RenderedHtml       { get; set; } = string.Empty;

    /// <summary>Base64-encoded PNG of the signature drawn by the tenant (null for Dokobit signings).</summary>
    public string  SignatureDataUrl   { get; set; } = string.Empty;

    public string  TenantName        { get; set; } = string.Empty;
    public string? TenantIdCode      { get; set; }
    public string  TenantEmail       { get; set; } = string.Empty;

    /// <summary>Client IP address at signing time — lightweight legal audit trail.</summary>
    public string? SignedFromIp      { get; set; }

    // ─── Dokobit / signing-method fields (added in AddDokobitSigningFields migration) ───

    /// <summary>Dokobit signing token (null for canvas-acknowledgment signings).</summary>
    public string? DokobitSigningToken { get; set; }

    /// <summary>"smartid" | "mobileid" | "idcard" | "canvas"</summary>
    public string SigningMethod { get; set; } = "canvas";

    /// <summary>SHA-256 hex hash of RenderedHtml — tamper-evidence.</summary>
    public string? RenderedHtmlHash { get; set; }

    /// <summary>Identity-verified full name (from Dokobit or null for canvas).</summary>
    public string? VerifiedName { get; set; }

    /// <summary>Identity-verified national ID code (from Dokobit or null for canvas).</summary>
    public string? VerifiedIdCode { get; set; }

    /// <summary>
    /// Signing status. "pending" while Dokobit flow is in progress; "completed" when
    /// signed (canvas always writes "completed" directly). "cancelled" / "error" for
    /// Dokobit terminal failure states.
    /// </summary>
    public string Status { get; set; } = "completed";

    public DateTime SignedAt  { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
