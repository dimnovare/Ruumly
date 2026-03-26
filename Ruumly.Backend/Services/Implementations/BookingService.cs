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
    IHttpContextAccessor http,
    IDistributedCache cache,
    IBackgroundJobClient? backgroundJobs = null) : IBookingService
{
    private string Lang => http.HttpContext?.Request.GetLang() ?? "et";
    private string Msg(string key) => ErrorMessages.Get(key, Lang);

    // Extra prices matching pricing.ts EXTRAS_PRICES
    private static readonly Dictionary<string, decimal> ExtrasPrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["packing"]   = 15m,
        ["loading"]   = 20m,
        ["insurance"] = 10m,
        ["forklift"]  = 25m,
    };

    public IReadOnlyDictionary<string, decimal> GetExtrasPrices() => ExtrasPrices;

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

    public async Task<PaginatedResult<BookingDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = db.Bookings
            .Include(b => b.Listing)
            .Include(b => b.Supplier)
            .Include(b => b.Timeline)
            .Include(b => b.Order).ThenInclude(o => o!.Supplier)
            .AsQueryable();

        if (role == UserRole.Customer)
        {
            query = query.Where(b => b.UserId == userId);
        }
        else if (role == UserRole.Provider)
        {
            var user = await db.Users.FindAsync(userId);
            if (user?.SupplierId is null)
                return new PaginatedResult<BookingDto>([], 0, page, limit, false);

            query = query.Where(b => b.SupplierId == user.SupplierId);
        }
        // Admin: no filter — sees everything

        var total    = await query.CountAsync();
        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var bookingIds = bookings.Select(b => b.Id).ToList();
        var invoiceIds = await db.Invoices
            .Where(i => bookingIds.Contains(i.BookingId))
            .ToDictionaryAsync(i => i.BookingId, i => (Guid?)i.Id);

        var data = bookings.Select(b => MapToDto(b, invoiceIds.GetValueOrDefault(b.Id))).ToList();
        return new PaginatedResult<BookingDto>(data, total, page, limit, (page - 1) * limit + data.Count < total);
    }

    public async Task<BookingDto?> GetByIdAsync(Guid id, Guid userId, UserRole role)
    {
        var booking = await db.Bookings
            .Include(b => b.Listing)
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
            var user = await db.Users.FindAsync(userId);
            if (user?.SupplierId is null ||
                booking.SupplierId != user.SupplierId)
                throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));
        }

        var invoiceId = await db.Invoices
            .Where(i => i.BookingId == booking.Id)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync();

        return MapToDto(booking, invoiceId);
    }

    public async Task<BookingDto> CreateAsync(CreateBookingRequest request, Guid userId)
    {
        // 0. Require verified email before booking
        var bookingUser = await db.Users.FindAsync(userId);
        if (bookingUser?.EmailVerified != true)
            throw new ForbiddenException(Msg("EMAIL_NOT_VERIFIED"));

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // 1. Load listing
            var listing = await db.Listings
                .Include(l => l.Supplier)
                .FirstOrDefaultAsync(l => l.Id == request.ListingId && l.IsActive)
                ?? throw new NotFoundException(Msg("LISTING_NOT_FOUND"));

            // 1b. Check for overlapping confirmed/active bookings on this listing
            // Moving-type listings are one-time services with no date range — skip overlap check.
            if (listing.Type != ListingType.Moving &&
                !string.IsNullOrEmpty(request.EndDate) &&
                DateTime.TryParse(request.EndDate, out var endDateCheck) &&
                DateTime.TryParse(request.StartDate, out var startDateCheck))
            {
                var startUtc = DateTime.SpecifyKind(startDateCheck, DateTimeKind.Utc);
                var endUtc   = DateTime.SpecifyKind(endDateCheck,   DateTimeKind.Utc);

                var totalUnits  = listing.QuantityTotal ?? 1;
                var bookedCount = await db.Bookings
                    .CountAsync(b =>
                        b.ListingId == request.ListingId &&
                        (b.Status == BookingStatus.Confirmed ||
                         b.Status == BookingStatus.Active) &&
                        b.EndDate.HasValue &&
                        b.StartDate < endUtc &&
                        b.EndDate.Value > startUtc);

                if (bookedCount >= totalUnits)
                    throw new ArgumentException(Msg("NO_UNITS_AVAILABLE"));
            }

            // 2. Calculate pricing (mirrors pricing.ts formulas exactly)
            var basePrice = listing.PriceFrom;

            // Effective client discount: listing override takes precedence over supplier default
            var supplier           = listing.Supplier;
            var clientDiscountRate = listing.ClientDiscountRateOverride ?? supplier.ClientDiscountRate;
            var discountMultiplier = 1m - (clientDiscountRate / 100m);
            var discountedBase     = basePrice * discountMultiplier;
            var platformPrice      = Math.Round(discountedBase * 0.95m);

            var extrasTotal = request.Extras
                .Sum(e => ExtrasPrices.TryGetValue(e, out var p) ? p : 0m);

            // VAT calculation
            var vatRate   = listing.VatRate ?? 0m;
            var subtotal  = platformPrice + extrasTotal;
            var vatAmount = listing.PricesIncludeVat
                ? Math.Round(subtotal * vatRate / (100m + vatRate), 2)
                : Math.Round(subtotal * vatRate / 100m, 2);
            var total = listing.PricesIncludeVat ? subtotal : subtotal + vatAmount;

            if (!DateTime.TryParse(request.StartDate, out var startDate))
                throw new ArgumentException(Msg("INVALID_DATE_FORMAT"));

            DateTime? endDate = null;
            if (!string.IsNullOrWhiteSpace(request.EndDate))
            {
                if (!DateTime.TryParse(request.EndDate, out var parsedEnd))
                    throw new ArgumentException(Msg("INVALID_DATE_FORMAT"));
                endDate = DateTime.SpecifyKind(parsedEnd, DateTimeKind.Utc);
            }

            // 3. Create Booking entity
            var booking = new Booking
            {
                Id            = Guid.NewGuid(),
                UserId        = userId,
                ListingId     = listing.Id,
                SupplierId    = listing.SupplierId,
                StartDate     = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
                EndDate       = endDate,
                Duration      = request.Duration,
                Extras        = request.Extras,
                BasePrice     = basePrice,
                PlatformPrice = platformPrice,
                ExtrasTotal   = extrasTotal,
                VatAmount     = vatAmount,
                Total         = total,
                ContactName   = request.ContactName,
                ContactEmail  = request.ContactEmail,
                ContactPhone  = request.ContactPhone,
                Notes         = request.Notes,
                Status        = BookingStatus.Pending,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            };

            db.Bookings.Add(booking);
            await db.SaveChangesAsync();

            // 4. Initial timeline entry
            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = "Broneering loodud",
                Status    = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            // 5. Route and dispatch the order
            await orderRoutingService.RouteOrderAsync(booking, listing);

            await transaction.CommitAsync();

            // 5b. Enqueue confirmation email — outside transaction; Hangfire has its own store.
            backgroundJobs?.Enqueue<BackgroundOrderDispatchService>(
                x => x.SendBookingConfirmationEmailAsync(booking.Id));

            // 6. Reload with all navigations for response
            var result = await db.Bookings
                .Include(b => b.Listing)
                .Include(b => b.Supplier)
                .Include(b => b.Timeline)
                .Include(b => b.Order).ThenInclude(o => o!.Supplier)
                .FirstAsync(b => b.Id == booking.Id);

            return MapToDto(result, null);
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
            .Include(b => b.Listing)
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
            var user = await db.Users.FindAsync(userId);
            if (user?.SupplierId is null || booking.SupplierId != user.SupplierId)
                throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed)
            throw new ArgumentException("Booking is already finalised and cannot be cancelled.");

        var now = DateTime.UtcNow;

        booking.Status    = BookingStatus.Cancelled;
        booking.IsDeleted = true;
        booking.DeletedAt = now;
        booking.UpdatedAt = now;

        db.BookingTimelines.Add(new BookingTimeline
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Event     = "Broneering tühistatud",
            Status    = BookingStatus.Cancelled,
            CreatedAt = now,
        });

        // Soft-delete the linked order if present
        if (booking.Order is not null)
        {
            booking.Order.Status    = OrderStatus.Cancelled;
            booking.Order.IsDeleted = true;
            booking.Order.DeletedAt = now;
            booking.Order.UpdatedAt = now;
        }

        await db.SaveChangesAsync();

        var invoiceId = await db.Invoices
            .Where(i => i.BookingId == booking.Id)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync();

        return MapToDto(booking, invoiceId);
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static BookingDto MapToDto(Booking b, Guid? invoiceId) => new(
        Id:           b.Id,
        ListingId:    b.ListingId,
        ListingTitle: b.Listing?.Title ?? string.Empty,
        ListingType:  b.Listing?.Type.ToString().ToLower() ?? string.Empty,
        Provider:     b.Supplier?.Name ?? string.Empty,
        City:         b.Listing?.City ?? string.Empty,
        StartDate:    b.StartDate.ToString("yyyy-MM-dd"),
        EndDate:      b.EndDate?.ToString("yyyy-MM-dd"),
        Duration:     b.Duration,
        Status:       b.Status.ToString().ToLower(),
        Extras:       b.Extras,
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
        InvoiceId: invoiceId
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
}
