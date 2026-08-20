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
    IConciergeOutreachService outreachService,
    IProviderOutcomeNotifier outcomeNotifier,
    ILogger<AdminOffersController> logger) : AdminBaseController(db)
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
        // Once sent, the customer holds a link to this exact set of options and
        // an email listing them. Editing it here would rewrite the page under
        // someone who may be reading it, and leave the mail disagreeing with
        // what they see. A provider who quotes after a send opens a new draft
        // (QuoteController) — that, not an edit in place, is how a late option
        // reaches the customer.
        if (offer.Status == OfferStatus.Sent)
            return Conflict(Error("This offer was already sent — start a new draft instead of editing it."));

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

        // Replace-set: a non-null Options list rewrites the whole option set —
        // but by identity, so an option that survives the admin's edit survives
        // as the same row. See ApplyOptions for what rides on that.
        if (body.Options is not null)
        {
            var error = ApplyOptions(offer, body.Options);
            if (error is not null) return BadRequest(Error(error));
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

        // Tell every provider inside this offer that their price is now in front
        // of the customer. Idempotent per option, so the re-send path above
        // (which deliberately refreshes SentAt) does not re-announce it.
        //
        // Isolated: the offer has been saved and the customer's email queued, so
        // a failure here must not present the admin with an error for an action
        // that in fact succeeded — they would send it a second time.
        try
        {
            await outcomeNotifier.NotifyOfferSentAsync(offer.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Provider 'offer sent' notification failed for offer {OfferId} — the send itself succeeded.",
                offer.Id);
        }

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

        // CATCH-UP, not the primary trigger. Every provider in the offer was told
        // the outcome the moment the customer chose (OffersController.ChooseOption
        // — founder decision 2026-08-20). This second pass exists because that one
        // is isolated behind a try/catch: if the mail queue was down at the click,
        // the letters would otherwise never be sent at all.
        //
        // Idempotent per option, so on the ordinary path this does nothing.
        try
        {
            await outcomeNotifier.NotifyOutcomeAsync(offer.Id, OutcomeAudience.All);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Outcome catch-up failed for offer {OfferId} — the booking is confirmed regardless.",
                offer.Id);
        }

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

        var recipients = new List<OutreachPreviewItemDto>(requestedIds.Count);
        // Same inbox-level dedupe the batch applies, in the same iteration order,
        // so ticking two branch rows of one company shows the admin which of them
        // will actually be written to before they press send.
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                lead, supplier, config["AppUrl"], OfferToken.Generate());
            var email = string.IsNullOrWhiteSpace(supplier.ContactEmail)
                ? null
                : supplier.ContactEmail.Trim();
            // Mirrors ConciergeOutreachService.SendAsync, in its order: the
            // opt-out is a promise we made in writing and is refused there
            // ahead of the operational reasons, so the preview never promises
            // a send that the batch will refuse.
            var skipReason = supplier.MarketingOptOutAt is not null
                ? "opted_out"
                : email is null
                    ? "no_email"
                    : supplier.ContactEmailUnusable
                        ? "email_bounced"
                        : contactedSupplierIds.Contains(supplierId)
                            ? "already_contacted"
                            : null;

            // Reserved even when the row is skipped for its own reason — the
            // batch reserves there too, and a sibling must not appear sendable
            // because the row ahead of it was refused.
            if (email is not null && !seenEmails.Add(email) && skipReason is null)
                skipReason = "duplicate_email";

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

        // A RESEND reuses the existing outreach row, so a provider who is already
        // blocked can appear in this result still blocked. Looked up rather than
        // passed as null: "freshly sent, therefore nothing open" is true of a
        // first send and quietly false of a resend.
        var openAsks = await OpenInfoRequestsAsync(id);

        return Ok(new
        {
            sent = result.Sent
                .Select(s => MapOutreach(
                    s.Row, s.SupplierName, openAsks.GetValueOrDefault(s.Row.Id)))
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

        var openAsks = await OpenInfoRequestsAsync(id);
        return Ok(rows
            .Select(r => MapOutreach(r.Row, r.SupplierName, openAsks.GetValueOrDefault(r.Row.Id)))
            .ToList());
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
        // Scoped to this one row, not the lead: an admin re-typing a status must
        // not silently drop the blocked marker off the row they just edited.
        var openAsk = await Db.ProviderInfoRequests
            .AsNoTracking()
            .Where(r => r.ProviderOutreachId == row.Id && r.ResolvedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        return Ok(MapOutreach(row, supplierName, openAsk));
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

    private static object MapOutreach(
        ProviderOutreach o, string? supplierName, ProviderInfoRequest? openInfoRequest) => new
    {
        id           = o.Id,
        demandLeadId = o.DemandLeadId,
        supplierId   = o.SupplierId,
        supplierName,
        sentTo       = o.SentTo,
        sentAt       = o.SentAt,
        status       = o.Status.ToString().ToLower(),
        note         = o.Note,
        // Did this specific provider actually receive it? The 30-day aggregate in
        // GetLeadMetrics cannot answer that for one row, and the question that
        // started this work was exactly per-row: five Viljandi storage requests
        // reached 18 providers and produced no reply at all, and nothing recorded
        // said whether any of them got the mail.
        //
        // Null means UNKNOWN, never "not delivered" -- every row sent before the
        // Resend webhook existed has both null, and open tracking is a separate
        // account-level setting that may never have been on. Read the doc comments
        // on ProviderOutreach.DeliveredAt/OpenedAt before rendering either as a
        // fact about the provider.
        deliveredAt  = o.DeliveredAt,
        openedAt     = o.OpenedAt,
        // The provider's answer from the tokenized quote page — all null until
        // they submit one (and on legacy rows sent before quote links existed).
        // Drives the "Quoted {amount} {unit}" outreach-history row.
        quotedAmount       = o.QuotedAmount,
        quotedUnit         = o.QuotedUnit,
        quotedAvailability = o.QuotedAvailability,
        quotedNote         = o.QuotedNote,
        quotedAt           = o.QuotedAt,
        // The provider's recorded NO, from the quote page's decline action.
        //
        // Carried here for the same reason the info-request is: the row's status
        // already says `declined`, and the reason is what that word means. Two of
        // the five reasons (wrong_area, not_our_service) are not about this lead
        // at all — they say the DIRECTORY ROW is mis-filed and every future
        // fan-out to this provider is wasted until someone fixes it. That is
        // worth more than the decline itself, and it is invisible unless the
        // workspace prints it.
        declineReason = o.DeclineReason,
        declineNote   = o.DeclineNote,
        declinedAt    = o.DeclinedAt,
        // The provider's OPEN "I cannot price this yet" (ProviderInfoRequest),
        // null once ops resolves it.
        //
        // It rides on the outreach row rather than on the lead, and rather than
        // on a queue endpoint of its own, because the block and its reason are
        // ONE fact: the row's status is already `needsinfo`, and the ask is what
        // that word means. Split across two payloads they refetch independently,
        // so the workspace would spend real windows rendering "blocked" with no
        // question under it, or a question the operator just closed. It is also
        // the only join that is correct — the ask names an OUTREACH, not a
        // supplier (see ProviderInfoRequest.ProviderOutreachId: one company can
        // be contacted twice about the same lead, and only one of those links is
        // the one to answer).
        infoRequest = openInfoRequest is null ? null : (object)new
        {
            id      = openInfoRequest.Id,
            // Re-validated on the way out by Parse, so a slug this build no
            // longer knows cannot reach the UI as an unlabelled chip.
            reasons = InfoRequestReasons.Parse(openInfoRequest.ReasonsJson),
            note    = openInfoRequest.Note,
            askedAt = openInfoRequest.CreatedAt,
        },
    };

    /// <summary>
    /// The open asks for one lead, keyed by the outreach they block.
    ///
    /// Batched rather than resolved per row: the workspace renders every outreach
    /// row for a lead at once, and a per-row lookup would be an N+1 on the one
    /// query the operator waits for.
    ///
    /// Newest wins per outreach. The quote endpoint add-or-updates a single open
    /// row, so there is normally exactly one — but nothing in the schema ENFORCES
    /// that (no unique index), and a plain ToDictionary would throw on the day a
    /// second one appears, taking the whole outreach list down with it. The ask
    /// they sent most recently is also the one worth answering.
    /// </summary>
    private async Task<Dictionary<Guid, ProviderInfoRequest>> OpenInfoRequestsAsync(
        Guid leadId, CancellationToken ct = default)
    {
        var rows = await Db.ProviderInfoRequests
            .AsNoTracking()
            .Where(r => r.DemandLeadId == leadId && r.ResolvedAt == null)
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ProviderOutreachId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());
    }

    /// <summary>
    /// Vets the whole payload before a single row is touched — a rejected save
    /// must leave the offer exactly as the admin last left it, not half-applied.
    /// </summary>
    private static string? ValidateOptions(List<OfferOptionInput> inputs)
    {
        foreach (var input in inputs)
            if (string.IsNullOrEmpty(input.Title?.Trim()))
                return "Every option needs a title.";
        return null;
    }

    /// <summary>
    /// Copies the fields the client owns onto an option row.
    /// CreatedFromOutreachId is pointedly not among them: provenance is a fact
    /// the quote endpoint records, never something a payload gets to claim.
    /// </summary>
    private static void CopyInto(OfferOption target, OfferOptionInput input, int index)
    {
        var title = input.Title.Trim();
        target.SupplierId         = input.SupplierId;
        target.SupplierLocationId = input.SupplierLocationId;
        target.Title              = title.Length > 200 ? title[..200] : title;
        target.PriceAmount        = input.PriceAmount;
        target.PriceUnit          = Clamp(input.PriceUnit, 40);
        target.Notes              = Clamp(input.Notes, 2000);
        // Explicit sort orders win (including an explicit 0);
        // otherwise keep the payload order.
        target.SortOrder          = input.SortOrder ?? index;
    }

    private static (List<OfferOption>? Options, string? Error) BuildOptions(
        Guid offerId, List<OfferOptionInput> inputs)
    {
        if (ValidateOptions(inputs) is { } error) return (null, error);

        var options = new List<OfferOption>();
        for (var i = 0; i < inputs.Count; i++)
        {
            // Any Id on the input is ignored: the offer is being created, so
            // there is no row to preserve, and letting a caller choose the key
            // would let a payload land on top of someone else's option.
            var option = new OfferOption { Id = Guid.NewGuid(), OfferId = offerId };
            CopyInto(option, inputs[i], i);
            options.Add(option);
        }
        return (options, null);
    }

    /// <summary>
    /// Applies a replace-set option payload to a tracked offer, matched on the
    /// ids the client read back with it. Membership is still replace-set — an
    /// option the payload drops is deleted — but an option the payload keeps
    /// keeps its ROW, and with it CreatedFromOutreachId.
    ///
    /// Rebuilding every row on every save severed that link, and it is the only
    /// thing tying an option to the provider quote that seeded it. A provider
    /// correcting their price then matched nothing and was appended as a SECOND
    /// option — the same company twice, at two prices, on the page the customer
    /// reads. It also erased the "from provider quote" badge on the very screen
    /// where the admin decides which numbers are real quotes and which are their
    /// own placeholders, and blinded the auto-send rule, which counts
    /// quote-seeded options to decide whether an offer is ready to go out.
    /// </summary>
    private string? ApplyOptions(Offer offer, List<OfferOptionInput> inputs)
    {
        if (ValidateOptions(inputs) is { } error) return error;

        // Snapshot BEFORE anything is added: relationship fixup drops newly
        // added options straight into offer.Options, and a row that was never
        // in the payload's "before" picture must not be read as one it dropped.
        var existing = offer.Options.ToDictionary(option => option.Id);
        var kept     = new HashSet<Guid>();

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            // An id only claims a row if the row is on THIS offer and no earlier
            // input claimed it already. Everything else — no id, a foreign id, a
            // repeat — is just a new option; there is nothing here worth failing
            // an admin's save over.
            if (input.Id is { } id && existing.TryGetValue(id, out var option) && kept.Add(id))
            {
                CopyInto(option, input, i);
                continue;
            }

            var added = new OfferOption { Id = Guid.NewGuid(), OfferId = offer.Id };
            CopyInto(added, input, i);
            // Add through the DbSet, NOT offer.Options: the row carries a
            // pre-generated Guid, so nav-fixup discovery would track it as
            // Modified (key set ⇒ "existing") and UPDATE a row that isn't there.
            // Relationship fixup still puts it into offer.Options for us.
            Db.OfferOptions.Add(added);
        }

        var dropped = existing.Values.Where(option => !kept.Contains(option.Id)).ToList();
        Db.OfferOptions.RemoveRange(dropped);
        foreach (var option in dropped) offer.Options.Remove(option);

        return null;
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
