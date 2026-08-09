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
