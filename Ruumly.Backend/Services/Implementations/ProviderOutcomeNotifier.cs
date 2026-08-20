using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Closes the loop with the providers who priced a job.
///
/// THE HOLE THIS FILLS. A provider answered a cold request with a real number
/// and then heard nothing — win or lose — because the only party told was the
/// ops inbox. Asking businesses for unpaid pricing work and never reporting the
/// result is the surest way to not get a second quote from them.
///
/// WHO COUNTS AS A PARTICIPANT. Every <see cref="OfferOption"/> on the offer
/// that names a supplier. That includes an admin-authored option (a price
/// relayed by phone) — from the provider's side it is still their quote sitting
/// in front of a customer. It deliberately EXCLUDES the providers who were
/// contacted and never answered: they never entered a decision, and telling
/// them they "lost" something they never quoted for is noise that costs
/// goodwill for nothing.
///
/// THE FOUR GATES, and why each one is where it is:
/// <list type="bullet">
/// <item><b>Already notified</b> — the exactly-once markers on the option
/// (<see cref="OfferOption.ProviderNotifiedSentAt"/> /
/// <see cref="OfferOption.ProviderNotifiedOutcomeAt"/>). <c>SendOffer</c> is
/// repeatable, so without this an admin fixing a typo re-announces the offer to
/// everyone.</item>
/// <item><b>Inbox dedupe</b> — the directory holds one row per BRANCH, so
/// several supplier rows routinely stand behind one head-office inbox. Same
/// hazard and the same shape as <see cref="ConciergeOutreachService"/>: dedupe
/// the ADDRESS, not the id, or one company is told twice about one job. Every
/// option sharing that inbox is still STAMPED, so a later pass does not
/// resurrect the duplicate.</item>
/// <item><b>Dead address</b> — <see cref="Supplier.ContactEmailUnusable"/> is
/// set by the Resend webhook on a hard bounce. Writing to it again cannot
/// arrive and only damages sending reputation.</item>
/// <item><b>No address at all</b> — 226 directory rows have a blank
/// ContactEmail; they are skipped with a reason rather than crashing.</item>
/// </list>
///
/// NOT GATED ON <see cref="Supplier.MarketingOptOutAt"/>, and that is a
/// deliberate, reviewable call. These three letters are TRANSACTIONAL: they
/// answer a price the provider chose to send us, about a job they asked to be
/// considered for. A business that told us to stop sending REQUESTS has not
/// asked to be left hanging on a quote already in flight. If the founder
/// decides otherwise this is one added predicate — see the audit doc.
///
/// FAILURE IS ISOLATED. Every caller invokes this AFTER its own state is
/// committed, and a throw here must never fail the admin's send or the
/// customer's choice: the offer really did go out, the customer really did
/// choose, and reporting a failure would invite the operator to redo an action
/// that already happened. Callers wrap accordingly.
/// </summary>
public class ProviderOutcomeNotifier(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    ILogger<ProviderOutcomeNotifier> logger) : IProviderOutcomeNotifier
{
    public Task<ProviderNotifyResult> NotifyOfferSentAsync(Guid offerId, CancellationToken ct = default) =>
        RunAsync(offerId, forOutcome: false, audience: OutcomeAudience.All, ct);

    public Task<ProviderNotifyResult> NotifyOutcomeAsync(
        Guid offerId, OutcomeAudience audience, CancellationToken ct = default) =>
        RunAsync(offerId, forOutcome: true, audience, ct);

    private async Task<ProviderNotifyResult> RunAsync(
        Guid offerId, bool forOutcome, OutcomeAudience audience, CancellationToken ct)
    {
        var offer = await db.Offers
            .Include(o => o.Options).ThenInclude(option => option.Supplier)
            .Include(o => o.DemandLead)
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);

        if (offer?.DemandLead is not { } lead)
        {
            logger.LogWarning("Provider outcome notify skipped — offer {OfferId} or its lead is missing.", offerId);
            return ProviderNotifyResult.Empty;
        }

        // The outcome pass is meaningless before a choice exists.
        if (forOutcome && offer.ChosenOptionId is null)
            return ProviderNotifyResult.Empty;

        var notified = new List<string>();
        var skipped  = new List<(Guid?, string)>();

        // Resolved ONCE. It was previously read inside the loop, which is an
        // uncached PlatformSettings query per option — and it cannot change
        // mid-pass anyway.
        var replyTo = await OpsInbox.ResolveAsync(db);

        // Address-level dedupe — see the class comment.
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stamped = false;

        // One company can hold two branch rows in the same offer, and only one of
        // them can win. Because the winner and the losers are told in SEPARATE
        // passes, a fresh dedupe set would let the losing branch's "somebody else
        // was chosen" land in the very inbox that just read "the customer chose
        // you" — about the same job. Reserving the winner's address up front is
        // what stops that; the losing sibling is then skipped as duplicate_email
        // and stamped, exactly as it would be inside a single pass.
        if (forOutcome && audience == OutcomeAudience.LosersOnly)
        {
            var winnerAddress = offer.Options
                .FirstOrDefault(o => o.Id == offer.ChosenOptionId)?.Supplier?.ContactEmail?.Trim();
            if (!string.IsNullOrEmpty(winnerAddress)) seenEmails.Add(winnerAddress);
        }

        // Stable order so two branch rows of one company resolve the same way
        // every run, and the surviving recipient is deterministic.
        //
        // THE WINNER GOES FIRST on an outcome pass, and that is load-bearing
        // rather than cosmetic. One company can hold two branch rows in the same
        // offer behind one inbox, and only one of them can win. Since the inbox
        // dedupe below gives the address to whoever reaches it first, plain
        // SortOrder would let the LOSING row claim it — and the company would be
        // told it lost a job it had actually won. Sorting the chosen option to
        // the front makes the good news win the tie, every time.
        var ordered = forOutcome
            ? offer.Options
                .OrderByDescending(o => o.Id == offer.ChosenOptionId)
                .ThenBy(o => o.SortOrder).ThenBy(o => o.Id)
            : offer.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id);

        foreach (var option in ordered)
        {
            var alreadyNotified = forOutcome
                ? option.ProviderNotifiedOutcomeAt is not null
                : option.ProviderNotifiedSentAt is not null;
            if (alreadyNotified) continue;

            // Audience filter, BEFORE the inbox is reserved and before any stamp:
            // a provider outside this pass's audience must stay completely
            // untouched, so the later pass still finds them unnotified and their
            // address still free.
            if (forOutcome && audience != OutcomeAudience.All)
            {
                var isWinner = option.Id == offer.ChosenOptionId;
                var wanted = audience == OutcomeAudience.WinnerOnly ? isWinner : !isWinner;
                if (!wanted) continue;
            }

            if (option.Supplier is not { } supplier)
            {
                // An admin-authored option with no supplier, or a supplier
                // deleted since (DeleteBehavior.SetNull). Nobody to write to —
                // stamp it so it stops being reconsidered on every pass.
                Stamp(option, forOutcome);
                stamped = true;
                skipped.Add((option.SupplierId, "no_supplier"));
                continue;
            }

            var address = supplier.ContactEmail?.Trim() ?? "";
            if (address.Length == 0)
            {
                Stamp(option, forOutcome);
                stamped = true;
                skipped.Add((supplier.Id, "no_email"));
                continue;
            }

            if (supplier.ContactEmailUnusable)
            {
                Stamp(option, forOutcome);
                stamped = true;
                skipped.Add((supplier.Id, "email_bounced"));
                continue;
            }

            if (!seenEmails.Add(address))
            {
                // A sibling branch row already took this inbox on this pass.
                // Stamp anyway: leaving it unstamped would let the next pass
                // deliver the duplicate this one refused.
                Stamp(option, forOutcome);
                stamped = true;
                skipped.Add((supplier.Id, "duplicate_email"));
                continue;
            }

            // CLAIM THE ROW BEFORE COMPOSING ANYTHING.
            //
            // The in-memory `alreadyNotified` check above is advisory only: it
            // reads entity state, and between that read and the SaveChangesAsync
            // at the end of the pass the letters are already handed to Hangfire,
            // which commits each job on its own connection. Two overlapping
            // passes would therefore both see null and both send — and
            // ConfirmBooking makes that reachable rather than theoretical,
            // because its FOR UPDATE lock is released by the commit immediately
            // before this notifier runs, so a second request is let go at exactly
            // the moment the first one starts.
            //
            // A conditional UPDATE ... WHERE marker IS NULL settles it in the
            // database: exactly one caller can win the row, and only the winner
            // sends. This is the same lesson ConciergeOutreachService already
            // learned (it serializes its read-check-write); this file cited that
            // service for the inbox-dedupe idea and originally dropped its
            // concurrency discipline.
            var claimed = await ClaimAsync(option.Id, forOutcome, ct);
            if (claimed == 0)
            {
                // Another pass got there first. Not an error — and deliberately
                // NOT re-added to seenEmails, because that pass is the one that
                // wrote to this inbox.
                skipped.Add((supplier.Id, "already_notified"));
                continue;
            }

            var outcome = forOutcome
                ? (option.Id == offer.ChosenOptionId
                    ? ProviderOutcomeComposer.Outcome.Won
                    : ProviderOutcomeComposer.Outcome.Lost)
                : ProviderOutcomeComposer.Outcome.OfferSent;

            var letter = ProviderOutcomeComposer.Compose(
                outcome, supplier, lead, option.PriceAmount, option.PriceUnit);

            // Keep the tracked entity consistent with the row we just claimed, so
            // a caller reading it back in this same scope sees the stamp.
            Stamp(option, forOutcome);
            notified.Add(address);

            emailQueue.EnqueueEmail(
                address, letter.Subject, letter.TextBody,
                htmlBody: null, replyTo: replyTo);
        }

        // One save for the whole pass. The stamps commit BEFORE nothing —
        // the mail is already queued at this point, which is the correct
        // direction of risk: a crash here re-notifies at worst, whereas
        // stamping first and crashing would silently swallow the letter.
        if (stamped) await db.SaveChangesAsync(ct);

        if (notified.Count > 0 || skipped.Count > 0)
        {
            logger.LogInformation(
                "Provider outcome notify ({Pass}) for offer {OfferId}: {Notified} sent, {Skipped} skipped.",
                forOutcome ? "outcome" : "offer_sent", offerId, notified.Count, skipped.Count);
        }

        return new ProviderNotifyResult(notified, skipped);
    }

    private static void Stamp(OfferOption option, bool forOutcome)
    {
        var now = DateTime.UtcNow;
        if (forOutcome) option.ProviderNotifiedOutcomeAt = now;
        else            option.ProviderNotifiedSentAt    = now;
    }

    /// <summary>
    /// Atomically claim the right to send this option's letter: a conditional
    /// UPDATE that only matches while the marker is still null. Returns the
    /// number of rows affected — 1 means this pass won and must send, 0 means
    /// another pass already did.
    ///
    /// The InMemory provider used by the unit tests cannot execute an
    /// ExecuteUpdate, so there it degrades to the in-memory check the caller
    /// already performed. That is honest rather than lossy: InMemory has no
    /// concurrency to defend against, and the relational path — the only one
    /// that runs in production — keeps the real guarantee.
    /// </summary>
    private async Task<int> ClaimAsync(Guid optionId, bool forOutcome, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (!db.Database.IsRelational())
        {
            var option = await db.OfferOptions.FirstOrDefaultAsync(o => o.Id == optionId, ct);
            if (option is null) return 0;
            var free = forOutcome
                ? option.ProviderNotifiedOutcomeAt is null
                : option.ProviderNotifiedSentAt is null;
            if (!free) return 0;
            Stamp(option, forOutcome);
            await db.SaveChangesAsync(ct);
            return 1;
        }

        return forOutcome
            ? await db.OfferOptions
                .Where(o => o.Id == optionId && o.ProviderNotifiedOutcomeAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ProviderNotifiedOutcomeAt, now), ct)
            : await db.OfferOptions
                .Where(o => o.Id == optionId && o.ProviderNotifiedSentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ProviderNotifiedSentAt, now), ct);
    }
}
