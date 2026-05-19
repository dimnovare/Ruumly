using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminLeadsController(RuumlyDbContext db) : AdminBaseController(db)
{
    [HttpGet("leads")]
    public async Task<IActionResult> GetLeads(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = Db.DemandLeads.AsQueryable();

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<DemandLeadStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(d => d.Status == parsedStatus);
        }

        var total = await query.CountAsync();
        var leads = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(d => new
            {
                d.Id,
                d.Email,
                d.City,
                category   = d.Category.ToString().ToLower(),
                d.Query,
                d.Language,
                d.CreatedAt,
                status     = d.Status.ToString().ToLower(),
                d.AdminNotes,
            })
            .ToListAsync();

        return Ok(new { total, page, limit, items = leads });
    }

    [HttpPatch("leads/{id:guid}")]
    public async Task<IActionResult> UpdateLead(Guid id, [FromBody] UpdateLeadRequest body)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        if (!string.IsNullOrEmpty(body.Status) &&
            Enum.TryParse<DemandLeadStatus>(body.Status, ignoreCase: true, out var parsedStatus))
        {
            lead.Status = parsedStatus;
        }

        if (body.AdminNotes is not null)
            lead.AdminNotes = body.AdminNotes;

        Audit("demand_lead.updated", User.GetUserId().ToString(), lead.Id.ToString(),
              $"Status: {lead.Status}, Notes: {(body.AdminNotes is not null ? "updated" : "unchanged")}");

        await Db.SaveChangesAsync();

        return Ok(new
        {
            lead.Id,
            lead.Email,
            lead.City,
            status     = lead.Status.ToString().ToLower(),
            lead.AdminNotes,
        });
    }
}

public record UpdateLeadRequest(string? Status, string? AdminNotes);
