using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ruumly.Backend.Jobs;

public class StaleBookingCleanupJob(RuumlyDbContext db, ILogger<StaleBookingCleanupJob> logger)
{
    /// <summary>
    /// PlatformSettings key controlling how long a Pending (book→sign→pay) booking may sit
    /// unpaid before it auto-voids. Covers both "never paid" and "signed-but-unpaid".
    /// Adding/changing this key needs no migration (key/value row). Fallback: 24h.
    /// </summary>
    public const string ExpiryHoursSettingKey = "booking.signedUnpaidExpiryHours";
    private const int DefaultExpiryHours = 24;

    public async Task ExecuteAsync()
    {
        var expiryHours = await ResolveExpiryHoursAsync();
        var cutoff      = DateTime.UtcNow.AddHours(-expiryHours);
        // Bank-transfer bookings are reconciled manually (admin marks Paid on receipt),
        // which takes business days — give them a much longer window than the 24h
        // sign-then-pay void, so a legitimate in-flight transfer isn't auto-cancelled.
        var bankTransferDays = await ResolveBankTransferExpiryDaysAsync();
        var btCutoff         = DateTime.UtcNow.AddDays(-bankTransferDays);

        var staleBookings = await db.Bookings
            .Include(b => b.Order)
            .Include(b => b.SignedContract)
            .Where(b => b.Status == BookingStatus.Pending
                     && !db.Invoices.Any(i => i.BookingId == b.Id
                                           && i.Status == InvoiceStatus.Paid)
                     && (
                         (db.Invoices.Any(i => i.BookingId == b.Id && i.PaymentMethod == "bank_transfer") && b.CreatedAt < btCutoff)
                         || (!db.Invoices.Any(i => i.BookingId == b.Id && i.PaymentMethod == "bank_transfer") && b.CreatedAt < cutoff)
                     ))
            .ToListAsync();

        var contractsVoided = 0;

        foreach (var booking in staleBookings)
        {
            booking.Status    = BookingStatus.Cancelled;
            booking.UpdatedAt = DateTime.UtcNow;

            if (booking.Order is not null &&
                booking.Order.Status != OrderStatus.Cancelled)
            {
                booking.Order.Status    = OrderStatus.Cancelled;
                booking.Order.UpdatedAt = DateTime.UtcNow;
            }

            // Sign-then-pay safeguard: a contract that was signed (or is mid-signing) but
            // never paid binds no one — void it so no live lease lingers. No refund path:
            // the booking never reached payment, so no money is held.
            var contractVoided = false;
            if (booking.SignedContract is not null && IsActiveContractStatus(booking.SignedContract.Status))
            {
                booking.SignedContract.Status = "void";
                contractVoided = true;
                contractsVoided++;
            }

            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = contractVoided
                    ? $"Auto-cancelled: signed but unpaid within {expiryHours}h — contract voided"
                    : $"Auto-cancelled: no payment within {expiryHours}h",
                Status    = BookingStatus.Cancelled,
                CreatedAt = DateTime.UtcNow,
            });

            logger.LogInformation(
                "Auto-cancelled stale booking {Id} (window {Hours}h){ContractNote}",
                booking.Id, expiryHours,
                contractVoided ? " — signed contract voided" : "");
        }

        // Reverse any pending payout entries for the auto-cancelled orders so a
        // stale, never-paid booking never leaves the supplier owed a payout.
        var staleOrderIds = staleBookings
            .Where(b => b.Order is not null)
            .Select(b => b.Order!.Id)
            .ToList();
        if (staleOrderIds.Count > 0)
        {
            var payouts = await db.PayoutEntries
                .Where(p => staleOrderIds.Contains(p.OrderId) && p.Status == PayoutStatus.Pending)
                .ToListAsync();
            foreach (var payout in payouts)
                payout.Status = PayoutStatus.Cancelled;
        }

        if (staleBookings.Count > 0)
            await db.SaveChangesAsync();

        // Warn about paid bookings stuck in Pending (supplier slow to confirm)
        var paidButPending = await db.Bookings
            .Where(b => b.Status == BookingStatus.Pending
                     && b.CreatedAt < cutoff
                     && db.Invoices.Any(i => i.BookingId == b.Id
                                          && i.Status == InvoiceStatus.Paid))
            .ToListAsync();

        foreach (var booking in paidButPending)
        {
            logger.LogWarning(
                "Pending booking {BookingId} has a paid invoice but is not confirmed. " +
                "SupplierId: {SupplierId}. Requires manual investigation.",
                booking.Id, booking.SupplierId);
        }

        logger.LogInformation(
            "Stale booking cleanup ({Hours}h window): {Cancelled} cancelled, {ContractsVoided} contracts voided, {PaidPending} paid-but-pending",
            expiryHours, staleBookings.Count, contractsVoided, paidButPending.Count);
    }

    /// <summary>
    /// A SignedContract status that still represents a live/in-progress agreement and so
    /// should be voided when its booking auto-cancels. Terminal states (cancelled/error/void)
    /// are left untouched.
    /// </summary>
    private static bool IsActiveContractStatus(string status) =>
        status is "completed" or "pending";

    private async Task<int> ResolveExpiryHoursAsync()
    {
        var settings = await db.PlatformSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        if (settings.TryGetValue(ExpiryHoursSettingKey, out var v)
            && int.TryParse(v, out var hours)
            && hours > 0)
            return hours;

        return DefaultExpiryHours;
    }

    /// <summary>Longer auto-void window (days) for manually-reconciled bank transfers. Fallback: 7.</summary>
    public const string BankTransferExpiryDaysSettingKey = "booking.bankTransferExpiryDays";
    private const int DefaultBankTransferExpiryDays = 7;

    private async Task<int> ResolveBankTransferExpiryDaysAsync()
    {
        var value = await db.PlatformSettings
            .Where(s => s.Key == BankTransferExpiryDaysSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        return value is not null && int.TryParse(value, out var days) && days > 0
            ? days
            : DefaultBankTransferExpiryDays;
    }
}
