using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
public class AdminSettingsController(RuumlyDbContext db) : AdminBaseController(db)
{
    // ══════════════════════════════════════════════════════════════════════════
    // STATS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalListings      = await Db.Listings.CountAsync(l => l.IsActive);
        var totalOrders        = await Db.Orders.CountAsync();
        var totalUsers         = await Db.Users.CountAsync(u => u.Role != UserRole.Admin);
        var totalRevenue       = await Db.Invoices
                                    .Where(i => i.Status == InvoiceStatus.Paid)
                                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var ordersThisMonth    = await Db.Orders
                                    .CountAsync(o => o.CreatedAt >= monthStart);
        var revenueThisMonth   = await Db.Invoices
                                    .Where(i => i.Status == InvoiceStatus.Paid && i.CreatedAt >= monthStart)
                                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var pendingOrders      = await Db.Orders
                                    .CountAsync(o => o.Status == OrderStatus.Created
                                                  || o.Status == OrderStatus.Sending);

        var recentInquiries = await Db.Bookings
            .Include(b => b.Listing)
            .Include(b => b.User)
            .Where(b => b.Status == BookingStatus.Pending)
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .Select(b => new RecentInquiryDto(
                b.Id,
                b.User.Name,
                b.User.Email,
                b.Listing.Title,
                b.Listing.Type.ToString().ToLower(),
                b.CreatedAt.ToString("yyyy-MM-dd"),
                "new",
                b.Notes ?? ""
            ))
            .ToListAsync();

        return Ok(new AdminStatsDto(
            TotalListings:    totalListings,
            TotalOrders:      totalOrders,
            TotalUsers:       totalUsers,
            TotalRevenue:     totalRevenue,
            OrdersThisMonth:  ordersThisMonth,
            RevenueThisMonth: revenueThisMonth,
            PendingOrders:    pendingOrders,
            RecentInquiries:  recentInquiries
        ));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // INQUIRIES
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("inquiries")]
    public async Task<IActionResult> GetInquiries()
    {
        var pendingBookings = await Db.Bookings
            .Include(b => b.Listing)
            .Include(b => b.User)
            .Where(b => b.Status == BookingStatus.Pending)
            .OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .Select(b => new
            {
                id       = b.Id,
                customer = b.User.Name,
                email    = b.User.Email,
                listing  = b.Listing.Title,
                type     = b.Listing.Type.ToString().ToLower(),
                date     = b.CreatedAt.ToString("yyyy-MM-dd"),
                status   = "new",
                notes    = b.Notes ?? "",
            })
            .ToListAsync();

        return Ok(pendingBookings);
    }

    [HttpPatch("inquiries/{id:guid}")]
    public async Task<IActionResult> UpdateInquiry(Guid id, [FromBody] UpdateInquiryRequest body)
    {
        var booking = await Db.Bookings.FindAsync(id);
        if (booking is null) return NotFound(Error("Inquiry not found"));

        if (!string.IsNullOrWhiteSpace(body.Notes))
            booking.Notes = body.Notes;

        await Audit("inquiry.updated", User.GetUserEmail(),
            id.ToString(), body.Status);
        await Db.SaveChangesAsync();

        return Ok(new { id, status = body.Status });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PLATFORM SETTINGS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var rows = await Db.PlatformSettings
            .OrderBy(s => s.Key)
            .ToListAsync();

        var dict = rows.ToDictionary(
            s => s.Key,
            s => (object)new
            {
                value     = s.Value,
                note      = s.Note,
                updatedAt = s.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                updatedBy = s.UpdatedBy,
            });
        return Ok(dict);
    }

    [HttpPatch("settings")]
    public async Task<IActionResult> PatchSettings([FromBody] Dictionary<string, string> updates)
    {
        if (updates is null || updates.Count == 0)
            return BadRequest(Error("No settings provided"));

        var actor = User.GetUserEmail();
        foreach (var kv in updates)
        {
            var existing = await Db.PlatformSettings.FindAsync(kv.Key);
            if (existing is not null)
            {
                existing.Value     = kv.Value;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = actor;
            }
            else
            {
                Db.PlatformSettings.Add(new PlatformSetting
                {
                    Key       = kv.Key,
                    Value     = kv.Value,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = actor,
                });
            }
        }

        await Audit("settings.updated", actor,
            string.Join(", ", updates.Keys), null);
        await Db.SaveChangesAsync();

        return Ok(new { updated = updates.Count });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AUDIT LOG
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("audit-log")]
    public async Task<IActionResult> GetAuditLog([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);

        var total = await Db.AuditLogs.CountAsync();
        var items = await Db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        return Ok(new
        {
            data  = items.Select(AdminMappers.MapAuditLog),
            total,
            page,
            limit,
            hasMore = (page - 1) * limit + items.Count < total,
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PROVIDER BANK DETAILS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("my-bank-details")]
    [Authorize(Roles = "Provider,Admin")]
    public async Task<IActionResult> GetBankDetails()
    {
        var userId   = User.GetUserId();
        var user     = await Db.Users
            .Include(u => u.Supplier)
            .FirstOrDefaultAsync(u => u.Id == userId);
        var supplier = user?.Supplier;
        if (supplier is null)
            return BadRequest(Error("No supplier linked to this account."));

        return Ok(new
        {
            iban            = supplier.Iban,
            bankAccountName = supplier.BankAccountName,
            bankName        = supplier.BankName,
        });
    }

    [HttpPatch("my-bank-details")]
    [Authorize(Roles = "Provider")]
    public async Task<IActionResult> UpdateBankDetails([FromBody] UpdateBankDetailsRequest body)
    {
        var userId = User.GetUserId();
        var user   = await Db.Users
            .Include(u => u.Supplier)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.SupplierId is null)
            return BadRequest(Error("Teie kontoga ei ole seotud ühtegi partnerit."));

        var supplier = await Db.Suppliers.FindAsync(user.SupplierId.Value);
        if (supplier is null) return NotFound(Error("Partner not found"));

        if (!string.IsNullOrWhiteSpace(body.Iban))
        {
            var cleanIban = body.Iban.Replace(" ", "").ToUpper();
            if (cleanIban.Length < 15 || cleanIban.Length > 34)
                return BadRequest(Error("IBAN formaat on vale. Näide: EE382200221011xxxx"));
            supplier.Iban = cleanIban;
        }

        if (body.BankAccountName is not null)
            supplier.BankAccountName = body.BankAccountName.Trim();

        if (body.BankName is not null)
            supplier.BankName = body.BankName.Trim();

        supplier.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new
        {
            iban            = supplier.Iban,
            bankAccountName = supplier.BankAccountName,
            bankName        = supplier.BankName,
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LISTINGS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("listings")]
    public async Task<IActionResult> GetListings()
    {
        var listings = await Db.Listings
            .Include(l => l.Supplier)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        return Ok(listings.Select(AdminMappers.MapListing));
    }

    [HttpGet("listings/{id:guid}")]
    public async Task<IActionResult> GetListing(Guid id)
    {
        var listing = await Db.Listings
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (listing is null) return NotFound(Error("Listing not found"));
        return Ok(AdminMappers.MapListing(listing));
    }

    [HttpPost("listings")]
    public async Task<IActionResult> CreateListing([FromBody] CreateAdminListingRequest body)
    {
        if (!Enum.TryParse<ListingType>(body.Type, ignoreCase: true, out var type))
            return BadRequest(Error($"Invalid listing type '{body.Type}'."));

        ListingBadge? badge = null;
        if (!string.IsNullOrWhiteSpace(body.Badge))
        {
            var normalized = body.Badge.Replace("-", "");
            if (Enum.TryParse<ListingBadge>(normalized, ignoreCase: true, out var b)) badge = b;
        }

        var supplier = await Db.Suppliers.FindAsync(body.SupplierId);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        var listing = new Listing
        {
            Id                          = Guid.NewGuid(),
            Type                        = type,
            Title                       = body.Title,
            SupplierId                  = body.SupplierId,
            Address                     = body.Address,
            City                        = body.City,
            Lat                         = body.Lat,
            Lng                         = body.Lng,
            PriceFrom                   = body.PriceFrom,
            PriceUnit                   = body.PriceUnit,
            AvailableNow                = body.AvailableNow,
            IsActive                    = true,
            Badge                       = badge,
            Description                 = body.Description,
            ImagesJson                  = System.Text.Json.JsonSerializer.Serialize(body.Images ?? []),
            FeaturesJson                = System.Text.Json.JsonSerializer.Serialize(body.Features ?? new Dictionary<string, object>()),
            PartnerDiscountRateOverride = body.PartnerDiscountRateOverride,
            ClientDiscountRateOverride  = body.ClientDiscountRateOverride,
            VatRate                     = body.VatRate,
            PricesIncludeVat            = body.PricesIncludeVat,
            LocationId                  = body.LocationId,
            QuantityTotal               = body.QuantityTotal,
            SizeM2                      = body.SizeM2,
            CreatedAt                   = DateTime.UtcNow,
            UpdatedAt                   = DateTime.UtcNow,
        };

        Db.Listings.Add(listing);
        await Audit("listing.created", User.GetUserEmail(), listing.Title, null);
        await Db.SaveChangesAsync();

        listing.Supplier = supplier;
        return CreatedAtAction(nameof(GetListing), new { id = listing.Id }, AdminMappers.MapListing(listing));
    }

    [HttpPatch("listings/{id:guid}")]
    public async Task<IActionResult> PatchListing(Guid id, [FromBody] PatchAdminListingRequest body)
    {
        var listing = await Db.Listings
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (listing is null) return NotFound(Error("Listing not found"));

        if (body.Type is not null && Enum.TryParse<ListingType>(body.Type, ignoreCase: true, out var t))
            listing.Type = t;
        if (body.Title is not null)         listing.Title       = body.Title;
        if (body.Address is not null)       listing.Address     = body.Address;
        if (body.City is not null)          listing.City        = body.City;
        if (body.Lat.HasValue)              listing.Lat         = body.Lat.Value;
        if (body.Lng.HasValue)              listing.Lng         = body.Lng.Value;
        if (body.PriceFrom.HasValue)        listing.PriceFrom   = body.PriceFrom.Value;
        if (body.PriceUnit is not null)     listing.PriceUnit   = body.PriceUnit;
        if (body.AvailableNow.HasValue)     listing.AvailableNow = body.AvailableNow.Value;
        if (body.IsActive.HasValue)         listing.IsActive    = body.IsActive.Value;
        if (body.Description is not null)   listing.Description = body.Description;
        if (body.Images is not null)        listing.ImagesJson  = System.Text.Json.JsonSerializer.Serialize(body.Images);
        if (body.Features is not null)      listing.FeaturesJson = System.Text.Json.JsonSerializer.Serialize(body.Features);
        if (body.PartnerDiscountRateOverride.HasValue) listing.PartnerDiscountRateOverride = body.PartnerDiscountRateOverride;
        if (body.ClientDiscountRateOverride.HasValue)  listing.ClientDiscountRateOverride  = body.ClientDiscountRateOverride;
        if (body.VatRate.HasValue)          listing.VatRate     = body.VatRate;
        if (body.PricesIncludeVat.HasValue) listing.PricesIncludeVat = body.PricesIncludeVat.Value;
        if (body.LocationId.HasValue)       listing.LocationId      = body.LocationId;
        if (body.QuantityTotal.HasValue)    listing.QuantityTotal   = body.QuantityTotal;
        if (body.SizeM2.HasValue)           listing.SizeM2          = body.SizeM2;

        if (body.SupplierId.HasValue)
        {
            var s = await Db.Suppliers.FindAsync(body.SupplierId.Value);
            if (s is null) return NotFound(Error("Supplier not found"));
            listing.SupplierId = s.Id;
            listing.Supplier   = s;
        }

        if (body.Badge is not null)
        {
            if (string.IsNullOrEmpty(body.Badge))
                listing.Badge = null;
            else
            {
                var normalized = body.Badge.Replace("-", "");
                listing.Badge = Enum.TryParse<ListingBadge>(normalized, ignoreCase: true, out var b) ? b : null;
            }
        }

        listing.UpdatedAt = DateTime.UtcNow;
        await Audit("listing.updated", User.GetUserEmail(), listing.Title, null);
        await Db.SaveChangesAsync();

        return Ok(AdminMappers.MapListing(listing));
    }

    [HttpPatch("listings/{id:guid}/images")]
    public async Task<IActionResult> PatchListingImages(Guid id, [FromBody] UpdateImagesRequest body)
    {
        var listing = await Db.Listings
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (listing is null) return NotFound(Error("Listing not found"));

        listing.ImagesJson = System.Text.Json.JsonSerializer.Serialize(body.Images);
        listing.UpdatedAt  = DateTime.UtcNow;
        await Audit("listing.images_updated", User.GetUserEmail(), listing.Title, null);
        await Db.SaveChangesAsync();

        return Ok(AdminMappers.MapListing(listing));
    }

    [HttpDelete("listings/{id:guid}")]
    public async Task<IActionResult> DeleteListing(Guid id)
    {
        var listing = await Db.Listings.FindAsync(id);
        if (listing is null) return NotFound(Error("Listing not found"));

        Db.Listings.Remove(listing);
        await Audit("listing.deleted", User.GetUserEmail(), listing.Title, null);
        await Db.SaveChangesAsync();

        return NoContent();
    }
}
