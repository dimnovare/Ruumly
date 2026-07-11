using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Admin side of the concierge offer loop: build an offer (a curated set of
/// options) for a demand lead, send it to the customer's email as a tokenized
/// public page, and ask providers for availability without exposing the
/// customer's identity (the admin brokers the introduction).
/// </summary>
[Route("api/admin")]
public class AdminOffersController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IConfiguration config) : AdminBaseController(db)
{
    // Reply-To on customer/provider correspondence — matches the info@ address
    // printed in the email signatures (EmailTranslations). This is NOT the ops
    // alert destination (see Helpers/OpsInbox); it's where a customer/provider
    // reply should land, so it stays paired with the signature text.
    private const string OpsReplyTo = OpsInbox.Fallback;

    // ─── Offers ───────────────────────────────────────────────────────────────

    [HttpPost("leads/{id:guid}/offers")]
    public async Task<IActionResult> CreateOffer(Guid id, [FromBody] CreateOfferRequest body)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var offer = new Offer
        {
            Id           = Guid.NewGuid(),
            DemandLeadId = lead.Id,
            Token        = OfferToken.Generate(),
            Status       = OfferStatus.Draft,
            Language     = NormalizeLanguage(body.Language)
                        ?? NormalizeLanguage(lead.Language)
                        ?? "et",
            CustomerNote = Clamp(body.CustomerNote, 2000),
            CreatedAt    = DateTime.UtcNow,
            CreatedBy    = User.FindFirstValue(ClaimTypes.Email) ?? User.GetUserId().ToString(),
        };

        if (body.Options is { Count: > 0 })
        {
            var (options, error) = BuildOptions(offer.Id, body.Options);
            if (error is not null) return BadRequest(Error(error));
            offer.Options = options!;
        }

        Db.Offers.Add(offer);
        Audit("offer.created", User.GetUserId().ToString(), offer.Id.ToString(),
              $"Lead: {lead.Id}, options: {offer.Options.Count}");
        await Db.SaveChangesAsync();

        await FixUpOptionSuppliers(offer);
        return Ok(MapOffer(offer));
    }

    [HttpGet("leads/{id:guid}/offers")]
    public async Task<IActionResult> GetLeadOffers(Guid id)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var offers = await Db.Offers
            .Include(o => o.Options).ThenInclude(op => op.Supplier)
            .Where(o => o.DemandLeadId == id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(offers.Select(MapOffer).ToList());
    }

    [HttpGet("offers/{id:guid}")]
    public async Task<IActionResult> GetOffer(Guid id)
    {
        var offer = await Db.Offers
            .Include(o => o.Options).ThenInclude(op => op.Supplier)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (offer is null) return NotFound(Error("Offer not found."));

        return Ok(MapOffer(offer));
    }

    [HttpPatch("offers/{id:guid}")]
    public async Task<IActionResult> UpdateOffer(Guid id, [FromBody] UpdateOfferRequest body)
    {
        var offer = await Db.Offers
            .Include(o => o.Options).ThenInclude(op => op.Supplier)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (offer is null) return NotFound(Error("Offer not found."));
        if (offer.Status == OfferStatus.Chosen)
            return Conflict(Error("The customer already chose an option — this offer can no longer be edited."));

        if (body.CustomerNote is not null)
            offer.CustomerNote = Clamp(body.CustomerNote, 2000);

        if (!string.IsNullOrWhiteSpace(body.Language))
        {
            var lang = NormalizeLanguage(body.Language);
            if (lang is null) return BadRequest(Error("Unknown language."));
            offer.Language = lang;
        }

        if (!string.IsNullOrEmpty(body.Status))
        {
            if (!Enum.TryParse<OfferStatus>(body.Status, ignoreCase: true, out var parsed))
                return BadRequest(Error("Unknown offer status."));
            // Chosen is set by the customer on the public page, never by PATCH.
            if (parsed == OfferStatus.Chosen)
                return BadRequest(Error("Chosen is set by the customer, not by PATCH."));
            offer.Status = parsed;
        }

        // Replace-set: a non-null Options list rewrites the whole option set.
        if (body.Options is not null)
        {
            var (options, error) = BuildOptions(offer.Id, body.Options);
            if (error is not null) return BadRequest(Error(error));
            Db.OfferOptions.RemoveRange(offer.Options.ToList());
            offer.Options.Clear();
            // AddRange through the DbSet: the new options carry pre-generated
            // Guids, so nav-fixup discovery would track them as Modified (key
            // set ⇒ "existing"), not Added — and the update would then target
            // rows that don't exist. Relationship fixup puts them into
            // offer.Options for us (OfferId matches the tracked parent) —
            // adding them to the nav manually as well would duplicate them.
            Db.OfferOptions.AddRange(options!);
        }

        Audit("offer.updated", User.GetUserId().ToString(), offer.Id.ToString(),
              $"Status: {offer.Status}, options: {offer.Options.Count}");
        await Db.SaveChangesAsync();

        await FixUpOptionSuppliers(offer);
        return Ok(MapOffer(offer));
    }

    /// <summary>
    /// Emails the offer to the lead (in the offer's language, with the public
    /// /offer/{token} link), marks it Sent and auto-moves the lead to Quoted —
    /// an offer in the customer's inbox IS the quote.
    /// </summary>
    [HttpPost("offers/{id:guid}/send")]
    public async Task<IActionResult> SendOffer(Guid id)
    {
        var offer = await Db.Offers
            .Include(o => o.Options).ThenInclude(op => op.Supplier)
            .Include(o => o.DemandLead)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (offer is null) return NotFound(Error("Offer not found."));
        if (offer.Status is OfferStatus.Chosen or OfferStatus.Expired)
            return Conflict(Error("This offer is closed — create a new one instead."));
        if (offer.Options.Count == 0)
            return BadRequest(Error("Add at least one option before sending."));

        var lead = offer.DemandLead!;
        if (string.IsNullOrWhiteSpace(lead.Email))
            return BadRequest(Error("The lead has no email address."));

        // Re-sends refresh SentAt but never regress a Viewed offer back to Sent.
        if (offer.Status == OfferStatus.Draft)
            offer.Status = OfferStatus.Sent;
        offer.SentAt = DateTime.UtcNow;

        // Shared lead lifecycle: → Quoted, first-touch ContactedAt stamped once.
        // Never demote an already-converted lead.
        if (lead.Status != DemandLeadStatus.Converted)
            DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Quoted);

        Audit("offer.sent", User.GetUserId().ToString(), offer.Id.ToString(),
              $"Lead: {lead.Id}, options: {offer.Options.Count}");

        // Save BEFORE enqueueing: Hangfire commits the job on its own
        // connection, so a failed save here must not leave the customer
        // holding a live /offer/{token} link to a still-Draft offer.
        await Db.SaveChangesAsync();

        var t    = EmailTranslations.For(offer.Language);
        var link = FrontendUrl.Localized(config["AppUrl"], offer.Language, $"offer/{offer.Token}");
        // Reply-To ops inbox — the email explicitly invites the customer to
        // reply with questions, and replies must not vanish into noreply@.
        emailQueue.EnqueueEmail(
            lead.Email.Trim(), t.OfferSubject, BuildOfferEmailBody(t, offer, link),
            htmlBody: null, replyTo: OpsReplyTo);

        return Ok(MapOffer(offer));
    }

    // ─── Provider outreach ────────────────────────────────────────────────────

    /// <summary>
    /// Availability-request batch: one email per supplier that has a
    /// ContactEmail (the rest are reported back as skipped). The email carries
    /// lead facts only — category, route, details, date — never the customer's
    /// name/email/phone; replies land in the ops inbox via Reply-To.
    /// </summary>
    [HttpPost("leads/{id:guid}/outreach")]
    public async Task<IActionResult> SendOutreach(Guid id, [FromBody] OutreachRequest body)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var requestedIds = (body.SupplierIds ?? []).Distinct().ToList();
        if (requestedIds.Count == 0)
            return BadRequest(Error("supplierIds must contain at least one supplier."));

        var suppliers = await Db.Suppliers
            .Where(s => requestedIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        var route   = string.IsNullOrWhiteSpace(lead.ToCity) ? lead.City : $"{lead.City} → {lead.ToCity}";
        var date    = lead.NeedDate?.ToString("yyyy-MM-dd") ?? "—";
        var details = string.IsNullOrWhiteSpace(lead.Details) ? "—" : lead.Details;

        var sent    = new List<object>();
        var skipped = new List<object>();
        var emails  = new List<(string To, string Subject, string Body)>();

        foreach (var supplierId in requestedIds)
        {
            if (!suppliers.TryGetValue(supplierId, out var supplier))
            {
                skipped.Add(new { supplierId, supplierName = (string?)null, reason = "not_found" });
                continue;
            }
            if (string.IsNullOrWhiteSpace(supplier.ContactEmail))
            {
                skipped.Add(new { supplierId, supplierName = (string?)supplier.Name, reason = "no_email" });
                continue;
            }

            // Providers get their own language (by country), not the customer's.
            var lang = supplier.Country?.ToUpperInvariant() switch
            {
                "LV" => "lv",
                "LT" => "lt",
                "EE" => "et",
                _    => "en",
            };
            var t        = EmailTranslations.For(lang);
            var category = t.CategoryLabel(lead.Category);
            var emailBody =
                $"{t.OutreachGreeting}\n\n" +
                $"{t.OutreachBody(category, route, details, date)}\n\n" +
                $"{t.OutreachAsk}\n\n" +
                $"{t.OutreachSignature}";

            emails.Add((supplier.ContactEmail.Trim(), t.OutreachSubject(category, route), emailBody));

            var row = new ProviderOutreach
            {
                Id           = Guid.NewGuid(),
                DemandLeadId = lead.Id,
                SupplierId   = supplier.Id,
                SentTo       = supplier.ContactEmail.Trim(),
                SentAt       = DateTime.UtcNow,
                Status       = ProviderOutreachStatus.Sent,
            };
            Db.ProviderOutreaches.Add(row);
            sent.Add(MapOutreach(row, supplier.Name));
        }

        // First outreach is the first admin touch: New → Contacted (stamps
        // ContactedAt). Leads further down the funnel keep their status.
        if (emails.Count > 0 && lead.Status == DemandLeadStatus.New)
            DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Contacted);

        Audit("lead.outreach_sent", User.GetUserId().ToString(), lead.Id.ToString(),
              $"Sent: {sent.Count}, skipped: {skipped.Count}");

        // Save BEFORE enqueueing: Hangfire commits jobs on its own connection,
        // so a failed save must not leave providers emailed with no
        // ProviderOutreach rows (history lost, a retry would double-email).
        await Db.SaveChangesAsync();

        foreach (var (to, subject, emailBody) in emails)
            emailQueue.EnqueueEmail(to, subject, emailBody, htmlBody: null, replyTo: OpsReplyTo);

        return Ok(new { sent, skipped });
    }

    [HttpGet("leads/{id:guid}/outreach")]
    public async Task<IActionResult> GetLeadOutreach(Guid id)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var rows = await Db.ProviderOutreaches
            .Where(o => o.DemandLeadId == id)
            .OrderByDescending(o => o.SentAt)
            .Select(o => new
            {
                Row = o,
                SupplierName = Db.Suppliers
                    .Where(s => s.Id == o.SupplierId)
                    .Select(s => (string?)s.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(rows.Select(r => MapOutreach(r.Row, r.SupplierName)).ToList());
    }

    [HttpPatch("outreach/{id:guid}")]
    public async Task<IActionResult> UpdateOutreach(Guid id, [FromBody] UpdateOutreachRequest body)
    {
        var row = await Db.ProviderOutreaches.FindAsync(id);
        if (row is null) return NotFound(Error("Outreach not found."));

        if (!string.IsNullOrEmpty(body.Status))
        {
            if (!Enum.TryParse<ProviderOutreachStatus>(body.Status, ignoreCase: true, out var parsed))
                return BadRequest(Error("Unknown outreach status."));
            row.Status = parsed;
        }

        if (body.Note is not null)
            row.Note = Clamp(body.Note, 2000);

        Audit("lead.outreach_updated", User.GetUserId().ToString(), row.Id.ToString(),
              $"Status: {row.Status}");
        await Db.SaveChangesAsync();

        var supplierName = await Db.Suppliers
            .Where(s => s.Id == row.SupplierId)
            .Select(s => (string?)s.Name)
            .FirstOrDefaultAsync();
        return Ok(MapOutreach(row, supplierName));
    }

    // ─── Mapping / helpers ────────────────────────────────────────────────────

    private static object MapOffer(Offer o) => new
    {
        id             = o.Id,
        demandLeadId   = o.DemandLeadId,
        token          = o.Token,
        status         = o.Status.ToString().ToLower(),
        language       = o.Language,
        customerNote   = o.CustomerNote,
        createdAt      = o.CreatedAt,
        sentAt         = o.SentAt,
        viewedAt       = o.ViewedAt,
        chosenAt       = o.ChosenAt,
        chosenOptionId = o.ChosenOptionId,
        createdBy      = o.CreatedBy,
        options        = o.Options
            .OrderBy(op => op.SortOrder).ThenBy(op => op.Id)
            .Select(op => new
            {
                id                 = op.Id,
                supplierId         = op.SupplierId,
                supplierName       = op.Supplier?.Name,
                supplierLocationId = op.SupplierLocationId,
                title              = op.Title,
                priceAmount        = op.PriceAmount,
                priceUnit          = op.PriceUnit,
                notes              = op.Notes,
                sortOrder          = op.SortOrder,
            })
            .ToList(),
    };

    private static object MapOutreach(ProviderOutreach o, string? supplierName) => new
    {
        id           = o.Id,
        demandLeadId = o.DemandLeadId,
        supplierId   = o.SupplierId,
        supplierName,
        sentTo       = o.SentTo,
        sentAt       = o.SentAt,
        status       = o.Status.ToString().ToLower(),
        note         = o.Note,
    };

    private static (List<OfferOption>? Options, string? Error) BuildOptions(
        Guid offerId, List<OfferOptionInput> inputs)
    {
        var options = new List<OfferOption>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var title = input.Title?.Trim();
            if (string.IsNullOrEmpty(title))
                return (null, "Every option needs a title.");

            options.Add(new OfferOption
            {
                Id                 = Guid.NewGuid(),
                OfferId            = offerId,
                SupplierId         = input.SupplierId,
                SupplierLocationId = input.SupplierLocationId,
                Title              = title.Length > 200 ? title[..200] : title,
                PriceAmount        = input.PriceAmount,
                PriceUnit          = Clamp(input.PriceUnit, 40),
                Notes              = Clamp(input.Notes, 2000),
                // Explicit sort orders win (including an explicit 0);
                // otherwise keep the payload order.
                SortOrder          = input.SortOrder ?? i,
            });
        }
        return (options, null);
    }

    private static string BuildOfferEmailBody(EmailTranslations.EmailStrings t, Offer offer, string link)
    {
        var sb = new StringBuilder();
        sb.AppendLine(t.OfferGreeting);
        sb.AppendLine();
        sb.AppendLine(t.OfferIntro);
        sb.AppendLine();

        var i = 1;
        foreach (var op in offer.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
        {
            sb.AppendLine($"{i}. {op.Title}{FormatPrice(op)}");
            if (!string.IsNullOrWhiteSpace(op.Notes))
                sb.AppendLine($"   {op.Notes}");
            i++;
        }

        if (!string.IsNullOrWhiteSpace(offer.CustomerNote))
        {
            sb.AppendLine();
            sb.AppendLine($"{t.OfferNoteLabel} {offer.CustomerNote}");
        }

        sb.AppendLine();
        sb.AppendLine(t.OfferCta);
        sb.AppendLine(link);
        sb.AppendLine();
        sb.AppendLine(t.OfferQuestions);
        sb.AppendLine();
        sb.Append(t.OfferSignature);
        return sb.ToString();
    }

    private static string FormatPrice(OfferOption op) =>
        op.PriceAmount is { } amount
            ? $" — {amount.ToString("0.##", CultureInfo.InvariantCulture)} €" +
              (string.IsNullOrWhiteSpace(op.PriceUnit) ? "" : $" / {op.PriceUnit}")
            : "";

    private static string? NormalizeLanguage(string? lang)
    {
        var l = lang?.Trim().ToLowerInvariant();
        return l is "et" or "en" or "ru" or "lv" or "lt" ? l : null;
    }

    private static string? Clamp(string? s, int max)
    {
        var trimmed = s?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>
    /// Loads the suppliers referenced by the offer's options into the change
    /// tracker; EF nav fixup then populates op.Supplier so MapOffer can emit
    /// supplierName without a per-option query.
    /// </summary>
    private async Task FixUpOptionSuppliers(Offer offer)
    {
        var ids = offer.Options
            .Where(op => op.SupplierId != null && op.Supplier == null)
            .Select(op => op.SupplierId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return;
        await Db.Suppliers.Where(s => ids.Contains(s.Id)).LoadAsync();
    }
}
