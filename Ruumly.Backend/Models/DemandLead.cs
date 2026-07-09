using System.ComponentModel.DataAnnotations;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class DemandLead
{
    public Guid Id { get; set; }
    [MaxLength(200)] public string Email { get; set; } = string.Empty;
    [MaxLength(100)] public string City { get; set; } = string.Empty;
    public DemandLeadCategory Category { get; set; } = DemandLeadCategory.Any;
    [MaxLength(500)] public string? Query { get; set; }
    public string Language { get; set; } = "et";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DemandLeadStatus Status { get; set; } = DemandLeadStatus.New;
    public string? AdminNotes { get; set; }

    // ── Routing + quote lifecycle (moving/trailer "request a quote") ──────────
    // A lead captured from a specific listing is routed to that listing's partner
    // (SupplierId set); generic demand-capture leads leave SupplierId null and stay
    // in the admin-only CRM. The partner responds with a one-time QuotedPrice.
    public Guid? SupplierId { get; set; }
    public Guid? ListingId { get; set; }
    [MaxLength(120)] public string? Name { get; set; }
    [MaxLength(40)]  public string? Phone { get; set; }
    public decimal? QuotedPrice { get; set; }
    public DateTime? QuotedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    [MaxLength(2000)] public string? ProviderNotes { get; set; }

    // ── Concierge intake (demand-first pivot) ─────────────────────────────────
    // A concierge request is a structured need ("move me from City to ToCity on
    // NeedDate") captured before any listing exists for it. Source tags the intake
    // channel ("concierge"); ContactedAt stamps the first admin touch for
    // first-response metrics.
    [MaxLength(100)]  public string? ToCity { get; set; }
    public DateTime? NeedDate { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    [MaxLength(40)]   public string? Source { get; set; }
    public DateTime? ContactedAt { get; set; }
}
