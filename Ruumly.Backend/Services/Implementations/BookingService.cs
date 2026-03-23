using Microsoft.EntityFrameworkCore;
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
    IOrderRoutingService orderRoutingService) : IBookingService
{
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
            query = query.Where(b => b.UserId == userId);
        // Admin and Provider see all bookings

        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return bookings.Select(MapToDto).ToList();
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
            throw new ForbiddenException("You do not have access to this booking.");

        return MapToDto(booking);
    }

    public async Task<BookingDto> CreateAsync(CreateBookingRequest request, Guid userId)
    {
        // 1. Load listing
        var listing = await db.Listings
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == request.ListingId && l.IsActive)
            ?? throw new NotFoundException($"Listing {request.ListingId} not found or inactive.");

        // 2. Calculate pricing (mirrors pricing.ts formulas exactly)
        var basePrice     = listing.PriceFrom;
        var platformPrice = Math.Round(basePrice * 0.95m);
        var extrasTotal   = request.Extras
            .Sum(e => ExtrasPrices.TryGetValue(e, out var p) ? p : 0m);
        var total         = platformPrice + extrasTotal;

        if (!DateTime.TryParse(request.StartDate, out var startDate))
            throw new ArgumentException("Invalid startDate format. Use yyyy-MM-dd.");

        DateTime? endDate = null;
        if (!string.IsNullOrWhiteSpace(request.EndDate))
        {
            if (!DateTime.TryParse(request.EndDate, out var parsedEnd))
                throw new ArgumentException("Invalid endDate format. Use yyyy-MM-dd.");
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

        // 6. Reload with all navigations for response
        var result = await db.Bookings
            .Include(b => b.Listing)
            .Include(b => b.Supplier)
            .Include(b => b.Timeline)
            .Include(b => b.Order).ThenInclude(o => o!.Supplier)
            .FirstAsync(b => b.Id == booking.Id);

        return MapToDto(result);
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static BookingDto MapToDto(Booking b) => new(
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
        Total:        b.Total,
        CreatedAt:    b.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
        Timeline:     b.Timeline
            .OrderBy(t => t.CreatedAt)
            .Select(t => new BookingTimelineDto(
                Date:   t.CreatedAt.ToString("yyyy-MM-dd"),
                Event:  t.Event,
                Status: t.Status.ToString().ToLower()
            )).ToList(),
        Order: b.Order is null ? null : MapOrderToDto(b.Order)
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
