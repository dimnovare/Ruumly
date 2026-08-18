using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Applies a Resend delivery failure to our own data.
///
/// Two writes matter to ops:
///  1. the SUPPLIER — a hard bounce or complaint sets ContactEmailUnusable, which
///     is what makes auto fan-out skip them and pick the next candidate;
///  2. the OUTREACH ROWS — so the lead workspace stops claiming "sent" for a mail
///     that never arrived.
///
/// Matching is by address, not by message id: we do not store Resend's email_id
/// on the outreach row, so an address-level match is the honest join. For a HARD
/// failure every still-open request to that address is marked — the mailbox is
/// dead, so all of them are dead. A SOFT bounce (full mailbox, greylisting) marks
/// only the newest open row and never retires the address.
///
/// Also records the OPPOSITE signal — delivered and opened — but by completely
/// different rules; see <see cref="RecordConfirmationAsync"/>.
/// </summary>
public sealed class EmailDeliveryTracker(
    RuumlyDbContext db,
    ILogger<EmailDeliveryTracker> logger) : IEmailDeliveryTracker
{
    public async Task<EmailDeliveryOutcome> RecordAsync(
        string eventId, ResendWebhookEvent evt, CancellationToken ct = default)
    {
        // Before the ledger check, because confirmations never reach the ledger.
        if (evt.IsDeliveryConfirmation)
            return await RecordConfirmationAsync(evt, ct);

        if (await db.EmailDeliveryEvents.AnyAsync(e => e.EventId == eventId, ct))
            return EmailDeliveryOutcome.DuplicateEvent;

        var disables = evt.DisablesAddress;
        var now = DateTime.UtcNow;
        var flagged = 0;
        var touched = 0;
        var rowsUpdated = 0;
        Guid? firstSupplierId = null;

        foreach (var recipient in evt.Recipients)
        {
            var suppliers = await db.Suppliers
                .Where(s => s.ContactEmail.ToLower() == recipient)
                .ToListAsync(ct);

            foreach (var supplier in suppliers)
            {
                supplier.ContactEmailBouncedAt    = now;
                supplier.ContactEmailBounceType   = evt.BounceType;
                supplier.ContactEmailBounceReason = evt.Reason;
                supplier.UpdatedAt                = now;
                touched++;
                firstSupplierId ??= supplier.Id;

                if (disables && !supplier.ContactEmailUnusable)
                {
                    supplier.ContactEmailUnusable = true;
                    flagged++;
                }
            }

            var openOutreach = await db.ProviderOutreaches
                .Where(o => o.SentTo.ToLower() == recipient
                            && o.Status == ProviderOutreachStatus.Sent)
                .OrderByDescending(o => o.SentAt)
                .ToListAsync(ct);

            // Soft bounce: only the message that actually failed (best effort =
            // the newest open one). Hard: the address itself is gone.
            var affected = disables ? openOutreach : openOutreach.Take(1).ToList();
            foreach (var row in affected)
            {
                row.Status = evt.Type == ResendWebhookEvent.ComplainedType
                    ? ProviderOutreachStatus.Complained
                    : ProviderOutreachStatus.Bounced;
                row.Note = AppendNote(row.Note, BuildNote(evt, now));
                rowsUpdated++;
                firstSupplierId ??= row.SupplierId;
            }
        }

        db.EmailDeliveryEvents.Add(new EmailDeliveryEvent
        {
            Id                  = Guid.NewGuid(),
            EventId             = eventId,
            EventType           = evt.Type,
            Recipient           = evt.Recipients.FirstOrDefault() ?? string.Empty,
            BounceType          = evt.BounceType,
            BounceSubType       = evt.BounceSubType,
            Reason              = evt.Reason,
            SupplierId          = firstSupplierId,
            OutreachRowsUpdated = rowsUpdated,
            OccurredAt          = evt.OccurredAt,
            ReceivedAt          = now,
        });

        // Surfaced in the admin audit log next to lead.outreach_sent, so the
        // "we mailed them / it bounced" pair reads as one story.
        db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = evt.Type == ResendWebhookEvent.ComplainedType
                ? "email.complained"
                : "email.bounced",
            Actor     = "resend-webhook",
            Target    = firstSupplierId?.ToString() ?? evt.Recipients.FirstOrDefault() ?? "unknown",
            Detail    = $"{evt.Recipients.FirstOrDefault() ?? "?"} — {evt.BounceType ?? "unknown"}"
                      + $"; suppliers flagged: {flagged}; outreach rows: {rowsUpdated}"
                      + (evt.Reason is null ? "" : $"; {evt.Reason}"),
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Resend retried while the first delivery was still in flight; the
            // unique index on EventId is the arbiter and the other request won.
            logger.LogInformation(
                "Resend webhook {EventId} lost the idempotency race — already applied.", eventId);
            return EmailDeliveryOutcome.DuplicateEvent;
        }

        return new EmailDeliveryOutcome(false, flagged, touched, rowsUpdated);
    }

    /// <summary>
    /// email.delivered / email.opened → a timestamp on the outreach row, and
    /// nothing else. This is the other half of the funnel: SentAt only says we
    /// handed the mail to Resend, so until now "18 providers, zero replies" and
    /// "18 providers, zero deliveries" were the same stored fact.
    ///
    /// Four things this deliberately does NOT do, each of which would be the
    /// obvious thing to write:
    ///
    /// 1. It never touches <c>Status</c>. Status carries the PROVIDER's intent —
    ///    Replied, Declined, NeedsInfo — set by a human or by the provider's own
    ///    quote submission, and AdminLeadsController.GetLeadMetrics counts
    ///    Replied as a supplier MATCH. A delivery receipt is a fact about the
    ///    mail server, not about the provider; letting it write Status would
    ///    overwrite a real reply with a receipt and quietly deflate the concierge
    ///    north-star. That is also why rows are matched regardless of status
    ///    (unlike the bounce path, which only touches still-open rows): a
    ///    provider who already replied still deserves an honest delivered count,
    ///    and there is no status this could corrupt because it writes none.
    ///
    /// 2. It never sets <c>ContactEmailUnusable</c> or touches the supplier at
    ///    all. That flag means "proven undeliverable" and a delivery is its
    ///    exact opposite.
    ///
    /// 3. It writes no AuditLog and no EmailDeliveryEvent row. A bounce is
    ///    exceptional and worth a permanent row explaining why an address was
    ///    retired; a delivery happens to every single email we send (twice, once
    ///    the opens arrive), and one row per message would bury the bounces in
    ///    the same tables that exist to make them findable. The signal belongs
    ///    in the metrics endpoint, which aggregates, not in a per-event log.
    ///
    /// 4. An open does not backfill <c>DeliveredAt</c>, even though opening a
    ///    mail obviously proves it was delivered. Each column records what
    ///    Resend actually reported, so "delivered" keeps meaning "Resend
    ///    confirmed delivery" rather than "we inferred it" — inference here is
    ///    how a metric stops being checkable against the provider's dashboard.
    ///
    /// IDEMPOTENCE comes from first-write-wins on the field, not from the
    /// EventId ledger: a redelivered receipt lands on the same row, finds the
    /// stamp already there and changes nothing. Note that this is also why the
    /// row choice must NOT skip rows that already carry the stamp — "stamp the
    /// next unstamped row instead" would make Resend's retry of one event write
    /// a second, invented, receipt onto a different lead's outreach.
    /// </summary>
    private async Task<EmailDeliveryOutcome> RecordConfirmationAsync(
        ResendWebhookEvent evt, CancellationToken ct)
    {
        var stamped = 0;

        foreach (var recipient in evt.Recipients)
        {
            // Same join as the bounce path — address, newest first — because the
            // same limitation applies: Resend's email_id is not stored on the
            // row, so the address is all we have to match on. Unlike a bounce,
            // one receipt describes exactly one message, so exactly one row is
            // stamped.
            var rows = await db.ProviderOutreaches
                .Where(o => o.SentTo.ToLower() == recipient)
                .OrderByDescending(o => o.SentAt)
                .ThenBy(o => o.Id)
                .ToListAsync(ct);

            // Silence is normal and not an error: customer acknowledgements,
            // the intro campaign and every other mail we send also generate
            // receipts, and none of them has an outreach row.
            if (rows.Count == 0) continue;

            var at = evt.OccurredAt ?? DateTime.UtcNow;

            // The newest row that could actually have produced this receipt — a
            // message cannot be delivered before it was sent. Without the bound,
            // a receipt for Monday's request would land on Tuesday's request to
            // the same provider. Falls back to the newest row when the event
            // carries no timestamp, or when clock skew puts every row after it.
            var row = rows.FirstOrDefault(r => r.SentAt <= at) ?? rows[0];

            if (evt.Type == ResendWebhookEvent.DeliveredType)
            {
                if (row.DeliveredAt is not null) continue;
                row.DeliveredAt = at;
            }
            else
            {
                // FIRST open only. Resend reports every open, and a provider who
                // reopens the mail a week later must not rewrite when they first
                // read it — that timestamp is the one that says whether the ask
                // was seen while it was still actionable.
                if (row.OpenedAt is not null) continue;
                row.OpenedAt = at;
            }

            stamped++;
        }

        if (stamped > 0) await db.SaveChangesAsync(ct);

        // Not a duplicate in the ledger sense — there is no ledger row to be a
        // duplicate of. A redelivery simply reports zero rows changed.
        return new EmailDeliveryOutcome(false, 0, 0, stamped);
    }

    private static string BuildNote(ResendWebhookEvent evt, DateTime at)
    {
        var label = evt.Type == ResendWebhookEvent.ComplainedType
            ? "Spam complaint"
            : $"{(evt.BounceType == "hard" ? "Hard" : "Soft")} bounce";
        var reason = string.IsNullOrWhiteSpace(evt.Reason) ? "" : $": {evt.Reason}";
        return $"[{at:yyyy-MM-dd HH:mm} UTC] {label}{reason}";
    }

    /// <summary>Keeps whatever the admin typed; the bounce is appended, never overwritten.</summary>
    private static string AppendNote(string? existing, string addition)
    {
        var combined = string.IsNullOrWhiteSpace(existing) ? addition : $"{existing}\n{addition}";
        return combined.Length <= 2000 ? combined : combined[^2000..];
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }

        return false;
    }
}
