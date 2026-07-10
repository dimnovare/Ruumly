using System.ComponentModel.DataAnnotations;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

/// <summary>
/// One availability-request email sent to a provider for a
/// <see cref="DemandLead"/> ("a customer near {city} needs {category} — can
/// you take it?"). The customer's identity is never shared — the admin
/// brokers the introduction. Status is updated manually from the provider's
/// reply; <see cref="SentTo"/> snapshots the address so history survives
/// later contact-email changes.
/// </summary>
public class ProviderOutreach
{
    public Guid Id { get; set; }
    public Guid DemandLeadId { get; set; }
    public DemandLead? DemandLead { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Email snapshot at send time.</summary>
    [MaxLength(200)] public string SentTo { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public ProviderOutreachStatus Status { get; set; } = ProviderOutreachStatus.Sent;
    [MaxLength(2000)] public string? Note { get; set; }
}
