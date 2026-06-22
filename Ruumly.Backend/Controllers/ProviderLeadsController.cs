using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Partner-facing inbox for routed demand leads (moving/trailer "request a quote").
/// A provider only ever sees and acts on leads routed to their own supplier; admins
/// may scope to any supplier via <c>?supplierId=</c> (impersonation).
/// </summary>
[ApiController]
[Route("api/provider/leads")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderLeadsController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeads(
        [FromQuery] Guid?   supplierId = null,
        [FromQuery] string? status     = null,
        [FromQuery] int     page       = 1,
        [FromQuery] int     limit      = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var sid = await ResolveSupplierIdAsync(supplierId);
        if (sid is null)
            return Ok(new { items = Array.Empty<object>(), total = 0, page, limit });

        var query = db.DemandLeads.Where(d => d.SupplierId == sid);

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<DemandLeadStatus>(status, ignoreCase: true, out var parsed))
            query = query.Where(d => d.Status == parsed);

        var total = await query.CountAsync();
        var items = await query
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
                category  = d.Category.ToString().ToLower(),
                d.Query,
                status    = d.Status.ToString().ToLower(),
                d.QuotedPrice,
                d.QuotedAt,
                d.ProviderNotes,
                d.ListingId,
                d.CreatedAt,
            })
            .ToListAsync();

        return Ok(new { items, total, page, limit });
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondLeadRequest body)
    {
        var lead = await db.DemandLeads.FindAsync(id);
        if (lead is null)
            return NotFound(new { error = "Lead not found." });

        // Ownership: a provider may only act on their own supplier's leads.
        if (User.GetUserRole() == UserRole.Provider)
        {
            var user = await db.Users.FindAsync(User.GetUserId());
            if (user?.SupplierId is null || user.SupplierId != lead.SupplierId)
                return StatusCode(403, new { error = "You can only act on your own supplier's leads." });
        }

        if (body.ProviderNotes is { Length: > 2000 })
            return BadRequest(new { error = "Notes must be 2000 characters or fewer." });

        var sentQuote = false;
        if (body.QuotedPrice.HasValue)
        {
            if (body.QuotedPrice.Value < 0)
                return BadRequest(new { error = "Quoted price cannot be negative." });
            lead.QuotedPrice = decimal.Round(body.QuotedPrice.Value, 2);
            lead.QuotedAt    = DateTime.UtcNow;
            // A price quote implies the partner has responded; advance the funnel
            // unless they explicitly set a different status below.
            if (lead.Status is DemandLeadStatus.New)
                lead.Status = DemandLeadStatus.Quoted;
            sentQuote = true;
        }

        if (!string.IsNullOrEmpty(body.Status) &&
            Enum.TryParse<DemandLeadStatus>(body.Status, ignoreCase: true, out var parsed))
            lead.Status = parsed;

        if (body.ProviderNotes is not null)
            lead.ProviderNotes = body.ProviderNotes;

        lead.RespondedAt ??= DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Email the customer the quote (transactional → EmailTranslations).
        if (sentQuote && !string.IsNullOrWhiteSpace(lead.Email))
        {
            var partnerName = await db.Suppliers
                .Where(s => s.Id == lead.SupplierId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync() ?? "Ruumly";
            var listingTitle = lead.ListingId is null
                ? lead.City
                : await db.Listings.Where(l => l.Id == lead.ListingId)
                    .Select(l => l.Title).FirstOrDefaultAsync() ?? lead.City;

            var tl    = EmailTranslations.For(lead.Language);
            var price = $"{lead.QuotedPrice:0.##} €";
            emailQueue.EnqueueEmail(
                to:       lead.Email,
                subject:  tl.QuoteReplySubject,
                textBody: tl.QuoteReplyBody(lead.Name ?? "", partnerName, listingTitle, price));
        }

        return Ok(new
        {
            lead.Id,
            status = lead.Status.ToString().ToLower(),
            lead.QuotedPrice,
            lead.QuotedAt,
            lead.ProviderNotes,
        });
    }

    /// <summary>Provider → own supplier; Admin → requested supplier (impersonation).</summary>
    private async Task<Guid?> ResolveSupplierIdAsync(Guid? requested)
    {
        if (User.GetUserRole() == UserRole.Admin && requested.HasValue)
            return requested;
        var user = await db.Users.FindAsync(User.GetUserId());
        return user?.SupplierId;
    }
}

public record RespondLeadRequest(string? Status, decimal? QuotedPrice, string? ProviderNotes);
