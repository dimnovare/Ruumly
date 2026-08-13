namespace Ruumly.Backend.Services.Interfaces;

/// <summary>What an auto-send attempt did, and why it did nothing when it didn't.</summary>
public readonly record struct OfferAutoSendResult(bool WasSent, string Reason, int Options)
{
    public static OfferAutoSendResult Sent(int options)  => new(true,  "sent", options);
    public static OfferAutoSendResult Skipped(string r)  => new(false, r, 0);
}

/// <summary>
/// Releases a provider-seeded draft offer to the customer without an admin in
/// the loop. Gated by the <c>offerAutoSend</c> platform setting, which defaults
/// to OFF — see <c>OfferAutoSendService</c> for why.
/// </summary>
public interface IOfferAutoSendService
{
    Task<OfferAutoSendResult> TrySendAsync(Guid offerId, CancellationToken ct = default);
}
