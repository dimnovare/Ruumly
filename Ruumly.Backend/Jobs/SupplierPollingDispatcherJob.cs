using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Jobs;

/// <summary>
/// Runs every 5 minutes via Hangfire. Dispatches a poll for each supplier that is due:
/// PollingEnabled=true AND (NextPollAt is null OR NextPollAt &lt;= now).
/// Per-supplier PollingIntervalMinutes controls the cadence after the first run.
/// </summary>
public class SupplierPollingDispatcherJob(
    RuumlyDbContext                          db,
    ISupplierPollingService                  pollingService,
    ILogger<SupplierPollingDispatcherJob>    logger)
{
    private const int MaxConcurrentPolls = 3;

    public async Task ExecuteAsync()
    {
        var now = DateTime.UtcNow;

        var dueSupplierIds = await db.Suppliers
            .Where(s => s.IsActive
                     && s.PollingEnabled
                     && (s.NextPollAt == null || s.NextPollAt <= now))
            .Select(s => s.Id)
            .ToListAsync();

        if (dueSupplierIds.Count == 0) return;

        logger.LogInformation(
            "PollingDispatcher: {Count} supplier(s) due for polling (max {Max} concurrent)",
            dueSupplierIds.Count, MaxConcurrentPolls);

        var semaphore = new SemaphoreSlim(MaxConcurrentPolls);

        var tasks = dueSupplierIds.Select(async id =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await pollingService.PollSupplierAsync(id);
                logger.LogInformation(
                    "Poll {SupplierId}: status={Status} units={Units} ms={Ms}",
                    id, result.Status, result.UnitsRefreshed, result.DurationMs);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "PollingDispatcher: unexpected error polling supplier {SupplierId} — continuing with next",
                    id);
                // Individual failures do not abort the rest of the batch
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
}
