namespace Ruumly.Backend.Models;

/// <summary>
/// Distinguishes the two contract-template authoring paths:
///   <see cref="Html"/> — the legacy admin path: an HTML body with {{tokens}}.
///   <see cref="Docx"/> — the partner self-serve path: a Word .docx uploaded to R2,
///   filled via Open XML and rendered to PDF via Gotenberg before signing.
/// Existing rows default to <see cref="Html"/> so behavior is unchanged.
/// </summary>
public enum ContractTemplateType
{
    Html = 0,
    Docx = 1,
}

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
    /// Nullable now that docx templates carry their body in R2 instead.
    /// </summary>
    public string   HtmlTemplate { get; set; } = string.Empty;

    /// <summary>Html (legacy admin) or Docx (partner self-serve). Default Html.</summary>
    public ContractTemplateType TemplateType { get; set; } = ContractTemplateType.Html;

    /// <summary>R2 key of the uploaded .docx (null for HTML templates).</summary>
    public string?  DocxObjectKey { get; set; }

    /// <summary>JSON array of the {{tokens}} found in the docx at upload time (null for HTML).</summary>
    public string?  DetectedTokens { get; set; }

    public bool     IsActive     { get; set; } = true;
    public bool     IsDefault    { get; set; } = false;
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt    { get; set; } = DateTime.UtcNow;
}
