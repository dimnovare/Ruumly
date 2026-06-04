using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Identity;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Provider self-serve contract templating (design spec §7). A partner uploads their own
/// Word <c>.docx</c> with <c>{{tokens}}</c>, we validate the tokens against the known
/// vocabulary, they preview a filled sample PDF, then activate it as their supplier's
/// template. Scoped to the caller's supplier; Admin may act for any supplier via
/// <c>?supplierId=</c>.
/// </summary>
[ApiController]
[Route("api/provider/contract-template")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderContractTemplateController(
    RuumlyDbContext db,
    IStorageService storageService,
    IContractDocumentService docService,
    IGotenbergClient gotenberg,
    ILogger<ProviderContractTemplateController> logger) : ControllerBase
{
    private const long MaxDocxBytes = 10 * 1024 * 1024; // 10 MB

    // ─── List ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null) return SupplierContextError();

        var templates = await db.ContractTemplates
            .Where(t => t.SupplierId == supplierId.Value)
            .OrderByDescending(t => t.IsActive)
            .ThenByDescending(t => t.UpdatedAt)
            .ToListAsync();

        var activeId = templates.FirstOrDefault(t => t.IsActive)?.Id;

        return Ok(new
        {
            templates = templates.Select(t => new
            {
                id             = t.Id,
                name           = t.Name,
                templateType   = t.TemplateType.ToString().ToLowerInvariant(),
                isActive       = t.IsActive,
                isDefault      = t.IsDefault,
                detectedTokens = DeserializeTokens(t.DetectedTokens),
                updatedAt      = t.UpdatedAt.ToString("o"),
            }),
            activeId,
            vocabulary = ContractTokenVocabulary.Vocabulary
                .Select(v => new { token = v.Token, label = v.Label }),
        });
    }

    // ─── Upload ────────────────────────────────────────────────────────────────

    [HttpPost]
    [EnableRateLimiting("user")]
    [RequestSizeLimit(MaxDocxBytes)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, [FromForm] string? name)
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null) return SupplierContextError();

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "A .docx file is required." });
        if (file.Length > MaxDocxBytes)
            return BadRequest(new { error = "File is too large (max 10 MB)." });
        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .docx files are supported." });

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        // Extract + validate tokens (run-merge aware).
        var detected = docService.DiscoverTokens(bytes);
        if (detected.Count == 0)
        {
            // Either no tokens or not a real docx — reject non-docx, allow token-less docs.
            if (!LooksLikeDocx(bytes))
                return BadRequest(new { error = "The file is not a valid .docx document." });
        }

        var unknown  = ContractTokenVocabulary.UnknownTokens(detected);
        var missing  = ContractTokenVocabulary.MissingCoreTokens(detected);

        // Store the docx to R2 under a stable key for later read-back.
        var objectName = $"contract-templates/{supplierId.Value:N}/{Guid.NewGuid():N}.docx";
        StoredObject stored;
        using (var ms = new MemoryStream(bytes))
        {
            stored = await storageService.UploadWithKeyAsync(
                ms, objectName,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }

        var template = new ContractTemplate
        {
            SupplierId     = supplierId.Value,
            Name           = string.IsNullOrWhiteSpace(name) ? file.FileName : name!.Trim(),
            TemplateType   = ContractTemplateType.Docx,
            DocxObjectKey  = stored.Key,
            DetectedTokens = JsonSerializer.Serialize(detected),
            HtmlTemplate   = string.Empty,
            IsActive       = false,   // not active until explicitly activated
            IsDefault      = false,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };
        db.ContractTemplates.Add(template);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Provider uploaded docx contract template {TemplateId} for supplier {SupplierId} ({Tokens} tokens, {Unknown} unknown)",
            template.Id, supplierId, detected.Count, unknown.Count);

        return Ok(new
        {
            templateId     = template.Id,
            detectedTokens = detected.Select(t => new { token = t, recognized = ContractTokenVocabulary.IsKnown(t) }),
            unknownTokens  = unknown,
            warnings       = missing.Count > 0
                ? new[] { $"Template does not use these recommended tokens: {string.Join(", ", missing.Select(m => "{{" + m + "}}"))}" }
                : Array.Empty<string>(),
            vocabulary     = ContractTokenVocabulary.Vocabulary.Select(v => new { token = v.Token, label = v.Label }),
        });
    }

    // ─── Preview ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/preview")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null) return SupplierContextError();

        var template = await db.ContractTemplates.FirstOrDefaultAsync(
            t => t.Id == id && t.SupplierId == supplierId.Value, ct);
        if (template is null) return NotFound(new { error = "Template not found." });
        if (template.TemplateType != ContractTemplateType.Docx || string.IsNullOrEmpty(template.DocxObjectKey))
            return BadRequest(new { error = "Only docx templates support PDF preview." });

        if (!gotenberg.IsEnabled)
            return BadRequest(new { error = "PDF preview is not configured on this deployment (Gotenberg:Url not set)." });

        var docx = await storageService.DownloadAsync(template.DocxObjectKey);
        if (docx is null) return NotFound(new { error = "Template file is missing from storage." });

        var filled = docService.Fill(docx, ContractTokenVocabulary.SampleValues());
        try
        {
            var pdf = await gotenberg.ConvertDocxToPdfAsync(filled, $"{template.Name}.docx", ct);
            return File(pdf, "application/pdf", $"contract-preview-{id:N}.pdf");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Preview render failed for template {TemplateId}", id);
            return StatusCode(502, new { error = "PDF preview failed: " + ex.Message });
        }
    }

    // ─── Activate ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/activate")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null) return SupplierContextError();

        var template = await db.ContractTemplates.FirstOrDefaultAsync(
            t => t.Id == id && t.SupplierId == supplierId.Value);
        if (template is null) return NotFound(new { error = "Template not found." });

        // Block activation while unknown tokens remain (spec §3, §7 → 409).
        var detected = DeserializeTokens(template.DetectedTokens);
        var unknown  = ContractTokenVocabulary.UnknownTokens(detected);
        if (unknown.Count > 0)
            return Conflict(new
            {
                error         = "Template has unknown tokens and cannot be activated.",
                unknownTokens = unknown,
                vocabulary    = ContractTokenVocabulary.Vocabulary.Select(v => new { token = v.Token, label = v.Label }),
            });

        // Single active template per supplier — deactivate the others.
        var siblings = await db.ContractTemplates
            .Where(t => t.SupplierId == supplierId.Value && t.Id != id && t.IsActive)
            .ToListAsync();
        foreach (var s in siblings) { s.IsActive = false; s.UpdatedAt = DateTime.UtcNow; }

        template.IsActive  = true;
        template.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Activated contract template {TemplateId} for supplier {SupplierId}", id, supplierId);

        return Ok(new { templateId = id, isActive = true });
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private async Task<Guid?> ResolveSupplierIdAsync()
    {
        if (User.IsInRole("Admin"))
        {
            if (Request.Query.TryGetValue("supplierId", out var sv) && Guid.TryParse(sv, out var sid))
                return sid;
            return null;
        }
        var user = await db.Users.FindAsync(User.GetUserId());
        return user?.SupplierId;
    }

    private IActionResult SupplierContextError() =>
        User.IsInRole("Admin")
            ? BadRequest(new
            {
                error   = "supplier_context_required",
                message = "Admin must specify ?supplierId= to view this resource.",
            })
            : BadRequest(new { error = "No supplier is linked to your account." });

    private static List<string> DeserializeTokens(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    /// <summary>A docx is a ZIP (PK\x03\x04) containing word/document.xml; cheap magic check.</summary>
    private static bool LooksLikeDocx(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
}
