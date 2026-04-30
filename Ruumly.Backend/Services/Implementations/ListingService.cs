using System.Globalization;
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

public class ListingService(
    RuumlyDbContext       db,
    IDistributedCache     cache,
    IPricingConfigService pricingConfigService) : IListingService
{
    private static readonly DistributedCacheEntryOptions SearchTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };

    private static readonly DistributedCacheEntryOptions FeaturedTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public async Task<PaginatedResult<ListingDto>> SearchAsync(ListingSearchRequest f, string? language = null)
    {
        var cacheKey = $"listings:search:{HashFilters(f)}:{language ?? "_"}";
        var cached   = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<PaginatedResult<ListingDto>>(cached)!;

        var pricingConfig = await pricingConfigService.GetAsync();

        var query = db.Listings
            .Include(l => l.Supplier)
            .Include(l => l.Location)
            .Where(l => l.IsActive)
            // Include standalone listings AND listings inside synthetic (auto-created)
            // Locations. Real, user-curated Locations are shown as their own cards;
            // their listings are accessed via the Location detail page and not
            // duplicated in the listing search results.
            .Where(l => l.LocationId == null
                     || (l.Location != null && l.Location.IsSynthetic))
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
        // Diacritic-insensitive: the SearchVector trigger applies unaccent() at index time,
        // and we strip diacritics from the query in C# (avoids EF/Npgsql translation of
        // EF.Functions.Unaccent which isn't available in older Npgsql versions).
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var searchTerm = RemoveDiacritics(f.Q.Trim());
            query = query.Where(l =>
                l.SearchVector != null &&
                l.SearchVector.Matches(EF.Functions.PlainToTsQuery("simple", searchTerm)));
        }

        // ── Sort ──────────────────────────────────────────────────────────────
        // Tier is stored as string via HasConversion<string>() in RuumlyDbContext,
        // so (int)l.Supplier.Tier translates to "Tier"::int in SQL and Postgres
        // throws 22P02 ("Starter" cannot cast to int). Use a CASE WHEN expression
        // EF can translate without casting from text.
        query = f.Sort switch
        {
            "cheapest" => query.OrderBy(l => l.PriceFrom)
                               .ThenByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                    : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                    : 1),
            "rating"   => query.OrderByDescending(l => l.Rating)
                               .ThenByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                    : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                    : 1),
            "newest"   => query.OrderByDescending(l => l.CreatedAt)
                               .ThenByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                    : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                    : 1),
            _          => query.OrderByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                    : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                    : 1)
                               .ThenBy(l => l.CreatedAt),
        };

        // ── Pagination ────────────────────────────────────────────────────────
        var total = await query.CountAsync();
        var page  = Math.Max(1, f.Page);
        var limit = Math.Clamp(f.Limit, 1, 200);
        var items = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var result = new PaginatedResult<ListingDto>(
            items.Select(l => MapToDto(l, language, pricingConfig)).ToList(),
            total,
            page,
            limit,
            (page - 1) * limit + items.Count < total
        );

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), SearchTtl);
        return result;
    }

    public async Task<ListingDto?> GetByIdAsync(Guid id, string? language = null)
    {
        var cacheKey = $"listing:{id}:{language ?? "_"}";
        var cached   = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<ListingDto>(cached);

        var pricingConfig = await pricingConfigService.GetAsync();

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

        var dto = MapToDto(listing, language, pricingConfig);
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), SearchTtl);
        return dto;
    }

    public async Task<List<ListingDto>> GetFeaturedAsync(string? language = null)
    {
        var cacheKey = $"listings:featured:{language ?? "_"}";

        var pricingConfig = await pricingConfigService.GetAsync();
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
            .Select(l => MapToDto(l, language, pricingConfig))
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

    // Strips combining diacritical marks so the search query matches the
    // unaccented SearchVector index. "Rīga" → "Riga", "Pärnu" → "Parnu".
    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the description in the requested language, falling back to
    /// the canonical Description (Estonian) if a translation isn't present.
    /// </summary>
    private static string ResolveDescription(Listing l, string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return l.Description;

        var lang = language.ToLowerInvariant();
        var translations = l.DescriptionTranslations;
        return translations.TryGetValue(lang, out var translated) && !string.IsNullOrWhiteSpace(translated)
            ? translated
            : l.Description;
    }

    private static ListingDto MapToDto(Listing l, string? language, PricingConfig pricingConfig)
    {
        var (partner, customer) = PricingHelpers.ComputeEffectiveDiscounts(
            listingPartnerOverride:  l.PartnerDiscountRateOverride,
            listingCustomerOverride: l.ClientDiscountRateOverride,
            supplierPartnerRate:     l.Supplier?.PartnerDiscountRate,
            defaultPartnerDiscount:  pricingConfig.DefaultPartnerDiscountRate,
            ruumlyMinMargin:         pricingConfig.RuumlyMinMarginRate);

        return new ListingDto(
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
        Description: ResolveDescription(l, language),
        Images:      DeserializeList(l.ImagesJson) is { Count: > 0 } imgs
                         ? imgs
                         : l.Location != null
                             ? DeserializeList(l.Location.ImagesJson)
                             : [],
        Features:    DeserializeDict(l.FeaturesJson),
        PartnerDiscountRateOverride: l.PartnerDiscountRateOverride,
        ClientDiscountRateOverride:  l.ClientDiscountRateOverride,
        ClientDiscountRate:          l.Supplier?.ClientDiscountRate,
        EffectiveCustomerDiscount:   customer,
        EffectivePartnerDiscount:    partner,
        VatRate:         l.VatRate,
        PricesIncludeVat: l.PricesIncludeVat,
        SupplierId:      l.SupplierId,
        SizeM2:          l.SizeM2,
        QuantityTotal:   l.QuantityTotal,
        LocationId:      l.LocationId,
        ViewCount:       l.ViewCount,
        IsVerified:      l.Supplier?.IsVerified ?? false,
        FoundingPartner: l.Supplier?.FoundingPartner ?? false);
    }

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
