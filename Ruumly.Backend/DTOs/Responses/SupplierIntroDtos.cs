namespace Ruumly.Backend.DTOs.Responses;

/// <summary>
/// A composed supplier-introduction email. <see cref="HtmlBody"/> is the
/// delivered body; <see cref="TextBody"/> is the fallback and what the dry-run
/// preview shows.
/// </summary>
public sealed record SupplierIntroMessage(
    string Language, string Subject, string TextBody, string HtmlBody);

/// <summary>One fully rendered email, shown once per language in a dry run.</summary>
public sealed record SupplierIntroSampleDto(
    string Language,
    Guid SupplierId,
    string SupplierName,
    string Country,
    string To,
    string Subject,
    string TextBody,
    string HtmlBody);

/// <summary>Why a matched supplier will not be emailed, and how many hit it.</summary>
public sealed record SupplierIntroSkipDto(string Reason, int Count);

/// <summary>
/// The delivery schedule. The campaign is drip-fed rather than blasted: a
/// 435-recipient burst from a domain that normally sends a handful of
/// transactional mails a day trips both Resend's rate limit and receiving-side
/// spam heuristics.
/// </summary>
public sealed record SupplierIntroPacingDto(
    int BatchSize,
    int BatchIntervalMinutes,
    int Batches,
    double EmailsPerMinute,
    int SpanMinutes,
    DateTime FirstSendAt,
    DateTime LastSendAt);

/// <summary>
/// One supplier at the delivered/undelivered boundary, echoed by the reset
/// preview. <see cref="Position"/> is 1-based in the original send order, so
/// the last KEPT row should be the last address the sending provider actually
/// accepted — that is the check that the cut point is right.
/// </summary>
public sealed record SupplierIntroBoundaryDto(
    int Position, Guid SupplierId, string Name, string Country, string Email, bool Keeps);

/// <summary>
/// What a reset did, or would do. A dry run changes nothing: it reports the
/// counts and the rows either side of the cut so the boundary can be checked
/// against the sending provider's own log before any stamp is cleared.
/// </summary>
public sealed record SupplierIntroResetResponse(
    bool DryRun,
    int Matched,
    int Kept,
    int WouldClear,
    int Cleared,
    IReadOnlyDictionary<string, int> ByCountry,
    IReadOnlyList<SupplierIntroBoundaryDto> Boundary);

/// <summary>
/// What the founder reviews before approving the campaign. On a dry run
/// <see cref="Sent"/> is 0, nothing is queued and nothing is stamped —
/// <see cref="Samples"/> carries the fully rendered copy.
/// </summary>
public sealed record SupplierIntroCampaignResponse(
    bool DryRun,
    string From,
    string ReplyTo,
    int Matched,
    int WouldSend,
    int Sent,
    IReadOnlyDictionary<string, int> ByCountry,
    IReadOnlyDictionary<string, int> ByLanguage,
    int SkippedTotal,
    IReadOnlyList<SupplierIntroSkipDto> Skipped,
    SupplierIntroPacingDto? Pacing,
    IReadOnlyList<SupplierIntroSampleDto> Samples);
