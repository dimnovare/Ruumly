namespace Ruumly.Backend.Models;

public class PollingLog
{
    public Guid     Id             { get; set; } = Guid.NewGuid();
    public Guid     SupplierId     { get; set; }
    public Supplier Supplier       { get; set; } = null!;
    public DateTime Timestamp      { get; set; } = DateTime.UtcNow;
    /// <summary>"success" | "error" | "skipped"</summary>
    public string   Status         { get; set; } = "success";
    public string?  ErrorMessage   { get; set; }
    /// <summary>How many listing AvailableNow values were updated this run.</summary>
    public int      UnitsRefreshed { get; set; }
    /// <summary>Elapsed milliseconds for the HTTP call.</summary>
    public int      DurationMs     { get; set; }
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
}
