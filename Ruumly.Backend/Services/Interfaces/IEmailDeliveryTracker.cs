using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Services.Interfaces;

/// <summary>
/// What one delivery-failure webhook actually changed. Returned so the endpoint
/// can log a useful line without a second query.
/// </summary>
/// <param name="Duplicate">The event id had already been processed — nothing was re-applied.</param>
/// <param name="SuppliersFlagged">Suppliers whose ContactEmail was retired (hard bounce / complaint).</param>
/// <param name="SuppliersTouched">Suppliers that got a bounce timestamp (includes soft bounces).</param>
/// <param name="OutreachRowsUpdated">ProviderOutreach rows moved off "sent".</param>
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
/// and onto every outreach row that used it. The single place that decides what a
/// bounce MEANS, so the webhook controller stays a thin transport shell.
/// </summary>
public interface IEmailDeliveryTracker
{
    /// <summary>
    /// Idempotent: <paramref name="eventId"/> (the Svix message id) is stored with
    /// a unique index, so a Resend redelivery of the same event is detected and
    /// skipped rather than re-marking rows.
    /// </summary>
    Task<EmailDeliveryOutcome> RecordAsync(
        string eventId, ResendWebhookEvent evt, CancellationToken ct = default);
}
