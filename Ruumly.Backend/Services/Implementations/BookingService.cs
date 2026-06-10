using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class BookingService(
    RuumlyDbContext db,
    IOrderRoutingService orderRoutingService,
    IPricingConfigService pricingConfigService,
    IInvoiceService invoiceService,
    IHttpContextAccessor http,
    IDistributedCache cache,
    ILogger<BookingService> logger,
    IBackgroundJobClient? backgroundJobs = null) : IBookingService
{
    private string Lang => http.HttpContext?.Request.GetLang() ?? "et";
    private string Msg(string key) => ErrorMessages.Get(key, Lang);

    public async Task<BookingStatsDto> GetStatsAsync()
    {
        const string cacheKey = "platform:booking-stats";
        var cached = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<BookingStatsDto>(cached)!;

        var totalBookings = await db.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled && !b.IsDeleted)
            .CountAsync();

        var avgRating = await db.Reviews.AnyAsync()
            ? Math.Round((decimal)await db.Reviews.AverageAsync(r => (double)r.Rating), 1)
            : 0m;

        var result = new BookingStatsDto(totalBookings, avgRating);

        await cache.SetStringAsync(cacheKey,
            JsonSerializer.Serialize(result),
            new DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });

        return result;
    }

    public async Task<PaginatedResult<BookingDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50, Guid? supplierId = null, CancellationToken ct = default)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);

        var query = db.Bookings
            .Include(b => b.Listing).ThenInclude(l => l!.Location)
            .Include(b => b.Supplier)
            .Include(b => b.Timeline)
            .Include(b => b.Order).ThenInclude(o => o!.Supplier)
            .AsSplitQuery()
            .AsQueryable();

        if (role == UserRole.Customer)
        {
            query = query.Where(b => b.UserId == userId);
        }
        else if (role == UserRole.Provider)
        {
            var supplierIdForUser = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync(ct);

            if (supplierIdForUser is null)
                return new PaginatedResult<BookingDto>([], 0, page, limit, false);

            query = query.Where(b => b.SupplierId == supplierIdForUser.Value);
        }
        else if (role == UserRole.Admin && supplierId.HasValue)
        {
            query = query.Where(b => b.SupplierId == supplierId.Value);
        }
        // Admin without supplierId → no filter, sees everything (existing behavior)

        var total    = await query.CountAsync(ct);
        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        var bookingIds = bookings.Select(b => b.Id).ToList();
        var invoiceIds = await db.Invoices
            .Where(i => bookingIds.Contains(i.BookingId))
            .ToDictionaryAsync(i => i.BookingId, i => (Guid?)i.Id, ct);
        var reviewedBookingIds = (await db.Reviews
            .Where(r => bookingIds.Contains(r.BookingId))
            .Select(r => r.BookingId)
            .ToListAsync(ct))
            .ToHashSet();

        var data = bookings.Select(b => MapToDto(
            b,
            invoiceIds.GetValueOrDefault(b.Id),
            reviewedBookingIds.Contains(b.Id)
        )).ToList();
        return new PaginatedResult<BookingDto>(data, total, page, limit, (page - 1) * limit + data.Count < total);
    }

    public async Task<BookingDto?> GetByIdAsync(Guid id, Guid userId, UserRole role)
    {
        var booking = await db.Bookings
            .Include(b => b.Listing).ThenInclude(l => l!.Location)
            .Include(b => b.Supplier)
            .Include(b => b.Timeline)
            .Include(b => b.Order).ThenInclude(o => o!.Supplier)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null)
            return null;

        if (role == UserRole.Customer && booking.UserId != userId)
            throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));  // don't reveal existence

        if (role == UserRole.Provider)
        {
            var supplierIdForUser = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync();

            if (supplierIdForUser is null || booking.SupplierId != supplierIdForUser.Value)
                throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));
        }

        var invoiceId = await db.Invoices
            .Where(i => i.BookingId == booking.Id)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync();

        var hasReview = await db.Reviews.AnyAsync(r => r.BookingId == booking.Id);

        return MapToDto(booking, invoiceId, hasReview);
    }

    public async Task<BookingDto> CreateAsync(CreateBookingRequest request, Guid userId)
    {
        var bookingUser = await db.Users.FindAsync(userId);
        var tl = EmailTranslations.For(bookingUser?.Language);

        // Idempotency check — return existing booking if this key was already used
        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            var existing = await db.Bookings
                .Include(b => b.Listing).ThenInclude(l => l!.Location)
                .Include(b => b.Supplier)
                .Include(b => b.Timeline)
                .Include(b => b.Order).ThenInclude(o => o!.Supplier)
                .FirstOrDefaultAsync(b => b.IdempotencyKey == request.IdempotencyKey);
            if (existing is not null)
            {
                var existingInvoiceId = await db.Invoices
                    .Where(i => i.BookingId == existing.Id)
                    .Select(i => (Guid?)i.Id)
                    .FirstOrDefaultAsync();
                return MapToDto(existing, existingInvoiceId);
            }
        }

        // Reject bookings with start date in the past (defense in depth — the
        // FluentValidation rule on the request runs first; this guards against
        // direct service calls and request shapes that bypass model validation).
        // Allow today (UTC-tolerant: 1-day buffer for timezone drift).
        if (DateTime.TryParseExact(
                request.StartDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var requestedStart))
        {
            if (requestedStart.Date < DateTime.UtcNow.Date.AddDays(-1))
                throw new ArgumentException(Msg("BOOKING_START_PAST"));

            if (DateTime.TryParseExact(
                    request.EndDate ?? "", "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var requestedEnd) && requestedEnd <= requestedStart)
            {
                throw new ArgumentException(Msg("BOOKING_END_BEFORE_START"));
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // 1. Load listing
            var listing = await db.Listings
                .Include(l => l.Supplier)
                .Include(l => l.Location)
                .FirstOrDefaultAsync(l => l.Id == request.ListingId && l.IsActive)
                ?? throw new NotFoundException(Msg("LISTING_NOT_FOUND"));

            // 1a. BlockedDates check — reject if any day in the range is blocked for this location
            if (listing.LocationId.HasValue &&
                DateOnly.TryParseExact(request.StartDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var bookingStartDate))
            {
                var bookingEndDate = (!string.IsNullOrEmpty(request.EndDate) &&
                    DateOnly.TryParseExact(request.EndDate, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var parsedEnd))
                    ? parsedEnd
                    : bookingStartDate;

                var hasBlockedDate = await db.BlockedDates.AnyAsync(b =>
                    b.LocationId == listing.LocationId.Value &&
                    b.Date >= bookingStartDate &&
                    b.Date <= bookingEndDate);

                if (hasBlockedDate)
                    throw new ConflictException(Msg("DATES_BLOCKED"));
            }

            // 1b. Check for overlapping confirmed/active bookings on this listing
            // Moving-type listings are one-time services with no date range — skip overlap check.
            if (listing.Type != ListingType.Moving &&
                !string.IsNullOrEmpty(request.EndDate) &&
                DateTime.TryParseExact(
                    request.EndDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var endDateCheck) &&
                DateTime.TryParseExact(
                    request.StartDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var startDateCheck))
            {
                var startUtc = startDateCheck;
                var endUtc   = endDateCheck;

                // Advisory lock prevents concurrent overlap-check races on PostgreSQL.
                // Skipped on InMemory (tests) where row-level contention cannot occur.
                if (db.Database.IsRelational())
                {
                    var listingIdLong = (long)(listing.Id.GetHashCode() & 0x7FFFFFFF);
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0})", listingIdLong);
                }

                var totalUnits  = listing.QuantityTotal ?? 1;
                var bookedCount = await db.Bookings
                    .CountAsync(b =>
                        b.ListingId == request.ListingId &&
                        (b.Status == BookingStatus.Pending   ||   // guard: back+resubmit without idempotency key
                         b.Status == BookingStatus.Confirmed ||
                         b.Status == BookingStatus.Active    ||
                         b.Status == BookingStatus.Reserved) &&
                        b.EndDate.HasValue &&
                        b.StartDate < endUtc &&
                        b.EndDate.Value > startUtc);

                if (bookedCount >= totalUnits)
                    throw new ArgumentException(Msg("NO_UNITS_AVAILABLE"));
            }

            // 2. Load pricing config
            var pricingConfig = await pricingConfigService.GetAsync();
            var supplier      = listing.Supplier;
            // Calculate base price scaled by booking duration and listing's price unit
            decimal basePrice;
            if (DateTime.TryParse(request.StartDate, out var durationStart) &&
                DateTime.TryParse(request.EndDate, out var durationEnd) &&
                durationEnd > durationStart)
            {
                basePrice = CalculateDurationPrice(listing.PriceFrom, listing.PriceUnit, durationStart, durationEnd);
            }
            else
            {
                basePrice = listing.PriceFrom;
            }

            // 3. Calculate base pricing via shared PricingHelpers — single source of
            // truth so frontend display and booking math always agree.
            // Honors per-listing partner + customer overrides; falls back to supplier
            // rate, then platform default; customer rate guarantees ruumlyMinMargin.
            var (partnerDiscountRate, customerDiscountRate) = PricingHelpers.ComputeEffectiveDiscounts(
                listingPartnerOverride:  listing.PartnerDiscountRateOverride,
                listingCustomerOverride: listing.ClientDiscountRateOverride,
                supplierPartnerRate:     supplier.PartnerDiscountRate,
                defaultPartnerDiscount:  pricingConfig.DefaultPartnerDiscountRate,
                ruumlyMinMargin:         pricingConfig.RuumlyMinMarginRate);

            var ruumlyMinMargin = pricingConfig.RuumlyMinMarginRate;

            // Log when supplier margin falls below platform minimum
            if (partnerDiscountRate < ruumlyMinMargin)
            {
                logger.LogWarning(
                    "Booking {ListingId}: supplier margin {PartnerRate}% below platform minimum {MinRate}%. Margin: {Margin}%",
                    request.ListingId, partnerDiscountRate, ruumlyMinMargin, partnerDiscountRate);
            }

            // What the customer pays
            var platformPrice = Math.Round(basePrice * (1m - customerDiscountRate / 100m), 2);

            // Safety check: ensure margin is never negative
            var supplierPrice = Math.Round(basePrice * (1m - partnerDiscountRate / 100m), 2);
            if (platformPrice < supplierPrice)
            {
                // If somehow the math fails, charge full price
                platformPrice        = basePrice;
                customerDiscountRate = 0;
            }

            // 4. Calculate extras from listing's own extras (not hardcoded)
            var listingExtras = await db.ListingExtras
                .Where(e => e.ListingId == listing.Id && e.IsActive)
                .ToDictionaryAsync(e => e.Key);

            var extrasSnapshots    = new List<BookingExtraSnapshot>();
            decimal extrasCustomerTotal = 0m;

            foreach (var extraKey in request.Extras)
            {
                if (listingExtras.TryGetValue(extraKey, out var extra))
                {
                    extrasSnapshots.Add(new BookingExtraSnapshot(
                        extra.Key, extra.Label,
                        extra.PublicPrice, extra.SupplierPrice, extra.CustomerPrice));
                    extrasCustomerTotal += extra.CustomerPrice;
                }
            }

            // 5. VAT calculation
            var country = listing.Location?.Country ?? supplier.Country ?? "EE";
            var vatRate = listing.VatRate ?? PricingConfigService.GetDefaultVatRate(country);
            var subtotal  = platformPrice + extrasCustomerTotal;
            var vatAmount = listing.PricesIncludeVat
                ? Math.Round(subtotal * vatRate / (100m + vatRate), 2)
                : Math.Round(subtotal * vatRate / 100m, 2);
            var total = listing.PricesIncludeVat ? subtotal : subtotal + vatAmount;

            if (!DateTime.TryParseExact(
                    request.StartDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var startDate))
                throw new ArgumentException(Msg("INVALID_DATE_FORMAT"));

            DateTime? endDate = null;
            if (!string.IsNullOrWhiteSpace(request.EndDate))
            {
                if (!DateTime.TryParseExact(
                        request.EndDate, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal |
                        System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var parsedEnd))
                    throw new ArgumentException(Msg("INVALID_DATE_FORMAT"));

                if (parsedEnd <= startDate)
                    throw new ArgumentException(Msg("BOOKING_END_BEFORE_START"));

                endDate = parsedEnd;
            }

            // 6. Create Booking entity
            var isReservation = string.Equals(request.PaymentMethod, "reserve", StringComparison.OrdinalIgnoreCase);
            var booking = new Booking
            {
                Id             = Guid.NewGuid(),
                UserId         = userId,
                ListingId      = listing.Id,
                SupplierId     = listing.SupplierId,
                StartDate      = startDate,
                EndDate        = endDate,
                Duration       = request.Duration,
                ExtrasSnapshot = extrasSnapshots,
                BasePrice      = basePrice,
                PlatformPrice  = platformPrice,
                ExtrasTotal    = extrasCustomerTotal,
                VatAmount      = vatAmount,
                Total          = total,
                ContactName    = request.ContactName,
                ContactEmail   = request.ContactEmail,
                ContactPhone   = request.ContactPhone,
                Notes          = request.Notes,
                IdempotencyKey = request.IdempotencyKey,
                Status         = isReservation ? BookingStatus.Reserved : BookingStatus.Pending,
                ReservedUntil  = isReservation ? DateTime.UtcNow.AddHours(24) : null,
                CreatedAt      = DateTime.UtcNow,
                UpdatedAt      = DateTime.UtcNow,
            };

            db.Bookings.Add(booking);

            // 7. Initial timeline entry
            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = tl.TimelineBookingCreated,
                Status    = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            // 8. Route and dispatch the order
            await orderRoutingService.RouteOrderAsync(booking, listing);

            // 9. Auto-generate invoice for payment (marketplace model only)
            // Rebate model: customer pays provider directly — no Ruumly invoice needed
            if (supplier.BillingModel == BillingModel.Marketplace)
            {
                await invoiceService.GenerateAsync(booking.Id);
            }

            await transaction.CommitAsync();

            // 8b. Enqueue confirmation email — outside transaction; Hangfire has its own store.
            backgroundJobs?.Enqueue<BackgroundOrderDispatchService>(
                x => x.SendBookingConfirmationEmailAsync(booking.Id));

            // 9. Reload with all navigations for response
            var result = await db.Bookings
                .Include(b => b.Listing).ThenInclude(l => l!.Location)
                .Include(b => b.Supplier)
                .Include(b => b.Timeline)
                .Include(b => b.Order).ThenInclude(o => o!.Supplier)
                .FirstAsync(b => b.Id == booking.Id);

            var generatedInvoiceId = await db.Invoices
                .Where(i => i.BookingId == booking.Id)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync();

            return MapToDto(result, generatedInvoiceId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<BookingDto> CancelAsync(Guid id, Guid userId, UserRole role)
    {
        var booking = await db.Bookings
            .Include(b => b.Listing).ThenInclude(l => l!.Location)
            .Include(b => b.Supplier)
            .Include(b => b.Timeline)
            .Include(b => b.Order).ThenInclude(o => o!.Supplier)
            .FirstOrDefaultAsync(b => b.Id == id)
            ?? throw new NotFoundException(Msg("BOOKING_NOT_FOUND"));

        // Authorisation
        if (role == UserRole.Customer && booking.UserId != userId)
            throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));

        if (role == UserRole.Provider)
        {
            var supplierIdForUser = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync();

            if (supplierIdForUser is null || booking.SupplierId != supplierIdForUser.Value)
                throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed)
            throw new ArgumentException(Msg("BOOKING_ALREADY_FINALISED"));

        var tl  = EmailTranslations.For((await db.Users.FindAsync(booking.UserId))?.Language);
        var now = DateTime.UtcNow;

        booking.Status    = BookingStatus.Cancelled;
        booking.UpdatedAt = now;
        // Note: do NOT set IsDeleted here. Cancelled bookings remain visible to
        // the customer in their booking history (filterable by status).
        // IsDeleted is reserved for admin/GDPR hard-delete operations.

        db.BookingTimelines.Add(new BookingTimeline
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Event     = tl.TimelineBookingCancelled,
            Status    = BookingStatus.Cancelled,
            CreatedAt = now,
        });

        if (booking.Order is not null)
        {
            booking.Order.Status    = OrderStatus.Cancelled;
            booking.Order.UpdatedAt = now;
            // Order also stays visible — only set IsDeleted for hard-delete.

            // Cancel the pending payout entry so the supplier is not paid for a cancelled order.
            var payout = await db.PayoutEntries
                .FirstOrDefaultAsync(p => p.OrderId == booking.Order.Id
                                       && p.Status == PayoutStatus.Pending);
            if (payout is not null)
                payout.Status = PayoutStatus.Cancelled;
        }

        // Cancel the invoice if it hasn't been fully refunded yet.
        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.BookingId == booking.Id);
        if (invoice is not null
            && invoice.Status != InvoiceStatus.Refunded
            && invoice.Status != InvoiceStatus.PendingRefund)
        {
            invoice.Status = invoice.Status == InvoiceStatus.Paid
                ? InvoiceStatus.PendingRefund   // paid → needs manual refund
                : InvoiceStatus.Refunded;        // pending/awaiting → void immediately
        }

        await db.SaveChangesAsync();

        var invoiceId = await db.Invoices
            .Where(i => i.BookingId == booking.Id)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync();

        var hasReview = await db.Reviews.AnyAsync(r => r.BookingId == booking.Id);
        return MapToDto(booking, invoiceId, hasReview);
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static BookingDto MapToDto(Booking b, Guid? invoiceId, bool hasReview = false) => new(
        Id:           b.Id,
        ListingId:    b.ListingId,
        ListingTitle: b.Listing?.Title ?? string.Empty,
        ListingType:  b.Listing?.Type.ToString().ToLower() ?? string.Empty,
        Provider:     b.Supplier?.Name ?? string.Empty,
        City:         b.Listing?.Location?.City ?? b.Listing?.City ?? string.Empty,
        LocationId:   b.Listing?.LocationId?.ToString(),
        StartDate:    b.StartDate.ToString("yyyy-MM-dd"),
        EndDate:      b.EndDate?.ToString("yyyy-MM-dd"),
        Duration:     b.Duration,
        Status:       b.Status.ToString().ToLower(),
        Extras:       b.ExtrasKeys,
        BasePrice:    b.BasePrice,
        PlatformPrice: b.PlatformPrice,
        ExtrasTotal:  b.ExtrasTotal,
        VatAmount:    b.VatAmount,
        Total:        b.Total,
        CreatedAt:    b.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
        Timeline:     b.Timeline
            .OrderBy(t => t.CreatedAt)
            .Select(t => new BookingTimelineDto(
                Date:   t.CreatedAt.ToString("yyyy-MM-dd"),
                Event:  t.Event,
                Status: t.Status.ToString().ToLower()
            )).ToList(),
        Order:     b.Order is null ? null : MapOrderToDto(b.Order),
        InvoiceId: invoiceId,
        ReservedUntil: b.ReservedUntil?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        IsReservation: b.Status == BookingStatus.Reserved,
        HasReview:     hasReview,
        ContactName:   b.ContactName,
        ContactEmail:  b.ContactEmail,
        ContactPhone:  b.ContactPhone,
        Notes:         b.Notes
    );

    private static OrderSummaryDto MapOrderToDto(Order o) => new(
        Id:              o.Id,
        Status:          o.Status.ToString().ToLower(),
        IntegrationType: o.IntegrationType.ToString().ToLower(),
        SupplierName:    o.Supplier?.Name ?? string.Empty,
        SupplierPrice:   o.SupplierPrice,
        ExtrasTotal:     o.ExtrasTotal,
        Total:           o.Total,
        Margin:          o.Margin,
        CreatedAt:       o.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
        SentAt:          o.SentAt?.ToString("yyyy-MM-dd HH:mm")
    );

    private static decimal CalculateDurationPrice(decimal priceFrom, string priceUnit, DateTime startDate, DateTime endDate)
    {
        var days = Math.Max(1, (endDate - startDate).Days);
        var unit = (priceUnit ?? "").ToLower().Replace("€", "").Trim().TrimStart('/');

        // Days-per-unit lookup: how many days does one unit price cover?
        // Used to pro-rate sub-unit bookings (e.g. 5 days on a /month listing
        // = 5/30.44 of a month price, NOT 1 full month).
        decimal daysPerUnit = unit switch
        {
            "day"   or "päev"  or "diena"  or "день"               => 1m,
            "week"  or "nädal" or "savaitė" or "nedēļa" or "неделя" => 7m,
            "month" or "kuu"   or "mėnuo"  or "mēnesis" or "месяц"  => 30.44m,
            "hour"  or "tund"  or "valanda" or "stunda" or "час"    => 1m / 24m,
            "time"  or "kord"  or "kartas" or "reize" or "раз"      => 1m,
            _                                                       => 30.44m, // default: assume monthly
        };

        // For one-time price units (hour/time), price is flat regardless of duration.
        if (unit is "hour" or "tund" or "valanda" or "stunda" or "час"
                or "time" or "kord" or "kartas" or "reize" or "раз")
        {
            return priceFrom;
        }

        // Pro-rate: total = (priceFrom / daysPerUnit) * days
        // Example: €49/month listing, 1-day booking
        //   = (49 / 30.44) * 1 = €1.61
        // Example: €49/month listing, 60-day booking
        //   = (49 / 30.44) * 60 = €96.59
        var total = priceFrom * days / daysPerUnit;

        return Math.Round(total, 2);
    }
}
