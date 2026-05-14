using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/provider")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderPartnerPageController(RuumlyDbContext db) : ControllerBase
{
    // Resolve the supplier for the caller: admin must pass ?supplierId=, provider uses own link.
    private async Task<Guid?> ResolveSupplierIdAsync()
    {
        if (User.IsInRole("Admin"))
        {
            if (Request.Query.TryGetValue("supplierId", out var sv) &&
                Guid.TryParse(sv, out var sid))
                return sid;
            return null;
        }
        var user = await db.Users.FindAsync(User.GetUserId());
        return user?.SupplierId;
    }

    /// <summary>
    /// Returns all partner-page fields for the caller's supplier.
    /// Partners can use this to populate their edit form.
    /// PartnerPageUrl is only set when the page is published; PreviewUrl is always set
    /// when a slug exists (for draft preview before publishing).
    /// </summary>
    [HttpGet("partner-page")]
    public async Task<IActionResult> GetPartnerPage()
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to view this resource.",
                });
            return BadRequest(new { error = ErrorMessages.Get("NO_SUPPLIER_LINKED", Request.GetLang()) });
        }

        var s = await db.Suppliers.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == supplierId.Value);
        if (s is null) return NotFound(new { error = "Supplier not found." });

        return Ok(new
        {
            s.Slug,
            s.IsPartnerPagePublished,
            s.Tagline,
            LongDescriptionEt  = AdminMappers.ParseLang(s.LongDescriptionTranslationsJson, "et"),
            LongDescriptionEn  = AdminMappers.ParseLang(s.LongDescriptionTranslationsJson, "en"),
            LongDescriptionRu  = AdminMappers.ParseLang(s.LongDescriptionTranslationsJson, "ru"),
            s.LogoUrl,
            s.HeroImageUrl,
            s.WebsiteUrl,
            s.FoundedYear,
            s.IsVerified,
            s.FoundingPartner,
            s.GooglePlaceId,
            // Live URL — only present when published
            PartnerPageUrl = s.Slug != null && s.IsPartnerPagePublished
                ? $"https://ruumly.eu/et/partner/{s.Slug}" : null,
            // Preview URL — always present when a slug is set (draft preview)
            PreviewUrl = s.Slug != null
                ? $"https://ruumly.eu/et/partner/{s.Slug}" : null,
        });
    }

    /// <summary>
    /// Partners can update content fields.
    /// Publish state and slug are admin-only — not accepted here.
    /// </summary>
    [HttpPatch("partner-page")]
    public async Task<IActionResult> UpdatePartnerPage(
        [FromBody] UpdateProviderPartnerPageRequest body)
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to update this resource.",
                });
            return BadRequest(new { error = ErrorMessages.Get("NO_SUPPLIER_LINKED", Request.GetLang()) });
        }

        var s = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == supplierId.Value);
        if (s is null) return NotFound(new { error = "Supplier not found." });

        if (body.Tagline is not null)      s.Tagline      = body.Tagline;
        if (body.LogoUrl is not null)      s.LogoUrl      = body.LogoUrl;
        if (body.HeroImageUrl is not null) s.HeroImageUrl = body.HeroImageUrl;
        if (body.WebsiteUrl is not null)   s.WebsiteUrl   = body.WebsiteUrl;
        if (body.FoundedYear.HasValue)     s.FoundedYear  = body.FoundedYear.Value;
        if (body.GooglePlaceId is not null) s.GooglePlaceId = body.GooglePlaceId;

        if (body.LongDescriptionEt is not null ||
            body.LongDescriptionEn is not null ||
            body.LongDescriptionRu is not null)
        {
            var existing = new Dictionary<string, string?>();
            if (!string.IsNullOrEmpty(s.LongDescriptionTranslationsJson))
            {
                try
                {
                    existing = System.Text.Json.JsonSerializer
                        .Deserialize<Dictionary<string, string?>>(
                            s.LongDescriptionTranslationsJson) ?? existing;
                }
                catch { /* leave empty dict */ }
            }
            if (body.LongDescriptionEt is not null) existing["et"] = body.LongDescriptionEt;
            if (body.LongDescriptionEn is not null) existing["en"] = body.LongDescriptionEn;
            if (body.LongDescriptionRu is not null) existing["ru"] = body.LongDescriptionRu;
            s.LongDescriptionTranslationsJson =
                System.Text.Json.JsonSerializer.Serialize(existing);
        }

        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { message = "Partner page updated." });
    }
}
