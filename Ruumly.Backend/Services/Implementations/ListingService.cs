using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class ListingService(RuumlyDbContext db) : IListingService
{
    public async Task<PaginatedResult<ListingDto>> SearchAsync(ListingSearchRequest f)
    {
        var query = db.Listings
            .Include(l => l.Supplier)
            .Where(l => l.IsActive)
            .AsQueryable();

        // ── Type filter ───────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(f.Type) &&
            Enum.TryParse<ListingType>(f.Type, ignoreCase: true, out var parsedType))
        {
            query = query.Where(l => l.Type == parsedType);
        }

        // ── City filter (case-insensitive) ────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(f.City))
        {
            var city = f.City.ToLower();
            query = query.Where(l => l.City.ToLower().Contains(city));
        }

        // ── PriceMax filter ───────────────────────────────────────────────────
        if (f.PriceMax.HasValue)
            query = query.Where(l => l.PriceFrom <= f.PriceMax.Value);

        // ── AvailableNow filter ───────────────────────────────────────────────
        if (f.AvailableNow == true)
            query = query.Where(l => l.AvailableNow);

        // ── Full-text search (title, city, address) ───────────────────────────
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var q = f.Q.ToLower();
            query = query.Where(l =>
                l.Title.ToLower().Contains(q) ||
                l.City.ToLower().Contains(q)  ||
                l.Address.ToLower().Contains(q));
        }

        // ── Sort ──────────────────────────────────────────────────────────────
        query = f.Sort switch
        {
            "cheapest" => query.OrderBy(l => l.PriceFrom),
            "rating"   => query.OrderByDescending(l => l.Rating),
            "newest"   => query.OrderByDescending(l => l.CreatedAt),
            _          => query.OrderBy(l => l.CreatedAt),   // stable default
        };

        // ── Pagination ────────────────────────────────────────────────────────
        var total  = await query.CountAsync();
        var page   = Math.Max(1, f.Page);
        var limit  = Math.Clamp(f.Limit, 1, 100);
        var items  = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        return new PaginatedResult<ListingDto>(
            items.Select(MapToDto).ToList(),
            total,
            page,
            limit,
            (page - 1) * limit + items.Count < total
        );
    }

    public async Task<ListingDto?> GetByIdAsync(Guid id)
    {
        var listing = await db.Listings
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id && l.IsActive);

        return listing is null ? null : MapToDto(listing);
    }

    public async Task<List<ListingDto>> GetFeaturedAsync()
    {
        // Badge priority: Promoted(4) > BestValue(3) > Closest(2) > Cheapest(1)
        var listings = await db.Listings
            .Include(l => l.Supplier)
            .Where(l => l.Badge != null && l.IsActive)
            .ToListAsync();

        return listings
            .OrderByDescending(l => l.Badge switch
            {
                ListingBadge.Promoted  => 4,
                ListingBadge.BestValue => 3,
                ListingBadge.Closest   => 2,
                ListingBadge.Cheapest  => 1,
                _                      => 0,
            })
            .Take(4)
            .Select(MapToDto)
            .ToList();
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static ListingDto MapToDto(Listing l) => new(
        Id:          l.Id,
        Type:        l.Type.ToString().ToLower(),
        Title:       l.Title,
        SupplierName: l.Supplier?.Name ?? string.Empty,
        Address:     l.Address,
        City:        l.City,
        Lat:         l.Lat,
        Lng:         l.Lng,
        PriceFrom:   l.PriceFrom,
        PriceUnit:   l.PriceUnit,
        AvailableNow: l.AvailableNow,
        Badge:       BadgeToString(l.Badge),
        Rating:      l.Rating,
        ReviewCount: l.ReviewCount,
        Description: l.Description,
        Images:      DeserializeList(l.ImagesJson),
        Features:    DeserializeDict(l.FeaturesJson),
        PartnerDiscountRateOverride: l.PartnerDiscountRateOverride,
        ClientDiscountRateOverride:  l.ClientDiscountRateOverride,
        VatRate:         l.VatRate,
        PricesIncludeVat: l.PricesIncludeVat,
        SupplierId:      l.SupplierId,
        SizeM2:          l.SizeM2,
        QuantityTotal:   l.QuantityTotal,
        LocationId:      l.LocationId
    );

    private static string? BadgeToString(ListingBadge? badge) => badge switch
    {
        ListingBadge.Cheapest  => "cheapest",
        ListingBadge.Closest   => "closest",
        ListingBadge.BestValue => "best-value",
        ListingBadge.Promoted  => "promoted",
        _                      => null,
    };

    private static List<string> DeserializeList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static Dictionary<string, object> DeserializeDict(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? []; }
        catch { return []; }
    }
}
