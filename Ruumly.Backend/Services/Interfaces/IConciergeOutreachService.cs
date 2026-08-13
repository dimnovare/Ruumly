using Ruumly.Backend.Models;

namespace Ruumly.Backend.Services.Interfaces;

/// <summary>A provider that actually received an availability request.</summary>
public sealed record OutreachSentRecipient(ProviderOutreach Row, string? SupplierName);

/// <summary>
/// A provider that was NOT emailed. <see cref="Reason"/> is a machine string the
/// admin UI already understands: "not_found" | "no_email" | "already_contacted".
/// </summary>
public sealed record OutreachSkippedRecipient(Guid SupplierId, string? SupplierName, string Reason);

public sealed record OutreachSendResult(
    IReadOnlyList<OutreachSentRecipient> Sent,
    IReadOnlyList<OutreachSkippedRecipient> Skipped,
    bool SerializationConflict)
{
    public static OutreachSendResult Empty { get; } = new([], [], false);

    /// <summary>A concurrent outreach won the Serializable race — nothing was
    /// written and nothing was emailed; the caller should retry.</summary>
    public static OutreachSendResult Conflict { get; } = new([], [], true);
}

/// <summary>
/// What the automatic fan-out did for one freshly created concierge lead —
/// reported verbatim in the ops alert so the admin instantly knows whether the
/// lead still needs to be worked by hand.
/// </summary>
/// <param name="Emailed">Providers that received an availability request.</param>
/// <param name="SkippedNoEmail">Nearby, category-matching providers we could not
/// reach because they have no contact email on file.</param>
/// <param name="RadiusKm">The radius the candidates were finally taken from.</param>
/// <param name="SkipReason">null when the fan-out ran; otherwise
/// "disabled" | "category_any" | "no_candidates" | "conflict" | "failed".</param>
public sealed record AutoOutreachSummary(
    int Emailed,
    int SkippedNoEmail,
    double RadiusKm,
    string? SkipReason,
    IReadOnlyList<string> Providers)
{
    public static AutoOutreachSummary Skipped(
        string reason, int skippedNoEmail = 0, double radiusKm = 0) =>
        new(0, skippedNoEmail, radiusKm, reason, []);

    /// <summary>The fan-out line for the ops alert (English — ops-facing).</summary>
    public string Describe() => SkipReason switch
    {
        "disabled" =>
            "Auto-outreach: OFF (conciergeAutoOutreach=false) — contact providers manually.",
        // Reached only when NOTHING routable was asked for (an insurance-only
        // request, say). A visitor who picked several services still fans out —
        // on each of them — so this line no longer means "more than one".
        "category_any" =>
            "Auto-outreach: skipped — the request names no service we can route, so there is "
            + "nothing specific to ask for. Pick the providers manually.",
        "no_candidates" =>
            $"Auto-outreach: no reachable provider within {RadiusKm:0} km"
            + NoEmailSuffix() + " — contact providers manually.",
        "conflict" =>
            "Auto-outreach: skipped — a concurrent outreach was already in flight. "
            + "Check the lead's outreach history.",
        "failed" =>
            "Auto-outreach: FAILED — see the server logs. Contact providers manually.",
        _ =>
            $"Auto-contacted: {Emailed} provider(s) within {RadiusKm:0} km"
            + NoEmailSuffix()
            + (Providers.Count > 0 ? $" — {string.Join(", ", Providers)}" : ""),
    };

    private string NoEmailSuffix() =>
        SkippedNoEmail > 0 ? $" ({SkippedNoEmail} skipped: no email)" : "";
}

/// <summary>
/// The single path that emails providers about a <see cref="DemandLead"/>.
/// Both the admin batch (POST /api/admin/leads/{id}/outreach) and the automatic
/// fan-out on concierge intake go through it, so their behaviour cannot drift.
/// </summary>
public interface IConciergeOutreachService
{
    /// <summary>
    /// Sends one availability request per supplier that has a contact email and
    /// has not been contacted for this lead yet (unless <paramref name="resend"/>).
    /// Persists a ProviderOutreach row per recipient inside a Serializable
    /// transaction and only enqueues the emails after that transaction commits.
    /// </summary>
    Task<OutreachSendResult> SendAsync(
        DemandLead lead,
        IReadOnlyList<Guid> supplierIds,
        bool resend,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Picks up to <c>conciergeAutoOutreachMax</c> nearby, category-matching
    /// providers and sends them the availability request immediately. Called on
    /// concierge lead creation — the customer's request must never wait for an
    /// admin to open the workspace.
    ///
    /// A request naming several services is searched once per service and the
    /// shared quota is spread across them, so each of the things the customer
    /// asked for gets somebody who actually does it. Only a request that names
    /// nothing routable is left for the admin.
    /// </summary>
    Task<AutoOutreachSummary> AutoFanOutAsync(DemandLead lead, CancellationToken ct = default);
}
