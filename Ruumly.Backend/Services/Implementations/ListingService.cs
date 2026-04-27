using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class ListingService(RuumlyDbContext db, IDistributedCache cache) : IListingService
{
    private static readonly DistributedCacheEntryOptions SearchTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };

    private static readonly DistributedCacheEntryOptions FeaturedTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public async Task<PaginatedResult<ListingDto>> SearchAsync(ListingSearchRequest f)
    {
        var cacheKey = $"listings:search:{HashFilters(f)}";
        var cached   = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<PaginatedResult<ListingDto>>(cached)!;

        var query = db.Listings
            .Include(l => l.Supplier)
            .Include(l => l.Location)
            .Where(l => l.IsActive)
            .Where(l => l.LocationId == null)  // Units inside locations are accessed via the location page
            .AsQueryable();

        // ── Type filter ───────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(f.Type) &&
            Enum.TryParse<ListingType>(f.Type, ignoreCase: true, out var parsedType))
        {
            query = query.Where(l => l.Type == parsedType);
        }

        // ── Country filter ────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(f.Country))
            query = query.Where(l => l.Location != null && l.Location.Country == f.Country);

        // ── Size filter (m² range OR category code) ────────────────────────────
        // Listings without a SizeM2 value (movers, trailers) are excluded only if
        // a size filter is explicitly applied. Otherwise they pass through.
        if (f.MinSize.HasValue)
            query = query.Where(l => l.SizeM2 != null && l.SizeM2 >= f.MinSize.Value);

        if (f.MaxSize.HasValue)
            query = query.Where(l => l.SizeM2 != null && l.SizeM2 < f.MaxSize.Value);

        if (!string.IsNullOrWhiteSpace(f.SizeCategory))
        {
            var bucket = StorageSizeBuckets.FindByCode(f.SizeCategory);
            if (bucket != null)
            {
                if (bucket.MinM2.HasValue)
                    query = query.Where(l => l.SizeM2 != null && l.SizeM2 >= bucket.MinM2.Value);
                if (bucket.MaxM2.HasValue)
                    query = query.Where(l => l.SizeM2 != null && l.SizeM2 <  bucket.MaxM2.Value);
            }
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

        // ── Full-text search via indexed tsvector column ──────────────────────
        // EF.Functions.PlainToTsQuery MUST be called inline within the lambda for EF Core
        // to translate it to a server-side plainto_tsquery() call. Hoisting it to a local
        // variable causes "switched to client-evaluation" InvalidOperationException.
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var searchTerm = f.Q.Trim();
            query = query.Where(l =>
                l.SearchVector != null &&
                l.SearchVector.Matches(EF.Functions.PlainToTsQuery("simple", searchTerm)));
        }

        // ── Sort ──────────────────────────────────────────────────────────────
        query = f.Sort switch
        {
            "cheapest" => query.OrderBy(l => l.PriceFrom)
                               .ThenByDescending(l => (int)l.Supplier!.Tier),
            "rating"   => query.OrderByDescending(l => l.Rating)
                               .ThenByDescending(l => (int)l.Supplier!.Tier),
            "newest"   => query.OrderByDescending(l => l.CreatedAt)
                               .ThenByDescending(l => (int)l.Supplier!.Tier),
            _          => query.OrderByDescending(l => (int)l.Supplier!.Tier)
                               .ThenBy(l => l.CreatedAt),
        };

        // ── Pagination ────────────────────────────────────────────────────────
        var total = await query.CountAsync();
        var page  = Math.Max(1, f.Page);
        var limit = Math.Clamp(f.Limit, 1, 100);
        var items = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var result = new PaginatedResult<ListingDto>(
            items.Select(MapToDto).ToList(),
            total,
            page,
            limit,
            (page - 1) * limit + items.Count < total
        );

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), SearchTtl);
        return result;
    }

    public async Task<ListingDto?> GetByIdAsync(Guid id)
    {
        var cacheKey = $"listing:{id}";
        var cached   = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<ListingDto>(cached);

        var listing = await db.Listings
            .Include(l => l.Supplier)
            .Include(l => l.Location)
            .FirstOrDefaultAsync(l => l.Id == id && l.IsActive);

        if (listing is null) return null;

        if (db.Database.IsRelational())
            await db.Listings
                .Where(l => l.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ViewCount, l => l.ViewCount + 1));
        else
        {
            listing.ViewCount++;
            await db.SaveChangesAsync();
        }

        var dto = MapToDto(listing);
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), SearchTtl);
        return dto;
    }

    public async Task<List<ListingDto>> GetFeaturedAsync()
    {
        const string cacheKey = "listings:featured";
        var cached = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<List<ListingDto>>(cached)!;

        // Badge priority: Promoted(4) > BestValue(3) > Closest(2) > Cheapest(1)
        var listings = await db.Listings
            .Include(l => l.Supplier)
            .Where(l => l.Badge != null && l.IsActive)
            .ToListAsync();

        var result = listings
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

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), FeaturedTtl);
        return result;
    }

    public async Task InvalidateListingAsync(Guid id)
    {
        await cache.RemoveAsync($"listing:{id}");
        await cache.RemoveAsync("listings:featured");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string HashFilters(ListingSearchRequest f)
    {
        var json  = JsonSerializer.Serialize(f);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..16];
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static ListingDto MapToDto(Listing l) => new(
        Id:          l.Id,
        Type:        l.Type.ToString().ToLower(),
        Title:       l.Title,
        SupplierName: l.Supplier?.Name ?? string.Empty,
        Address:     l.Location?.Address ?? l.Address,
        City:        l.Location?.City    ?? l.City,
        Lat:         l.Location?.Lat     ?? l.Lat,
        Lng:         l.Location?.Lng     ?? l.Lng,
        PriceFrom:   l.PriceFrom,
        PriceUnit:   l.PriceUnit,
        AvailableNow: l.AvailableNow,
        Badge:       BadgeToString(l.Badge),
        Rating:      l.Rating,
        ReviewCount: l.ReviewCount,
        Description: l.Description,
        Images:      DeserializeList(l.ImagesJson) is { Count: > 0 } imgs
                         ? imgs
                         : l.Location != null
                             ? DeserializeList(l.Location.ImagesJson)
                             : [],
        Features:    DeserializeDict(l.FeaturesJson),
        PartnerDiscountRateOverride: l.PartnerDiscountRateOverride,
        ClientDiscountRateOverride:  l.ClientDiscountRateOverride,
        ClientDiscountRate:          l.Supplier?.ClientDiscountRate,
        VatRate:         l.VatRate,
        PricesIncludeVat: l.PricesIncludeVat,
        SupplierId:      l.SupplierId,
        SizeM2:          l.SizeM2,
        QuantityTotal:   l.QuantityTotal,
        LocationId:      l.LocationId,
        ViewCount:       l.ViewCount,
        IsVerified:      l.Supplier?.IsVerified ?? false,
        FoundingPartner: l.Supplier?.FoundingPartner ?? false
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
