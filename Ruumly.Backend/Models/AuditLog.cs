namespace Ruumly.Backend.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
