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
/// </summary>
public sealed class EmailDeliveryTracker(
    RuumlyDbContext db,
    ILogger<EmailDeliveryTracker> logger) : IEmailDeliveryTracker
{
    public async Task<EmailDeliveryOutcome> RecordAsync(
        string eventId, ResendWebhookEvent evt, CancellationToken ct = default)
    {
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
