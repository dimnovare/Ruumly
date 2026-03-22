using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class IntegrationSettings
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public ApprovalMode ApprovalMode { get; set; } = ApprovalMode.Auto;
    public PostingMode PostingMode { get; set; }
    public PostingMode FallbackPostingMode { get; set; }
    public string? MappingProfile { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public string? LastTestResult { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
