using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Services.Interfaces;

/// <summary>
/// What one delivery webhook actually changed. Returned so the endpoint can log
/// a useful line without a second query.
/// </summary>
/// <param name="Duplicate">The event id had already been processed — nothing was re-applied.</param>
/// <param name="SuppliersFlagged">Suppliers whose ContactEmail was retired (hard bounce / complaint).</param>
/// <param name="SuppliersTouched">Suppliers that got a bounce timestamp (includes soft bounces).</param>
/// <param name="OutreachRowsUpdated">
/// ProviderOutreach rows written: moved off "sent" by a failure, or stamped with
/// a DeliveredAt/OpenedAt by a confirmation. Both supplier counts are 0 for a
/// confirmation — a delivery never touches the supplier.
/// </param>
public sealed record EmailDeliveryOutcome(
    bool Duplicate,
    int SuppliersFlagged,
    int SuppliersTouched,
    int OutreachRowsUpdated)
{
    public static EmailDeliveryOutcome DuplicateEvent { get; } = new(true, 0, 0, 0);
}

/// <summary>
/// Writes an email delivery failure back onto the supplier that owns the address
/// and onto every outreach row that used it, and a delivery confirmation onto
/// the outreach row alone. The single place that decides what a bounce — or a
/// receipt — MEANS, so the webhook controller stays a thin transport shell.
/// </summary>
public interface IEmailDeliveryTracker
{
    /// <summary>
    /// Idempotent, by two different mechanisms. For a FAILURE,
    /// <paramref name="eventId"/> (the Svix message id) is stored with a unique
    /// index, so a Resend redelivery is detected and skipped rather than
    /// re-marking rows. For a CONFIRMATION nothing is stored per event: the
    /// DeliveredAt/OpenedAt stamps are first-write-wins, so a redelivery finds
    /// the timestamp already set and changes nothing.
    /// </summary>
    Task<EmailDeliveryOutcome> RecordAsync(
        string eventId, ResendWebhookEvent evt, CancellationToken ct = default);
}
