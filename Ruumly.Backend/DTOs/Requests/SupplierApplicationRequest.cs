namespace Ruumly.Backend.DTOs.Requests;

public class SupplierApplicationRequest
{
    public string CompanyName    { get; set; } = string.Empty;
    public string RegistryCode   { get; set; } = string.Empty;
    public string ContactName    { get; set; } = string.Empty;
    public string ContactEmail   { get; set; } = string.Empty;
    public string ContactPhone   { get; set; } = string.Empty;
    public string BusinessType   { get; set; } = string.Empty;
    public string[] ServiceTypes { get; set; } = [];
    public string[] ServiceAreas { get; set; } = [];
    public string? Notes         { get; set; }
}
