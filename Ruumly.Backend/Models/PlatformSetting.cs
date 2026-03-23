namespace Ruumly.Backend.Models;

public class PlatformSetting
{
    public string    Key       { get; set; } = string.Empty;
    public string    Value     { get; set; } = string.Empty;
    public string?   Note      { get; set; }
    public DateTime  UpdatedAt { get; set; } = DateTime.UtcNow;
    public string    UpdatedBy { get; set; } = string.Empty;
}
