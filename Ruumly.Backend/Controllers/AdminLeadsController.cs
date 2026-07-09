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
                d.Name,
                d.Email,
                d.Phone,
                d.City,
                category     = d.Category.ToString().ToLower(),
                d.Query,
                d.Language,
                d.CreatedAt,
                status       = d.Status.ToString().ToLower(),
                d.AdminNotes,
                // Concierge intake context (null for legacy/routed leads)
                d.ToCity,
                d.NeedDate,
                d.Details,
                d.Source,
                d.ContactedAt,
                // Routing + quote (null for generic demand-capture leads)
                d.SupplierId,
                supplierName = d.SupplierId == null
                    ? null
                    : Db.Suppliers.Where(s => s.Id == d.SupplierId).Select(s => s.Name).FirstOrDefault(),
                d.ListingId,
                d.QuotedPrice,
                d.QuotedAt,
                d.ProviderNotes,
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

            // First admin touch: any move out of New stamps the first-response
            // timestamp used by the concierge ops metrics. Stamped once, never
            // overwritten by later status changes.
            if (parsedStatus != DemandLeadStatus.New && lead.ContactedAt is null)
                lead.ContactedAt = DateTime.UtcNow;
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

    /// <summary>
    /// Top match suggestions for a concierge lead: active listings from active
    /// suppliers in the lead's category (or all categories for Any), same-city
    /// listings first, then most recently updated. Powers the admin "who can
    /// serve this request" view.
    /// </summary>
    [HttpGet("leads/{id:guid}/matches")]
    public async Task<IActionResult> GetLeadMatches(Guid id)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var query = Db.Listings
            .Where(l => l.IsActive && l.Supplier != null && l.Supplier.IsActive);

        if (lead.Category != DemandLeadCategory.Any)
        {
            var type = lead.Category switch
            {
                DemandLeadCategory.Warehouse => ListingType.Warehouse,
                DemandLeadCategory.Moving    => ListingType.Moving,
                _                            => ListingType.Trailer,
            };
            query = query.Where(l => l.Type == type);
        }

        // ToLower (not ILike) so the same expression runs on both Npgsql and the
        // InMemory test provider.
        var leadCity = (lead.City ?? "").ToLower();
        var matches = await query
            .OrderByDescending(l => leadCity != "" && l.City != null && l.City.ToLower() == leadCity)
            .ThenByDescending(l => l.UpdatedAt)
            .Take(10)
            .Select(l => new
            {
                supplierId   = l.SupplierId,
                supplierName = l.Supplier.Name,
                contactEmail = l.Supplier.ContactEmail,
                contactPhone = l.Supplier.ContactPhone,
                listingId    = l.Id,
                listingTitle = l.Title,
                listingCity  = l.City,
                price        = l.PriceFrom,
                priceUnit    = l.PriceUnit,
            })
            .ToListAsync();

        return Ok(matches);
    }

    /// <summary>
    /// Concierge ops funnel over DemandLeads: request volume, contact/quote/
    /// booking rates and median first-response time for the last 30 days.
    /// Rates computed in memory over the 30-day slice — lead volumes are tiny
    /// and this keeps the math identical on Npgsql and the InMemory provider.
    /// </summary>
    [HttpGet("leads/metrics")]
    public async Task<IActionResult> GetLeadMetrics()
    {
        var now      = DateTime.UtcNow;
        var weekAgo  = now.AddDays(-7);
        var monthAgo = now.AddDays(-30);

        var requestsThisWeek = await Db.DemandLeads.CountAsync(d => d.CreatedAt >= weekAgo);

        var last30 = await Db.DemandLeads
            .Where(d => d.CreatedAt >= monthAgo)
            .Select(d => new { d.CreatedAt, d.Status, d.ContactedAt, d.RespondedAt, d.QuotedPrice })
            .ToListAsync();

        var requests30d    = last30.Count;
        var contacted      = last30.Count(d => d.ContactedAt != null || d.Status != DemandLeadStatus.New);
        var quotedOrBeyond = last30.Count(d => d.QuotedPrice != null
                                            || d.Status == DemandLeadStatus.Quoted
                                            || d.Status == DemandLeadStatus.Converted);
        var converted      = last30.Count(d => d.Status == DemandLeadStatus.Converted);

        // Median minutes from creation to first touch (admin ContactedAt or
        // partner RespondedAt, whichever came first). Null when no lead in the
        // window has been touched yet.
        var responseMinutes = last30
            .Where(d => d.ContactedAt != null || d.RespondedAt != null)
            .Select(d =>
            {
                var first = d.ContactedAt is { } c && d.RespondedAt is { } r
                    ? (c < r ? c : r)
                    : (d.ContactedAt ?? d.RespondedAt)!.Value;
                return (first - d.CreatedAt).TotalMinutes;
            })
            .OrderBy(m => m)
            .ToList();

        int? medianFirstResponseMinutes = null;
        if (responseMinutes.Count > 0)
        {
            var mid = responseMinutes.Count / 2;
            var median = responseMinutes.Count % 2 == 1
                ? responseMinutes[mid]
                : (responseMinutes[mid - 1] + responseMinutes[mid]) / 2.0;
            medianFirstResponseMinutes = (int)Math.Round(median);
        }

        return Ok(new
        {
            requestsThisWeek,
            requests30d,
            contactRate30d = requests30d == 0 ? 0d : contacted / (double)requests30d,
            quoteRate30d   = requests30d == 0 ? 0d : quotedOrBeyond / (double)requests30d,
            bookingRate30d = converted / (double)Math.Max(1, quotedOrBeyond),
            medianFirstResponseMinutes,
        });
    }
}

public record UpdateLeadRequest(string? Status, string? AdminNotes);
