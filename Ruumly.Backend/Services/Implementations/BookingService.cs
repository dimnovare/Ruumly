using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Ruumly.Backend.Data;
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
    IEmailSender emailSender,
    ILogger<BookingService> logger,
    IConfiguration config,
    IHttpContextAccessor http) : IBookingService
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

    public async Task<List<BookingDto>> GetAllAsync(Guid userId, UserRole role)
    {
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
            // Provider only sees bookings for their
            // own supplier — never other suppliers.
            var user = await db.Users.FindAsync(userId);
            if (user?.SupplierId is null)
                return [];   // no supplier linked → no bookings

            query = query.Where(
                b => b.SupplierId == user.SupplierId);
        }
        // Admin: no filter — sees everything

        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        var bookingIds = bookings.Select(b => b.Id).ToList();
        var invoiceIds = await db.Invoices
            .Where(i => bookingIds.Contains(i.BookingId))
            .ToDictionaryAsync(i => i.BookingId, i => (Guid?)i.Id);

        return bookings.Select(b => MapToDto(b, invoiceIds.GetValueOrDefault(b.Id))).ToList();
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
        // 1. Load listing
        var listing = await db.Listings
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == request.ListingId && l.IsActive)
            ?? throw new NotFoundException(Msg("LISTING_NOT_FOUND"));

        // 1b. Check for overlapping confirmed/active bookings on this listing
        if (!string.IsNullOrEmpty(request.EndDate) &&
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
        var basePrice     = listing.PriceFrom;

        // Effective client discount: listing override takes precedence over supplier default
        var supplier              = listing.Supplier;
        var clientDiscountRate    = listing.ClientDiscountRateOverride ?? supplier.ClientDiscountRate;
        var discountMultiplier    = 1m - (clientDiscountRate / 100m);
        var discountedBase        = basePrice * discountMultiplier;
        var platformPrice         = Math.Round(discountedBase * 0.95m);

        var extrasTotal   = request.Extras
            .Sum(e => ExtrasPrices.TryGetValue(e, out var p) ? p : 0m);

        // VAT calculation
        var vatRate    = listing.VatRate ?? 0m;
        var subtotal   = platformPrice + extrasTotal;
        var vatAmount  = listing.PricesIncludeVat
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

        // 5b. Send booking confirmation email to customer
        if (!string.IsNullOrWhiteSpace(booking.ContactEmail))
        {
            try
            {
                var bookingUser = await db.Users.FindAsync(userId);
                var lang        = bookingUser?.Language ?? "et";
                var t           = EmailTranslations.For(lang);

                var accountUrl  = $"{config["AppUrl"]}/account?tab=bookings";

                var textBody =
                    $"{t.BookingConfirmGreeting} {booking.ContactName},\n\n" +
                    $"{t.BookingConfirmBody}\n\n" +
                    $"{t.BookingConfirmService}: {listing.Title}\n" +
                    $"{t.BookingConfirmStartDate}: {booking.StartDate:dd.MM.yyyy}\n" +
                    $"{t.BookingConfirmPeriod}: {booking.Duration}\n" +
                    $"{t.BookingConfirmTotal}: €{booking.Total:F2}" +
                    (booking.VatAmount > 0
                        ? $" ({t.BookingConfirmVat} €{booking.VatAmount:F2})"
                        : "") +
                    $"\n\n{t.BookingConfirmNext}\n{accountUrl}\n\n" +
                    $"Ruumly\ninfo@ruumly.eu";

                var confirmSubject =
                    $"{t.BookingConfirmSubject} #{booking.Id.ToString()[..8].ToUpper()}";

                await emailSender.SendAsync(booking.ContactEmail, confirmSubject, textBody);
            }
            catch (Exception ex)
            {
                // Don't fail the booking if email fails
                logger.LogWarning(ex,
                    "Failed to send booking confirmation email to {Email}",
                    booking.ContactEmail);
            }
        }

        // 6. Reload with all navigations for response
        var result = await db.Bookings
            .Include(b => b.Listing)
            .Include(b => b.Supplier)
            .Include(b => b.Timeline)
            .Include(b => b.Order).ThenInclude(o => o!.Supplier)
            .FirstAsync(b => b.Id == booking.Id);

        return MapToDto(result, null);
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
