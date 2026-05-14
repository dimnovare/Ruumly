namespace Ruumly.Backend.Services.Interfaces;

public record PollingResult(
    bool    Success,
    string  Status,
    string? ErrorMessage,
    int     UnitsRefreshed,
    int     DurationMs
);

public interface ISupplierPollingService
{
    /// <summary>
    /// Polls the API endpoint for a single supplier, writes a PollingLog entry,
    /// and updates the supplier's LastPolledAt / LastPollStatus / NextPollAt fields.
    /// Returns a skipped result when the supplier is inactive or not API-typed.
    /// Never throws — all exceptions are caught and returned as error results.
    /// </summary>
    Task<PollingResult> PollSupplierAsync(Guid supplierId, CancellationToken ct = default);
}
