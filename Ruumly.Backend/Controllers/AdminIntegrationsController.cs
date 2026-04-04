using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminIntegrationsController(RuumlyDbContext db) : AdminBaseController(db)
{
    [HttpGet("integrations")]
    public async Task<IActionResult> GetIntegrations()
    {
        // Backfill: create IntegrationSettings for suppliers that don't have one
        var suppliersWithoutSettings = await Db.Suppliers
            .Where(s => s.IsActive && !Db.IntegrationSettings.Any(i => i.SupplierId == s.Id))
            .ToListAsync();

        if (suppliersWithoutSettings.Count > 0)
        {
            foreach (var s in suppliersWithoutSettings)
            {
                Db.IntegrationSettings.Add(new IntegrationSettings
                {
                    Id                  = Guid.NewGuid(),
                    SupplierId          = s.Id,
                    ApprovalMode        = ApprovalMode.Auto,
                    PostingMode         = (PostingMode)(int)s.IntegrationType,
                    FallbackPostingMode = PostingMode.Email,
                    IsActive            = true,
                    UpdatedAt           = DateTime.UtcNow,
                });
            }
            await Db.SaveChangesAsync();
        }

        var settings = await Db.IntegrationSettings
            .Include(i => i.Supplier)
            .OrderBy(i => i.Supplier.Name)
            .ToListAsync();
        return Ok(settings.Select(AdminMappers.MapIntegrationSettings));
    }

    [HttpPatch("integrations/{id:guid}")]
    public async Task<IActionResult> PatchIntegration(Guid id, [FromBody] PatchIntegrationSettingsRequest body)
    {
        var settings = await Db.IntegrationSettings
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (settings is null) return NotFound(Error("Integration settings not found"));

        if (body.ApprovalMode is not null &&
            Enum.TryParse<ApprovalMode>(body.ApprovalMode, ignoreCase: true, out var am))
            settings.ApprovalMode = am;

        if (body.PostingMode is not null &&
            Enum.TryParse<PostingMode>(body.PostingMode, ignoreCase: true, out var pm))
            settings.PostingMode = pm;

        if (body.FallbackPostingMode is not null &&
            Enum.TryParse<PostingMode>(body.FallbackPostingMode, ignoreCase: true, out var fpm))
            settings.FallbackPostingMode = fpm;

        if (body.MappingProfile is not null)
            settings.MappingProfile = body.MappingProfile;

        if (body.IsActive.HasValue)
            settings.IsActive = body.IsActive.Value;

        settings.UpdatedAt = DateTime.UtcNow;

        await Audit("integration.updated", User.GetUserEmail(),
            settings.Supplier.Name, "Settings updated");
        await Db.SaveChangesAsync();

        return Ok(AdminMappers.MapIntegrationSettings(settings));
    }
}
