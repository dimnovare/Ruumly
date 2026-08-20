using System.Text.Json.Serialization;

namespace Ruumly.Backend.DTOs.Responses;

/// <summary>Provider display name — the only party detail the quote page shows.</summary>
public sealed record PublicQuoteProviderDto(string Name);

/// <summary>
/// One answered scoping question: the question's id and the 1-based position of
/// the chip the customer tapped, e.g. <c>{ question: "movingAccess", option: 3 }</c>.
///
/// SLUG AND NUMBER, never a rendered sentence — the same contract
/// <see cref="PublicQuoteInfoRequestDto"/> uses for its reasons. The quote page
/// already owns a five-language copy deck and renders the provider's own
/// language from it; shipping strings from here would either hard-code one
/// language into the payload or duplicate the deck. Both ends validate against
/// the same catalogue (<see cref="Constants.ScopeQuestions"/>), so a question
/// one side does not know is a chip the other side simply does not draw.
/// </summary>
/// <param name="Option">
/// The FIRST chip of the answer, and the only field an older bundle reads.
/// Kept — rather than replaced by the list below — because the quote page is
/// served through a service worker: a provider who opened the link we emailed
/// them may be running a bundle from before the list existed, and for that
/// bundle this field is the difference between one true fact and a row it drops
/// on the floor.
/// </param>
/// <param name="Options">
/// Every chip of the answer, for the two tick-all-that-apply questions — "a
/// piano AND an aquarium" is what a mover has to be able to read.
///
/// PRESENT ONLY WHEN THERE IS MORE THAN ONE, which is precisely when
/// <paramref name="Option"/> alone would be a half-truth — hence the
/// <c>JsonIgnore</c>, which keeps null OFF the wire rather than on it. Every
/// single-answer question therefore puts exactly the same bytes on the wire as
/// it did before this field existed, and the field's absence is a fact rather
/// than a shrug: it means the compatibility field above already says everything.
/// </param>
public sealed record PublicQuoteScopeDto(
    string Question,
    int Option,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<int>? Options = null);

/// <summary>
/// What the provider is quoting for. Deliberately excludes ALL customer PII
/// (name/email/phone) — only the structured ask the customer submitted. The
/// street address is PII too and is excluded for the same reason: until the
/// customer accepts an offer the provider sees the city, and nothing narrower.
/// </summary>
public sealed record PublicQuoteLeadDto(
    string Category, string City, string? ToCity, DateTime? NeedDate, string? Details,
    /// <summary>
    /// How many photos the customer attached. The page renders that many
    /// &lt;img&gt; tags pointed at /api/quote/{token}/photos/{index} — indexes,
    /// never storage keys, so the private-bucket layout is not published to
    /// anyone holding a quote token.
    /// </summary>
    int PhotoCount = 0,
    /// <summary>
    /// The intake's scoping answers, in catalogue order, so the page can render
    /// chips instead of re-reading them out of the free-text blob in
    /// <c>Details</c>. Always sent (empty for a request with no answers);
    /// nullable only so the parameter can follow one that has a default.
    /// </summary>
    IReadOnlyList<PublicQuoteScopeDto>? Scope = null);

/// <summary>The provider's already-submitted quote (prefill for "update your quote").</summary>
public sealed record PublicQuoteExistingDto(
    decimal? Amount, string? Unit, string? Availability, string? Note);

/// <summary>
/// The provider's OPEN "I can't quote this yet" flag, echoed back so the page
/// can say "you already told us — we're getting it" instead of showing the ask
/// twice. Reasons are <see cref="Constants.InfoRequestReasons"/> slugs; the page
/// renders its own localized labels for them.
/// </summary>
public sealed record PublicQuoteInfoRequestDto(
    IReadOnlyList<string> Reasons, string? Note, DateTime CreatedAt);

/// <summary>
/// GET /api/quote/{token} — the public quote page payload. <c>Closed</c> means
/// the underlying request has ended (booked elsewhere, dismissed or unmatched):
/// the page should show a closed state, and a submit would be rejected 409.
/// <c>InfoRequested</c>/<c>InfoRequest</c> mirror
/// <c>AlreadySubmitted</c>/<c>Existing</c> for the second action on the page —
/// they describe only an UNRESOLVED ask, so once ops answers it the page goes
/// back to normal on its own.
/// </summary>
public sealed record PublicQuoteDto(
    PublicQuoteProviderDto Provider,
    PublicQuoteLeadDto Lead,
    string Currency,
    bool AlreadySubmitted,
    PublicQuoteExistingDto? Existing,
    bool Closed,
    bool InfoRequested = false,
    PublicQuoteInfoRequestDto? InfoRequest = null,
    /// <summary>
    /// The provider already said no through this page. Mirrors the
    /// AlreadySubmitted pattern: the page renders the recorded decline instead
    /// of the price form, so a second visit does not invite a second answer.
    /// </summary>
    bool Declined = false);

/// <summary>POST /api/quote/{token}/decline — echo of the recorded decline.</summary>
public sealed record QuoteDeclinedDto(bool Ok, string? Reason, string? Note);

/// <summary>POST /api/quote/{token} — thank-you echo of the stored quote.</summary>
public sealed record QuoteSubmittedDto(
    bool Ok, decimal Amount, string? Unit, string? Availability, string? Note);

/// <summary>
/// POST /api/quote/{token}/need-info — echo of what was recorded. Reasons come
/// back NORMALIZED (unknown slugs dropped, de-duplicated), so the page can show
/// the provider exactly what we understood rather than what they sent.
/// </summary>
public sealed record NeedInfoSubmittedDto(
    bool Ok, IReadOnlyList<string> Reasons, string? Note);
