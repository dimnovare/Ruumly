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
}
