namespace Ruumly.Backend.Models;

public class SignedContract
{
    public Guid    Id                 { get; set; } = Guid.NewGuid();
    public Guid    BookingId          { get; set; }
    public Booking Booking            { get; set; } = null!;
    public Guid    ContractTemplateId { get; set; }

    /// <summary>Rendered HTML with all variables substituted — immutable snapshot.</summary>
    public string  RenderedHtml       { get; set; } = string.Empty;

    /// <summary>Base64-encoded PNG of the signature drawn by the tenant.</summary>
    public string  SignatureDataUrl   { get; set; } = string.Empty;

    public string  TenantName        { get; set; } = string.Empty;
    public string? TenantIdCode      { get; set; }
    public string  TenantEmail       { get; set; } = string.Empty;

    /// <summary>Client IP address at signing time — lightweight legal audit trail.</summary>
    public string? SignedFromIp      { get; set; }

    public DateTime SignedAt  { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
