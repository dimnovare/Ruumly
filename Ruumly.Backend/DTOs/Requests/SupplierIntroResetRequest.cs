namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// Un-stamps <c>Supplier.IntroEmailSentAt</c> for suppliers a campaign run
/// CLAIMED but whose mail was never actually delivered, so the campaign can
/// pick them up again.
///
/// Why this exists: the campaign stamps every recipient inside a Serializable
/// transaction and commits BEFORE queueing, because the unrecoverable failure
/// is mail going out with nothing recording it. The safe failure — stamped but
/// never sent — is exactly what a provider-side send cap produces: on
/// 2026-08-13 a 754-recipient run stamped all 754 and Resend accepted the first
/// 200, leaving 554 marked as contacted who had heard nothing. Hangfire retries
/// three times over ~16 minutes and then parks the job in Failed forever, and
/// the prod dashboard is Development-only, so there is no way to replay them.
///
/// The reset reproduces the campaign's own ordering server-side, using the same
/// EF query, so the cut point is decided by the database collation that ordered
/// the original send rather than by a client's guess at it.
/// </summary>
/// <param name="DryRun">
/// DEFAULTS TO TRUE, including when omitted or explicitly null. Clearing a
/// stamp is how a supplier becomes mailable again, so it is opt-in per request.
/// </param>
/// <param name="SentAtFrom">
/// Only rows stamped at or after this instant are considered — i.e. "the run I
/// am recovering". Required: without it a careless call would un-stamp every
/// supplier ever introduced and re-mail the whole directory.
/// </param>
/// <param name="KeepFirst">
/// How many of the run's recipients actually received mail. Those keep their
/// stamp; everyone after them in send order is cleared. Read this number off
/// the sending provider, not off a guess — the response echoes the boundary
/// rows so it can be checked before committing.
/// </param>
/// <param name="SupplierIds">
/// Explicit rows to clear, for the case where delivery was not a clean prefix
/// (scattered failures rather than a cap). Overrides <paramref name="KeepFirst"/>.
/// </param>
public record SupplierIntroResetRequest(
    bool? DryRun = null,
    DateTime? SentAtFrom = null,
    int? KeepFirst = null,
    IReadOnlyList<Guid>? SupplierIds = null)
{
    /// <summary>True unless the caller explicitly asked for a live reset.</summary>
    public bool IsDryRun => DryRun ?? true;
}
