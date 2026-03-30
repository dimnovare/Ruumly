namespace Ruumly.Backend.Models;

public class BlockedDate
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public SupplierLocation Location { get; set; } = null!;
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
