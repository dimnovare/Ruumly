namespace Ruumly.Backend.Models;

public class ContractTemplate
{
    public Guid     Id           { get; set; } = Guid.NewGuid();
    public Guid     SupplierId   { get; set; }
    public Supplier Supplier     { get; set; } = null!;
    public string   Name         { get; set; } = string.Empty;

    /// <summary>
    /// HTML body of the contract. Supports template variables:
    ///   {{tenant_name}}, {{tenant_id_code}}, {{unit_title}},
    ///   {{unit_address}}, {{price}}, {{price_unit}},
    ///   {{start_date}}, {{signed_date}}, {{supplier_name}}
    /// </summary>
    public string   HtmlTemplate { get; set; } = string.Empty;

    public bool     IsActive     { get; set; } = true;
    public bool     IsDefault    { get; set; } = false;
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt    { get; set; } = DateTime.UtcNow;
}
