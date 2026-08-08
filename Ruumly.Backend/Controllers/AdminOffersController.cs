using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
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
/// Admin side of the concierge offer loop: build an offer (a curated set of
/// options) for a demand lead, send it to the customer's email as a tokenized
/// public page, and ask providers for availability without exposing the
/// customer's identity (the admin brokers the introduction).
/// </summary>
[Route("api/admin")]
public class AdminOffersController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IConfiguration config,
    IConciergeOutreachService outreachService) : AdminBaseController(db)
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
        if (!Db.Database.IsRelational())
        {
            var inMemoryLead = await Db.DemandLeads.FindAsync(id);
            return inMemoryLead is null
                ? NotFound(Error("Lead not found."))
                : await CreateOrReuseOffer(inMemoryLead, body);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction =
                await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                // One lead can have at most one active draft. Locking the lead
                // makes concurrent retries queue behind the creating transaction.
                var lead = await Db.DemandLeads
                    .FromSqlInterpolated(
                        $"""SELECT * FROM "DemandLeads" WHERE "Id" = {id} FOR UPDATE""")
                    .SingleOrDefaultAsync();
                if (lead is null) return NotFound(Error("Lead not found."));

                var result = await CreateOrReuseOffer(lead, body);
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < 2)
            {
                try { await transaction.RollbackAsync(); }
                catch { /* The failed statement may already have aborted the transaction. */ }
                Db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Serializable offer creation retry limit exhausted.");
    }

    private async Task<IActionResult> CreateOrReuseOffer(
        DemandLead lead, CreateOfferRequest body)
    {
        var existingDraft = await Db.Offers
            .Include(o => o.Options).ThenInclude(option => option.Supplier)
            .Where(o => o.DemandLeadId == lead.Id && o.Status == OfferStatus.Draft)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync();
        if (existingDraft is not null)
            return Ok(MapOffer(existingDraft));

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

    [HttpDelete("offers/{id:guid}")]
    public async Task<IActionResult> DeleteOffer(Guid id)
    {
        if (!Db.Database.IsRelational())
        {
            var inMemoryOffer = await Db.Offers
                .Include(o => o.Options)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (inMemoryOffer is null) return NotFound(Error("Offer not found."));
            if (inMemoryOffer.Status != OfferStatus.Draft)
                return Conflict(Error("Only draft offers can be deleted."));

            Db.Offers.Remove(inMemoryOffer);
            Audit("offer.deleted", User.GetUserId().ToString(), id.ToString(),
                  $"Lead: {inMemoryOffer.DemandLeadId}");
            await Db.SaveChangesAsync();
            return NoContent();
        }

        await using var transaction = await Db.Database.BeginTransactionAsync();
        var offer = await Db.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);
        if (offer is null) return NotFound(Error("Offer not found."));
        if (offer.Status != OfferStatus.Draft)
            return Conflict(Error("Only draft offers can be deleted."));

        var deleted = await Db.Offers
            .Where(o => o.Id == id && o.Status == OfferStatus.Draft)
            .ExecuteDeleteAsync();
        if (deleted == 0)
        {
            await transaction.RollbackAsync();
            return await Db.Offers.AnyAsync(o => o.Id == id)
                ? Conflict(Error("Only draft offers can be deleted."))
                : NotFound(Error("Offer not found."));
        }

        Audit("offer.deleted", User.GetUserId().ToString(), id.ToString(),
              $"Lead: {offer.DemandLeadId}");
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
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

        // Optimistic concurrency. Options are replace-set, so a payload built
        // before a provider quote seeded an option would silently delete it on
        // save. When the client echoes the version it read, a stale write is
        // rejected instead of clobbering (the workspace reloads on 409).
        if (body.Version is { } expectedVersion && expectedVersion != offer.Version)
        {
            return Conflict(new
            {
                error      = "This offer changed since you loaded it — reload and reapply your edit.",
                message    = "A provider quote or another admin updated the offer.",
                statusCode = StatusCodes.Status409Conflict,
                retryable  = true,
            });
        }

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

        // Any accepted edit invalidates versions held by other readers.
        offer.Version++;

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
    [HttpGet("offers/{id:guid}/delivery-preview")]
    public async Task<IActionResult> GetDeliveryPreview(Guid id)
    {
        var offer = await LoadOfferForDelivery(id);
        if (offer is null) return NotFound(Error("Offer not found."));
        if (string.IsNullOrWhiteSpace(offer.DemandLead?.Email))
            return BadRequest(Error("The lead has no email address."));
        if (offer.Options.Count == 0)
            return BadRequest(Error("Add at least one option before previewing."));

        var link = FrontendUrl.Localized(config["AppUrl"], offer.Language, $"offer/{offer.Token}");
        var email = OfferDeliveryComposer.ComposeEmail(offer, link);
        return Ok(new OfferDeliveryPreviewDto(
            new OfferDeliveryRecipientDto(offer.DemandLead.Name, offer.DemandLead.Email.Trim()),
            new OfferDeliveryEmailDto(email.Subject, email.TextBody),
            OfferDeliveryComposer.ToPublic(offer)));
    }

    [HttpPost("offers/{id:guid}/send")]
    public async Task<IActionResult> SendOffer(Guid id)
    {
        var offer = await LoadOfferForDelivery(id);
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
        offer.Version++;

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

        var link = FrontendUrl.Localized(config["AppUrl"], offer.Language, $"offer/{offer.Token}");
        var email = OfferDeliveryComposer.ComposeEmail(offer, link);
        // Reply-To ops inbox — the email explicitly invites the customer to
        // reply with questions, and replies must not vanish into noreply@.
        emailQueue.EnqueueEmail(
            lead.Email.Trim(), email.Subject, email.TextBody,
            htmlBody: null, replyTo: OpsReplyTo);

        return Ok(MapOffer(offer));
    }

    [HttpPost("offers/{id:guid}/confirm-booking")]
    public async Task<IActionResult> ConfirmBooking(Guid id)
    {
        await using var transaction = Db.Database.IsRelational()
            ? await Db.Database.BeginTransactionAsync()
            : null;

        var offer = await LoadOfferForConfirmation(id);
        if (offer is null) return NotFound(Error("Offer not found."));
        if (offer.Status != OfferStatus.Chosen || offer.ChosenOptionId is null)
            return Conflict(Error("The customer has not requested an option."));
        if (offer.Options.All(option => option.Id != offer.ChosenOptionId.Value))
            return Conflict(Error("The requested option no longer exists."));

        var lead = offer.DemandLead;
        if (lead is null)
            return Conflict(Error("The offer's lead no longer exists."));

        if (lead.Status != DemandLeadStatus.Converted)
        {
            DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Converted);
            Audit("offer.booking_confirmed", User.GetUserId().ToString(), offer.Id.ToString(),
                  $"Lead: {lead.Id}, option: {offer.ChosenOptionId}");
            await Db.SaveChangesAsync();
        }

        if (transaction is not null)
            await transaction.CommitAsync();
        return Ok(MapOffer(offer));
    }

    // ─── Provider outreach ────────────────────────────────────────────────────

    [HttpPost("leads/{id:guid}/outreach/preview")]
    public async Task<IActionResult> PreviewOutreach(
        Guid id, [FromBody] OutreachPreviewRequest body)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var requestedIds = (body.SupplierIds ?? []).Distinct().ToList();
        if (requestedIds.Count == 0)
            return BadRequest(Error("supplierIds must contain at least one supplier."));

        var suppliers = await Db.Suppliers
            .Where(s => requestedIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);
        var contactedSupplierIds = (await Db.ProviderOutreaches
                .Where(o => o.DemandLeadId == id && requestedIds.Contains(o.SupplierId))
                .Select(o => o.SupplierId)
                .Distinct()
                .ToListAsync())
            .ToHashSet();

        var opsPhone = await OpsPhone.ResolveAsync(Db);

        var recipients = new List<OutreachPreviewItemDto>(requestedIds.Count);
        foreach (var supplierId in requestedIds)
        {
            if (!suppliers.TryGetValue(supplierId, out var supplier))
            {
                recipients.Add(new(
                    supplierId, null, null, null, null, null, "not_found"));
                continue;
            }

            // Preview shows the exact message the provider will receive, including
            // a sample "Submit your price" link. The real per-row token is minted
            // only at send time, so this preview token is ephemeral (never stored).
            var message = ProviderOutreachComposer.Compose(
                lead, supplier, config["AppUrl"], OfferToken.Generate(), opsPhone);
            var email = string.IsNullOrWhiteSpace(supplier.ContactEmail)
                ? null
                : supplier.ContactEmail.Trim();
            var skipReason = email is null
                ? "no_email"
                : contactedSupplierIds.Contains(supplierId)
                    ? "already_contacted"
                    : null;

            recipients.Add(new(
                supplier.Id,
                supplier.Name,
                email,
                message.Language,
                message.Subject,
                message.TextBody,
                skipReason));
        }

        return Ok(new OutreachPreviewResponse(recipients));
    }

    /// <summary>
    /// Availability-request batch: one email per supplier that has a
    /// ContactEmail (the rest are reported back as skipped). The email carries
    /// lead facts only — category, route, details, date — never the customer's
    /// name/email/phone; replies land in the ops inbox via Reply-To.
    /// The sending itself lives in <see cref="IConciergeOutreachService"/>, which
    /// the automatic fan-out on concierge intake also uses — one implementation,
    /// so admin-sent and auto-sent outreach can never drift apart.
    /// </summary>
    [HttpPost("leads/{id:guid}/outreach")]
    public async Task<IActionResult> SendOutreach(Guid id, [FromBody] OutreachRequest body)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var requestedIds = (body.SupplierIds ?? []).Distinct().ToList();
        if (requestedIds.Count == 0)
            return BadRequest(Error("supplierIds must contain at least one supplier."));

        var result = await outreachService.SendAsync(
            lead, requestedIds, body.Resend, User.GetUserId().ToString());

        if (result.SerializationConflict)
            return Conflict(new
            {
                error = "Provider outreach changed concurrently.",
                message = "Retry the request.",
                statusCode = StatusCodes.Status409Conflict,
                retryable = true,
            });

        return Ok(new
        {
            sent = result.Sent
                .Select(s => MapOutreach(s.Row, s.SupplierName))
                .ToList(),
            skipped = result.Skipped
                .Select(s => (object)new
                {
                    supplierId   = s.SupplierId,
                    supplierName = s.SupplierName,
                    reason       = s.Reason,
                })
                .ToList(),
        });
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

    private async Task<Offer?> LoadOfferForConfirmation(Guid id)
    {
        if (!Db.Database.IsRelational())
        {
            return await Db.Offers
                .Include(o => o.Options).ThenInclude(option => option.Supplier)
                .Include(o => o.DemandLead)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        var offer = await Db.Offers
            .FromSqlInterpolated(
                $"""SELECT * FROM "Offers" WHERE "Id" = {id} FOR UPDATE""")
            .SingleOrDefaultAsync();
        if (offer is null)
            return null;

        var lead = await Db.DemandLeads
            .FromSqlInterpolated(
                $"""SELECT * FROM "DemandLeads" WHERE "Id" = {offer.DemandLeadId} FOR UPDATE""")
            .SingleOrDefaultAsync();
        offer.DemandLead = lead;
        await Db.Entry(offer).Collection(o => o.Options).Query()
            .Include(option => option.Supplier)
            .LoadAsync();
        return offer;
    }

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
        // Echo back on PATCH to make the replace-set write conditional (409 on a
        // stale read) instead of silently clobbering a newly seeded option.
        version        = o.Version,
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
                // Stored provenance, not inferred: true only for options this
                // outreach's quote actually created. (Deriving it from "supplier
                // has quoted" false-positived on admin-authored options for a
                // supplier who also quoted separately.)
                fromProviderQuote  = op.CreatedFromOutreachId != null,
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
        // The provider's answer from the tokenized quote page — all null until
        // they submit one (and on legacy rows sent before quote links existed).
        // Drives the "Quoted {amount} {unit}" outreach-history row.
        quotedAmount       = o.QuotedAmount,
        quotedUnit         = o.QuotedUnit,
        quotedAvailability = o.QuotedAvailability,
        quotedNote         = o.QuotedNote,
        quotedAt           = o.QuotedAt,
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

    private Task<Offer?> LoadOfferForDelivery(Guid id) =>
        Db.Offers
            .Include(o => o.Options).ThenInclude(option => option.Supplier)
            .Include(o => o.DemandLead)
            .FirstOrDefaultAsync(o => o.Id == id);
}
