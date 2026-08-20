namespace Ruumly.Backend.Services.Interfaces;

/// <summary>
/// Tells the providers whose quotes are inside an offer what happened to them.
/// See <see cref="Implementations.ProviderOutcomeNotifier"/> for the rules.
/// </summary>
public interface IProviderOutcomeNotifier
{
    /// <summary>
    /// "Your price went to the customer." Call AFTER the send has been
    /// committed. Idempotent per option — safe on a re-send.
    /// </summary>
    Task<ProviderNotifyResult> NotifyOfferSentAsync(Guid offerId, CancellationToken ct = default);

    /// <summary>
    /// "The customer chose you" / "the customer chose somebody else." Call AFTER
    /// the choice has been committed. Idempotent per option.
    /// </summary>
    Task<ProviderNotifyResult> NotifyOutcomeAsync(
        Guid offerId, OutcomeAudience audience, CancellationToken ct = default);
}

/// <summary>
/// Who to tell, and WHY the two verdicts are decoupled.
///
/// A customer clicking "choose" is not the same event as ops confirming that
/// the chosen provider can actually take the job. If the losers are told at the
/// click and the winner then falls through, ops has to go back to a panel it
/// already dismissed — the most expensive possible moment to have been hasty.
///
/// So the default wiring is: <see cref="WinnerOnly"/> the moment the customer
/// chooses (they are the one waiting on an answer, and the letter promises a
/// follow-up rather than a confirmed booking), and <see cref="LosersOnly"/> when
/// ops confirms the booking — by which point "somebody else was chosen" is
/// actually true and final.
/// </summary>
public enum OutcomeAudience
{
    /// <summary>Only the chosen option's provider.</summary>
    WinnerOnly,
    /// <summary>Only the providers who were not chosen.</summary>
    LosersOnly,
    /// <summary>Both, in one pass.</summary>
    All,
}

/// <summary>
/// What a notification pass actually did — returned rather than logged only, so
/// a caller (and a test) can assert on it.
/// </summary>
/// <param name="Notified">Addresses written to.</param>
/// <param name="Skipped">(supplier, reason) for every option not written to.</param>
public readonly record struct ProviderNotifyResult(
    IReadOnlyList<string> Notified,
    IReadOnlyList<(Guid? SupplierId, string Reason)> Skipped)
{
    public static ProviderNotifyResult Empty =>
        new([], []);
}
