using System.ComponentModel.DataAnnotations;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

/// <summary>
/// A trust-and-safety claim raised against a booking/order: damage, a no-show, or a
/// deposit-return disagreement. Deliberately lightweight — it captures the claim,
/// optional evidence, and an admin resolution. SupplierId is denormalised so the
/// admin queue can be filtered/scoped per partner without a join.
/// </summary>
public class Dispute
{
    public Guid Id { get; set; }

    // A dispute always concerns a booking and/or its order. Both are optional at the
    // type level so an admin can also log an out-of-band claim, but the raise endpoint
    // requires at least one and derives SupplierId from it.
    public Guid? BookingId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid SupplierId { get; set; }

    public Guid? RaisedByUserId { get; set; }      // null = admin-logged
    public UserRole RaisedByRole { get; set; }

    public DisputeType   Type   { get; set; }
    public DisputeStatus Status { get; set; } = DisputeStatus.Open;

    [MaxLength(150)]  public string  Subject      { get; set; } = string.Empty;
    [MaxLength(4000)] public string  Description  { get; set; } = string.Empty;
    [MaxLength(120)]  public string  ContactEmail { get; set; } = string.Empty;

    public decimal? AmountClaimed { get; set; }    // deposit / damage amount, if any

    // JSON array of evidence references ({ label, url } or freeform notes). Kept as a
    // string to avoid a child table for an MVP feature.
    public string EvidenceJson { get; set; } = "[]";

    [MaxLength(4000)] public string? AdminNotes { get; set; }
    [MaxLength(2000)] public string? Resolution { get; set; }

    public DateTime  CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
