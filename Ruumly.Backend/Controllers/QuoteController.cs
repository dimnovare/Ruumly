using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Public, tokenized provider quote form. A provider opens
/// /{lang}/quote/{token} from their outreach email and submits a price without
/// an account. The per-recipient 256-bit token is the only credential — an
/// unknown token looks exactly like a missing one (404). The page NEVER exposes
/// the customer's identity (name/email/phone); the provider only sees what they
/// are quoting. Submitting flips the outreach to Replied and auto-seeds the
/// lead's draft offer with an option — it creates NO customer email, Booking or
/// Order; the admin later reviews the seeded draft and sends it.
/// </summary>
[ApiController]
[Route("api/quote")]
public class QuoteController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue) : ControllerBase
{
    /// <summary>
    /// The provider's view of what they are quoting. No customer PII — only the
    /// structured ask (category, city/route, date, details) and the provider's
    /// own already-submitted quote for prefill. 404 for unknown tokens.
    /// </summary>
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuote(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound(new { error = "Quote not found." });

        var outreach = await db.ProviderOutreaches
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.QuoteToken == token);
        if (outreach is null)
            return NotFound(new { error = "Quote not found." });

        var lead = await db.DemandLeads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == outreach.DemandLeadId);
        if (lead is null)
            return NotFound(new { error = "Quote not found." });

        var providerName = await db.Suppliers
            .Where(s => s.Id == outreach.SupplierId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? "Provider";

        var alreadySubmitted = outreach.QuotedAt is not null;
        var existing = alreadySubmitted
            ? new PublicQuoteExistingDto(
                outreach.QuotedAmount, outreach.QuotedUnit,
                outreach.QuotedAvailability, outreach.QuotedNote)
            : null;

        return Ok(new PublicQuoteDto(
            new PublicQuoteProviderDto(providerName),
            new PublicQuoteLeadDto(
                ServiceCategories.SlugFor(lead.Category),
                lead.City, lead.ToCity, lead.NeedDate, lead.Details),
            "EUR",
            alreadySubmitted,
            existing));
    }

    /// <summary>
    /// The provider submits (or updates) their price. Idempotent: re-submitting
    /// updates the same outreach row and the same auto-seeded OfferOption (keyed
    /// by SupplierId) — never a duplicate. Stores the quote, flips the outreach
    /// to Replied, seeds/updates the lead's newest Draft offer, and alerts ops.
    /// No customer email, Booking or Order is created.
    /// </summary>
    [HttpPost("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitQuote(string token, [FromBody] SubmitQuoteRequest req)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound(new { error = "Quote not found." });

        // Validate BEFORE any lookup/mutation.
        if (req is null)
            return BadRequest(new { error = "A quote is required." });
        if (req.PriceAmount < 0)
            return BadRequest(new { error = "Price cannot be negative." });
        static bool HasAngle(string? s) => s is not null && (s.Contains('<') || s.Contains('>'));
        if (HasAngle(req.PriceUnit) || HasAngle(req.Availability) || HasAngle(req.Note))
            return BadRequest(new { error = "Text fields contain invalid characters." });

        // InMemory (tests) — no transaction; the find-or-create is sequential.
        if (!db.Database.IsRelational())
        {
            var outreach = await db.ProviderOutreaches
                .FirstOrDefaultAsync(o => o.QuoteToken == token);
            if (outreach is null)
                return NotFound(new { error = "Quote not found." });
            var lead = await db.DemandLeads.FirstOrDefaultAsync(d => d.Id == outreach.DemandLeadId);
            var (result, email) = await ApplyQuoteAsync(outreach, lead, req);
            EnqueueOps(email);
            return result;
        }

        // Relational — serializable + FOR UPDATE on the outreach row AND the lead
        // so two concurrent submits can't spawn two draft offers (mirrors the
        // offer create-reuse locking in AdminOffersController).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var outreach = await db.ProviderOutreaches
                    .FromSqlInterpolated(
                        $"""SELECT * FROM "ProviderOutreaches" WHERE "QuoteToken" = {token} FOR UPDATE""")
                    .SingleOrDefaultAsync();
                if (outreach is null)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { error = "Quote not found." });
                }

                var lead = await db.DemandLeads
                    .FromSqlInterpolated(
                        $"""SELECT * FROM "DemandLeads" WHERE "Id" = {outreach.DemandLeadId} FOR UPDATE""")
                    .SingleOrDefaultAsync();

                var (result, email) = await ApplyQuoteAsync(outreach, lead, req);
                await transaction.CommitAsync();
                EnqueueOps(email);
                return result;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < 2)
            {
                try { await transaction.RollbackAsync(); }
                catch { /* The failed statement may already have aborted the transaction. */ }
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Serializable quote submit retry limit exhausted.");
    }

    /// <summary>
    /// Applies a validated quote: stores it on the outreach row, flips Status to
    /// Replied, and add-or-updates the SupplierId-keyed option on the lead's
    /// newest Draft offer (creating that draft if none exists). Returns the
    /// thank-you result plus the ops-alert email to enqueue AFTER commit.
    /// </summary>
    private async Task<(IActionResult Result, (string To, string Subject, string Body)? Email)> ApplyQuoteAsync(
        ProviderOutreach outreach, DemandLead? lead, SubmitQuoteRequest req)
    {
        if (lead is null)
            return (NotFound(new { error = "Quote not found." }), null);

        var amount = req.PriceAmount;
        var unit = Clamp(req.PriceUnit, 40);
        var availability = Clamp(req.Availability, 200);
        var note = Clamp(req.Note, 2000);

        // 1. Record the answer on the outreach row.
        outreach.Status             = ProviderOutreachStatus.Replied;
        outreach.QuotedAmount       = amount;
        outreach.QuotedUnit         = unit;
        outreach.QuotedAvailability = availability;
        outreach.QuotedNote         = note;
        outreach.QuotedAt           = DateTime.UtcNow;

        var supplierName = await db.Suppliers
            .Where(s => s.Id == outreach.SupplierId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? "Provider";

        // 2. Find-or-create the lead's newest Draft offer.
        var offer = await db.Offers
            .Include(o => o.Options)
            .Where(o => o.DemandLeadId == lead.Id && o.Status == OfferStatus.Draft)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync();
        if (offer is null)
        {
            offer = new Offer
            {
                Id           = Guid.NewGuid(),
                DemandLeadId = lead.Id,
                Token        = OfferToken.Generate(),
                Status       = OfferStatus.Draft,
                Language     = lead.Language is "et" or "en" or "ru" or "lv" or "lt" ? lead.Language : "et",
                CreatedAt    = DateTime.UtcNow,
                // Seeded by the provider, not an admin — flagged so the workspace
                // can badge the option "from provider quote".
                CreatedBy    = "provider-quote",
            };
            db.Offers.Add(offer);
        }

        // 3. Add-or-update the option keyed by SupplierId (re-submit updates, never duplicates).
        var title = Clamp($"{supplierName} — {lead.City}", 200)!;
        var existingOption = offer.Options.FirstOrDefault(o => o.SupplierId == outreach.SupplierId);
        if (existingOption is null)
        {
            offer.Options.Add(new OfferOption
            {
                Id          = Guid.NewGuid(),
                OfferId     = offer.Id,
                SupplierId  = outreach.SupplierId,
                Title       = title,
                PriceAmount = amount,
                PriceUnit   = unit,
                Notes       = note,
                SortOrder   = offer.Options.Count,   // appended
            });
        }
        else
        {
            existingOption.Title       = title;
            existingOption.PriceAmount = amount;
            existingOption.PriceUnit   = unit;
            existingOption.Notes       = note;
        }

        await db.SaveChangesAsync();

        // 4. Ops alert (internal inbox) — enqueued by the caller after commit.
        var opsInbox = await OpsInbox.ResolveAsync(db);
        var amountStr = amount.ToString("0.##", CultureInfo.InvariantCulture);
        var unitStr = string.IsNullOrWhiteSpace(unit) ? "" : $" {unit}";
        var category = ServiceCategories.SlugFor(lead.Category);
        var email = (
            opsInbox,
            $"Ruumly — provider quote ({lead.City})",
            $"Provider {supplierName} quoted {amountStr} €{unitStr} for the {category} lead in {lead.City}.\n\n" +
            $"Availability: {availability ?? "—"}\n" +
            $"Note: {note ?? "—"}\n\n" +
            $"The quote seeded the lead's draft offer — review it in the admin CRM → Leads and send it.");

        return (Ok(new QuoteSubmittedDto(true, amount, unit, availability, note)), email);
    }

    private void EnqueueOps((string To, string Subject, string Body)? email)
    {
        if (email is { } e)
            emailQueue.EnqueueEmail(e.To, e.Subject, e.Body);
    }

    private static string? Clamp(string? s, int max)
    {
        var trimmed = s?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                return true;
        }

        return false;
    }
}
