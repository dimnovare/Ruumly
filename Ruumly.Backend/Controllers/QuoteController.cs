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
    IBackgroundEmailQueue emailQueue,
    IOfferAutoSendService autoSend,
    IStorageService storage,
    ILogger<QuoteController> logger) : ControllerBase
{
    /// <summary>Upper bound for a submitted price — keeps numeric(18,4) from overflowing into a 500.</summary>
    private const decimal MaxPriceAmount = 1_000_000m;

    /// <summary>Lead states that take no further quotes (the request is closed).</summary>
    private static readonly DemandLeadStatus[] TerminalLeadStatuses =
    [
        DemandLeadStatus.Converted,
        DemandLeadStatus.Dismissed,
        DemandLeadStatus.Unmatched,
    ];

    /// <summary>
    /// The provider's view of what they are quoting. No customer PII — only the
    /// structured ask (category, city/route, date, details) and the provider's
    /// own already-submitted quote for prefill. 404 for unknown tokens.
    /// </summary>
    // Read-only and sends no email — the generous public read policy, the same
    // one the public offer page uses. Rendering the form must never spend a
    // permit that the provider's submit (or a customer's request) needs.
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("search")]
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

        // The provider's own open "I can't price this yet" flag, so re-opening the
        // link shows that we have it rather than inviting them to send it again.
        // UNRESOLVED only: once ops has answered the question the page has nothing
        // left to say about it and should go back to simply asking for a price.
        var openInfo = await db.ProviderInfoRequests
            .AsNoTracking()
            .Where(r => r.ProviderOutreachId == outreach.Id && r.ResolvedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        var infoRequest = openInfo is null
            ? null
            : new PublicQuoteInfoRequestDto(
                InfoRequestReasons.Parse(openInfo.ReasonsJson), openInfo.Note, openInfo.CreatedAt);

        return Ok(new PublicQuoteDto(
            new PublicQuoteProviderDto(providerName),
            new PublicQuoteLeadDto(
                ServiceCategories.SlugFor(lead.Category),
                // City, never FromAddress/ToAddress. The lead may now carry the
                // customer's street address; a provider gets it only after the
                // customer accepts their offer, and this page is open to anyone
                // holding a token we emailed to a stranger.
                lead.City, lead.ToCity, lead.NeedDate, lead.Details,
                LeadPhotos.Count(lead.PhotoKeysJson),
                // Structured, so the page can draw the answers as chips in the
                // provider's own language instead of showing them as whatever
                // language the customer happened to fill the form in.
                //
                // The list rides along only where it says something the single
                // Option cannot — see PublicQuoteScopeDto. That keeps the wire
                // shape of every single-answer question byte-for-byte what a
                // service-worker-cached quote page already knows how to read.
                LeadScope.Answers(lead.ScopeJson)
                    .Select(a => new PublicQuoteScopeDto(
                        a.QuestionId, a.Option, a.Options.Count > 1 ? a.Options : null))
                    .ToList()),
            "EUR",
            alreadySubmitted,
            existing,
            // Lets the page render the closed state up front instead of only
            // discovering it when the submit 409s.
            Closed: TerminalLeadStatuses.Contains(lead.Status),
            InfoRequested: infoRequest is not null,
            InfoRequest: infoRequest,
            // A recorded "no" outranks the price form: re-opening the link shows
            // the decline we hold instead of asking the same question again.
            Declined: outreach.Status == ProviderOutreachStatus.Declined));
    }

    /// <summary>
    /// Streams one of the customer's photos to the holder of this quote token.
    ///
    /// Addressed by INDEX, not by storage key: the page is public-with-a-token,
    /// and publishing private-bucket keys to everyone who can open it would
    /// invite exactly the probing that keeping them private is meant to prevent.
    /// The index is resolved against THIS lead's own list, so a token can only
    /// ever reach the photos of the request it was minted for.
    ///
    /// Same 404-for-everything contract as the rest of this controller: an
    /// unknown token, a closed lead and an out-of-range index are
    /// indistinguishable from outside.
    /// </summary>
    [HttpGet("{token}/photos/{index:int}")]
    [AllowAnonymous]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuotePhoto(string token, int index, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || index < 0) return NotFound();

        var outreach = await db.ProviderOutreaches
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.QuoteToken == token, ct);
        if (outreach is null) return NotFound();

        var lead = await db.DemandLeads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == outreach.DemandLeadId, ct);
        if (lead is null) return NotFound();

        var keys = LeadPhotos.Keys(lead.PhotoKeysJson);
        if (index >= keys.Count) return NotFound();

        var bytes = await storage.DownloadPrivateAsync(keys[index]);
        if (bytes is null) return NotFound();

        // Everything stored by this feature is re-encoded JPEG — see
        // LeadPhotoNormalizer — so the type is known rather than sniffed.
        // no-store: a customer's home should not linger in a shared cache.
        Response.Headers.CacheControl = "private, no-store";
        return File(bytes, "image/jpeg");
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
    [EnableRateLimiting("provider-quote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitQuote(string token, [FromBody] SubmitQuoteRequest req)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound(new { error = "Quote not found." });

        // Validate BEFORE any lookup/mutation.
        if (req is null)
            return BadRequest(new { error = "A quote is required." });
        if (req.PriceAmount < 0)
            return BadRequest(new { error = "Price cannot be negative." });
        // Bounded so an absurd figure is a clean 400 rather than an overflow of
        // the numeric(18,4) column surfacing as an unhandled 500 on a public
        // endpoint. No real storage/moving quote approaches this.
        if (req.PriceAmount > MaxPriceAmount)
            return BadRequest(new { error = "Price is too large." });
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
            var (result, email, offerId) = await ApplyQuoteAsync(outreach, lead, req);
            EnqueueOps(email);
            await TryAutoSendAsync(offerId);
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

                var (result, email, offerId) = await ApplyQuoteAsync(outreach, lead, req);
                await transaction.CommitAsync();
                EnqueueOps(email);
                // Strictly after the commit: auto-send reads the offer back on
                // its own, and inside the serializable transaction it would
                // either deadlock against these locks or act on rows that a
                // retry is about to roll back.
                await TryAutoSendAsync(offerId);
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
    /// The second action on the quote page: "I cannot price this from what you
    /// sent me — here is what is missing."
    ///
    /// Until this existed the page offered submitting a price and nothing else,
    /// so a provider who could not do that had to reply to the email instead.
    /// That reply lands in a shared ops inbox as free text with nothing on it
    /// naming the lead — which is exactly what happened on 2026-08-17, when
    /// Adduco answered a live Haapsalu move with "selle info pealt adekvaatset
    /// pakkumist paraku ei saa teha" and asked whether both ends were the same
    /// address, plus photos. It cost a full round trip on a job the customer
    /// needed that week, and to every metric we have that outreach still counted
    /// as silence.
    ///
    /// Records a <see cref="ProviderInfoRequest"/>, moves the outreach off the
    /// silent states to NeedsInfo, and alerts ops with the lead reference in the
    /// subject so the mail threads with the original outreach. It does NOT email
    /// the customer: what to ask a person, and in which language, is a human
    /// decision for now.
    ///
    /// Same contract as the rest of this controller — an unknown token is
    /// indistinguishable from a missing one, a closed lead 409s with a reason,
    /// and nothing in the response mentions the customer.
    /// </summary>
    [HttpPost("{token}/need-info")]
    [AllowAnonymous]
    // Same bucket as the price submit: it is the same provider, on the same page,
    // and it likewise costs one outbound ops email per call.
    [EnableRateLimiting("provider-quote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NeedInfo(string token, [FromBody] NeedInfoRequest req)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound(new { error = "Quote not found." });

        // Validate BEFORE any lookup or mutation, exactly like SubmitQuote.
        if (req is null)
            return BadRequest(new { error = "Tell us what is missing." });
        if (HasAngle(req.Note))
            return BadRequest(new { error = "Text fields contain invalid characters." });

        // Unknown/duplicate/blank slugs are dropped rather than refused: a stale
        // checkbox in a cached page must not cost us the reply. What CANNOT be
        // stored is a flag carrying nothing at all — an empty reason set and an
        // empty note is not an answer, it is a row that would sit in the ops
        // queue saying "somebody is blocked on something".
        var reasons = InfoRequestReasons.Normalize(req.Reasons);
        var note    = Clamp(req.Note, 2000);
        if (reasons.Count == 0 && note is null)
            return BadRequest(new { error = "Tell us what is missing." });

        var outreach = await db.ProviderOutreaches
            .FirstOrDefaultAsync(o => o.QuoteToken == token);
        if (outreach is null)
            return NotFound(new { error = "Quote not found." });

        var lead = await db.DemandLeads.FirstOrDefaultAsync(d => d.Id == outreach.DemandLeadId);
        if (lead is null)
            return NotFound(new { error = "Quote not found." });

        // A closed request takes no new work. Answering a question about a lead
        // that is already booked, dismissed or unmatched would put dead work back
        // in the ops queue — the same reasoning as the quote submit, and the same
        // machine-readable reason so the page can say "already closed".
        if (TerminalLeadStatuses.Contains(lead.Status))
        {
            return Conflict(new
            {
                error  = "This request is already closed.",
                reason = "lead_closed",
            });
        }

        // Add-or-update the OPEN request for this outreach rather than appending a
        // row per press. A provider who clicks twice, or comes back to add the
        // detail they forgot, has one question outstanding, not two — and ops
        // closes questions, so two rows would mean closing the same thing twice.
        var request = await db.ProviderInfoRequests
            .Where(r => r.ProviderOutreachId == outreach.Id && r.ResolvedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        if (request is null)
        {
            request = new ProviderInfoRequest
            {
                Id                 = Guid.NewGuid(),
                DemandLeadId       = lead.Id,
                SupplierId         = outreach.SupplierId,
                ProviderOutreachId = outreach.Id,
                CreatedAt          = DateTime.UtcNow,
            };
            db.ProviderInfoRequests.Add(request);
        }

        // Reasons REPLACE (a checkbox set is submitted whole, so unticking one has
        // to mean something) but an EMPTY set does not — that is an omission, not
        // a retraction, and it must not silently erase what they told us the first
        // time. Same rule, same reason, as the quote note above.
        if (reasons.Count > 0) request.ReasonsJson = InfoRequestReasons.Serialize(reasons);
        if (note is not null)  request.Note        = note;

        // "Replied, but blocked" — applied ONLY from a state that means silence.
        // Sent is the one this feature exists to end; NoAnswer is an admin's guess
        // that the provider never came back, which this message disproves. Every
        // other state is a stronger fact already on the record — a submitted price
        // (Replied), a refusal (Declined), a dead address (Bounced/Complained) —
        // and a question must not overwrite any of them.
        if (outreach.Status is ProviderOutreachStatus.Sent or ProviderOutreachStatus.NoAnswer)
            outreach.Status = ProviderOutreachStatus.NeedsInfo;

        await db.SaveChangesAsync();

        // Read back what is now STORED rather than echoing the request: after the
        // merge above those can differ, and the page should render what we
        // actually hold.
        var storedReasons = InfoRequestReasons.Parse(request.ReasonsJson);

        // Strictly after the write commits. The email queue hands the job to
        // Hangfire, which commits on its own connection — so a failed save must
        // never be able to leave an ops alert claiming a provider replied when
        // nothing was recorded. Same ordering, same reason, as
        // ConciergeOutreachService and the quote submit above.
        await EnqueueNeedInfoOpsAlertAsync(lead, outreach, storedReasons, request.Note);

        return Ok(new NeedInfoSubmittedDto(true, storedReasons, request.Note));
    }

    /// <summary>
    /// The provider says NO — the third answer the outreach email has offered
    /// since 2026-08-18 ("a short 'not possible' is a perfectly good answer"),
    /// which until now had nowhere to land: the only way to give it was a
    /// free-text reply into a shared inbox that nothing parsed. So every real
    /// decline was recorded as SILENCE — it kept feeding the provider-silence
    /// metric as evidence the outreach was failing, and the same provider kept
    /// receiving the next lead in range with no memory of their answer.
    ///
    /// A bare decline is a COMPLETE answer (unlike need-info, where an empty
    /// payload says nothing) — reason and note are welcome but optional.
    ///
    /// Same contract as the rest of this controller: unknown token is
    /// indistinguishable from missing, a closed lead 409s with a reason, and a
    /// provider who already QUOTED cannot silently retract the price this way —
    /// that price is live on a draft offer an admin may be about to send, so
    /// withdrawing it is a conversation, not a button.
    /// </summary>
    [HttpPost("{token}/decline")]
    [AllowAnonymous]
    // Same bucket as the price submit and need-info: same provider, same page,
    // one outbound ops email per call.
    [EnableRateLimiting("provider-quote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decline(string token, [FromBody] DeclineQuoteRequest? req)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound(new { error = "Quote not found." });

        if (req is not null && HasAngle(req.Note))
            return BadRequest(new { error = "Text fields contain invalid characters." });

        var outreach = await db.ProviderOutreaches
            .FirstOrDefaultAsync(o => o.QuoteToken == token);
        if (outreach is null)
            return NotFound(new { error = "Quote not found." });

        var lead = await db.DemandLeads.FirstOrDefaultAsync(d => d.Id == outreach.DemandLeadId);
        if (lead is null)
            return NotFound(new { error = "Quote not found." });

        if (TerminalLeadStatuses.Contains(lead.Status))
        {
            return Conflict(new
            {
                error  = "This request is already closed.",
                reason = "lead_closed",
            });
        }

        // A submitted price is a stronger fact than a later "no", and it is
        // already seeded on the lead's draft offer — silently flipping the
        // outreach to Declined would leave a live option whose provenance row
        // says the provider refused. Distinct reason so the page can say
        // "you already sent a price — write to us to withdraw it".
        if (outreach.QuotedAt is not null)
        {
            return Conflict(new
            {
                error  = "A price was already submitted for this request.",
                reason = "already_quoted",
            });
        }

        var reason = DeclineReasons.Normalize(req?.Reason);
        var note   = Clamp(req?.Note, 2000);

        // Idempotent by design: a second press updates the reason/note rather
        // than erroring — the provider changing "no capacity" to "wrong area"
        // is them being MORE helpful, not a conflict. A blank note on a repeat
        // press keeps the earlier text, same as the quote-note rule.
        outreach.Status        = ProviderOutreachStatus.Declined;
        outreach.DeclineReason = reason;
        if (note is not null) outreach.DeclineNote = note;
        outreach.DeclinedAt  ??= DateTime.UtcNow;

        await db.SaveChangesAsync();

        // After the commit, same ordering rule as every alert in this file.
        await EnqueueDeclineOpsAlertAsync(lead, outreach, reason, outreach.DeclineNote);

        return Ok(new QuoteDeclinedDto(true, reason, outreach.DeclineNote));
    }

    /// <summary>
    /// Tells ops a provider said no — with the reason, because two of the five
    /// reasons (wrong area, not our service) mean the DIRECTORY ROW is wrong
    /// and every future fan-out to this provider is wasted until it is fixed.
    /// Reference in the subject, same threading rule as the other alerts.
    /// </summary>
    private async Task EnqueueDeclineOpsAlertAsync(
        DemandLead lead, ProviderOutreach outreach, string? reason, string? note)
    {
        var supplierName = await db.Suppliers
            .Where(s => s.Id == outreach.SupplierId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? "Provider";

        var reference = ProviderOutreachComposer.Reference(lead.Id);
        var route     = string.IsNullOrWhiteSpace(lead.ToCity)
            ? lead.City
            : $"{lead.City} → {lead.ToCity}";
        var category  = ServiceCategories.SlugFor(lead.Category);
        var reasonLine = reason is null
            ? "No reason given — a bare no."
            : DeclineReasons.OpsLabel(reason);

        var opsInbox = await OpsInbox.ResolveAsync(db);
        emailQueue.EnqueueEmail(
            opsInbox,
            $"Ruumly — provider declined ({route}) [{reference}]",
            $"Provider {supplierName} declined the {category} request in {route}.\n\n" +
            $"Reason: {reasonLine}\n" +
            $"Their note: {note ?? "—"}\n\n" +
            $"Lead reference: {reference}\n\n" +
            "No action needed for this provider — the decline is recorded and the quote " +
            "page shows it. If the reason says wrong area or wrong service, fix the " +
            "supplier row so the next fan-out skips them.");
    }

    /// <summary>
    /// Tells ops a provider is blocked, in the one place they already watch.
    ///
    /// The lead reference goes in the SUBJECT: Reply-To on provider mail is a
    /// single shared inbox and two live Tallinn → Tartu moves produce identical
    /// subjects, so without the handle this alert would land with nothing on it
    /// saying which customer it is about — the very failure the feature exists to
    /// fix. Same 8-hex handle the original outreach subject carries, so the two
    /// sit together in a search (and, in most clients, in one thread).
    /// </summary>
    private async Task EnqueueNeedInfoOpsAlertAsync(
        DemandLead lead, ProviderOutreach outreach,
        IReadOnlyList<string> reasons, string? note)
    {
        var supplierName = await db.Suppliers
            .Where(s => s.Id == outreach.SupplierId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? "Provider";

        var reference = ProviderOutreachComposer.Reference(lead.Id);
        var route     = string.IsNullOrWhiteSpace(lead.ToCity)
            ? lead.City
            : $"{lead.City} → {lead.ToCity}";
        var category  = ServiceCategories.SlugFor(lead.Category);
        var missing   = reasons.Count == 0
            ? "  • not itemised — see their note"
            : string.Join("\n", reasons.Select(r => $"  • {InfoRequestReasons.OpsLabel(r)}"));

        var opsInbox = await OpsInbox.ResolveAsync(db);
        emailQueue.EnqueueEmail(
            opsInbox,
            $"Ruumly — provider needs more info ({route}) [{reference}]",
            $"Provider {supplierName} cannot quote the {category} request in {route} yet.\n\n" +
            $"What they are missing:\n{missing}\n\n" +
            $"Their note: {note ?? "—"}\n\n" +
            $"Lead reference: {reference}\n\n" +
            "The customer has NOT been contacted about this. Deciding what to ask them, and in " +
            "which language, is a human call — answer it from the admin CRM → Leads, then mark " +
            "the request resolved.");
    }

    /// <summary>
    /// Applies a validated quote: stores it on the outreach row, flips Status to
    /// Replied, and add-or-updates the SupplierId-keyed option on the lead's
    /// newest Draft offer (creating that draft if none exists). Returns the
    /// thank-you result plus the ops-alert email to enqueue AFTER commit.
    /// </summary>
    private async Task<(IActionResult Result, (string To, string Subject, string Body)? Email, Guid? OfferId)> ApplyQuoteAsync(
        ProviderOutreach outreach, DemandLead? lead, SubmitQuoteRequest req)
    {
        if (lead is null)
            return (NotFound(new { error = "Quote not found." }), null, null);

        // A closed request takes no new quotes: seeding a fresh Draft onto a
        // Converted/Dismissed/Unmatched lead would resurrect dead work in the
        // ops queue. Distinct signal so the page can say "already closed".
        if (TerminalLeadStatuses.Contains(lead.Status))
        {
            return (Conflict(new
            {
                error  = "This request is already closed.",
                reason = "lead_closed",
            }), null, null);
        }

        var amount = req.PriceAmount;
        var unit = Clamp(req.PriceUnit, 40);
        var availability = Clamp(req.Availability, 200);
        var note = Clamp(req.Note, 2000);

        // 1. Record the answer on the outreach row. A blank note leaves any
        //    previously submitted note intact — never destroys text on a re-submit.
        outreach.Status             = ProviderOutreachStatus.Replied;
        outreach.QuotedAmount       = amount;
        outreach.QuotedUnit         = unit;
        outreach.QuotedAvailability = availability;
        if (note is not null) outreach.QuotedNote = note;
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
                // Authorship only (no admin is behind this draft); the per-option
                // "from provider quote" badge comes from CreatedFromOutreachId.
                CreatedBy    = "provider-quote",
            };
            db.Offers.Add(offer);
        }

        // 3. Add-or-update THIS outreach's own option (re-submit updates it, never
        //    duplicates). Keyed on CreatedFromOutreachId rather than SupplierId so
        //    the quote can never overwrite an admin-authored option for the same
        //    supplier — if one exists, this adds a separate option beside it.
        var title = Clamp($"{supplierName} — {lead.City}", 200)!;

        // The provider typed their unit in THEIR language; this option is read by
        // the CUSTOMER. A Latvian yard answering an English-speaking customer
        // submits "/diena", and without this the offer says "60 € /diena" to
        // someone who cannot read it — which is exactly what the first real quote
        // did (Rīga, 2026-08-13) before it was fixed by hand. Only the customer's
        // copy is translated: outreach.QuotedUnit above keeps the provider's own
        // words, so ops can always see what they actually wrote.
        var customerUnit = PriceUnitNormalizer.ToCustomerLanguage(unit, offer.Language);

        var existingOption = offer.Options.FirstOrDefault(o => o.CreatedFromOutreachId == outreach.Id);
        if (existingOption is null)
        {
            var option = new OfferOption
            {
                Id                    = Guid.NewGuid(),
                OfferId               = offer.Id,
                SupplierId            = outreach.SupplierId,
                CreatedFromOutreachId = outreach.Id,
                Title                 = title,
                PriceAmount           = amount,
                PriceUnit             = customerUnit,
                Notes                 = note,
                SortOrder             = offer.Options.Count,   // appended
            };
            // Add through the DbSet, NOT offer.Options: the option carries a
            // pre-generated Guid, so when the parent is already tracked (an
            // existing draft) nav-fixup discovery would mark it Modified — "key
            // set ⇒ existing" — and SaveChanges would UPDATE a nonexistent row,
            // throwing and rolling back the provider's whole quote. Relationship
            // fixup still populates offer.Options via OfferId. Same trap and same
            // remedy as AdminOffersController's replace-set.
            db.OfferOptions.Add(option);
        }
        else
        {
            existingOption.Title       = title;
            existingOption.PriceAmount = amount;
            existingOption.PriceUnit   = customerUnit;
            // A blank note must not wipe the note submitted earlier.
            if (note is not null) existingOption.Notes = note;
        }

        // The option set changed — a stale admin PATCH must now 409 rather than
        // silently replace-set this option away.
        offer.Version++;

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

        return (Ok(new QuoteSubmittedDto(true, amount, unit, availability, note)), email, offer.Id);
    }

    private void EnqueueOps((string To, string Subject, string Body)? email)
    {
        if (email is { } e)
            emailQueue.EnqueueEmail(e.To, e.Subject, e.Body);
    }

    /// <summary>
    /// Offers the seeded draft to the auto-send rule, AFTER the provider's quote
    /// is safely committed.
    ///
    /// Wrapped so it can never fail the provider's submit. From the provider's
    /// side the job was done the moment their price was stored; whether we then
    /// chose to release the offer is Ruumly's problem, and turning that into a
    /// 500 would tell them their quote was lost when it was not.
    ///
    /// Does nothing at all unless the founder has switched offerAutoSend on.
    /// </summary>
    private async Task TryAutoSendAsync(Guid? offerId)
    {
        if (offerId is not { } id) return;
        try
        {
            var result = await autoSend.TrySendAsync(id);
            if (result.WasSent)
                logger.LogInformation("Provider quote released offer {OfferId} automatically.", id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Auto-send failed for offer {OfferId}; the quote itself is stored and the " +
                "draft is still in the admin workspace.", id);
        }
    }

    /// <summary>
    /// Cheap "this is prose, not markup" gate on anything a public caller typed.
    /// Shared by both public writes so the two cannot drift apart — a field the
    /// quote form refuses must not be accepted by the form beside it.
    /// </summary>
    private static bool HasAngle(string? s) =>
        s is not null && (s.Contains('<') || s.Contains('>'));

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
