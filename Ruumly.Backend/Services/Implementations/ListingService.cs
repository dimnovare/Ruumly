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

    public async Task<PaginatedResult<ListingDto>> SearchAsync(ListingSearchRequest f, string? language = null, CancellationToken ct = default)
    {
        // Defence-in-depth: reject oversized inputs even if the [MaxLength] annotation
        // on the DTO was bypassed (e.g. called directly from another service).
        if (!string.IsNullOrWhiteSpace(f.Q)    && f.Q.Length    > 200)
            return PaginatedResult<ListingDto>.Empty(f.Page, f.Limit);
        if (!string.IsNullOrWhiteSpace(f.City) && f.City.Length > 100)
            return PaginatedResult<ListingDto>.Empty(f.Page, f.Limit);

        // Read vertical-visibility flags once and fold them into the cache key so a
        // toggle change can't be served stale, and hidden verticals never appear.
        var (showMoving, showTrailer) = await GetVerticalVisibilityAsync(ct);

        var cacheKey = $"listings:search:{HashFilters(f)}:{language ?? "_"}:m{(showMoving ? 1 : 0)}t{(showTrailer ? 1 : 0)}";
        var cached   = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            try
            {
                var hit = JsonSerializer.Deserialize<PaginatedResult<ListingDto>>(cached);
                if (hit is not null) return hit;
            }
            catch (JsonException)
            {
                await cache.RemoveAsync(cacheKey, ct);
            }
        };

        var pricingConfig = await pricingConfigService.GetAsync();

        var query = db.Listings
            .AsNoTracking()
            .Include(l => l.Supplier)
            .Include(l => l.Location)
            .Where(l => l.IsActive && l.Supplier != null && l.Supplier.IsActive)
            .AsQueryable();

        // Hide listings inside real (non-synthetic) Locations from generic search
        // — those are surfaced via Location detail pages — UNLESS the caller is
        // explicitly drilling into a supplier or location, in which case they want
        // everything.
        var hasExplicitTarget = f.SupplierId.HasValue || f.LocationId.HasValue;
        if (!hasExplicitTarget)
        {
            query = query.Where(l => l.LocationId == null
                                  || (l.Location != null && l.Location.IsSynthetic));
        }

        if (f.SupplierId.HasValue) query = query.Where(l => l.SupplierId == f.SupplierId.Value);
        if (f.LocationId.HasValue) query = query.Where(l => l.LocationId == f.LocationId.Value);

        // ── Type filter ───────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(f.Type) &&
            Enum.TryParse<ListingType>(f.Type, ignoreCase: true, out var parsedType))
        {
            query = query.Where(l => l.Type == parsedType);
        }

        // ── Storage-only launch guard ─────────────────────────────────────────
        // Exclude disabled verticals BEFORE Count/pagination so totals stay correct.
        if (!showMoving)  query = query.Where(l => l.Type != ListingType.Moving);
        if (!showTrailer) query = query.Where(l => l.Type != ListingType.Trailer);

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
        // EF.Functions.ILike maps to PostgreSQL's native ILIKE operator, which avoids
        // the LOWER() function call on the column side and allows a pg_trgm GIN index.
        if (!string.IsNullOrWhiteSpace(f.City))
        {
            var city = f.City.Trim();
            query = query.Where(l => EF.Functions.ILike(l.City, $"%{city}%"));
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
            // Strip tsquery operator characters (&, |, !, :, *, (, ), <, >) to prevent
            // plainto_tsquery parse errors on special-character input.
            searchTerm = System.Text.RegularExpressions.Regex.Replace(
                searchTerm, @"[&|!:()*<>]", " ");
            searchTerm = System.Text.RegularExpressions.Regex.Replace(
                searchTerm, @"\s+", " ").Trim();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(l =>
                    l.SearchVector != null &&
                    l.SearchVector.Matches(EF.Functions.PlainToTsQuery("simple", searchTerm)));
            }
        }

        // ── Sort ──────────────────────────────────────────────────────────────
        // Tier is stored as string via HasConversion<string>() in RuumlyDbContext,
        // so (int)l.Supplier.Tier translates to "Tier"::int in SQL and Postgres
        // throws 22P02 ("Starter" cannot cast to int). Use a CASE WHEN expression
        // EF can translate without casting from text.
        query = f.Sort switch
        {
            "cheapest"   => query.OrderBy(l => l.PriceFrom)
                                 .ThenByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                      : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                      : 1),
            "rating"     => query.OrderByDescending(l => l.Rating)
                                 .ThenBy(l => l.PriceFrom)
                                 .ThenByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                      : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                      : 1),
            "newest"     => query.OrderByDescending(l => l.CreatedAt)
                                 .ThenByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                      : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                      : 1),
            // Best value = high rating per €; rating bucketed to half-stars so
            // a 4.8★ at €49 beats a 4.9★ at €99.
            "best-value" => query.OrderByDescending(l => Math.Floor((double)l.Rating * 2) / 2.0)
                                 .ThenBy(l => l.PriceFrom)
                                 .ThenByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                      : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                      : 1),
            // "best" (and any unknown value) = balanced default: tier first,
            // then rating, then recency.
            _            => query.OrderByDescending(l => l.Supplier!.Tier == SupplierTier.Premium  ? 3
                                                      : l.Supplier!.Tier == SupplierTier.Standard ? 2
                                                      : 1)
                                 .ThenByDescending(l => l.Rating)
                                 .ThenByDescending(l => l.CreatedAt),
        };

        // ── Pagination ────────────────────────────────────────────────────────
        var total = await query.CountAsync(ct);
        var page  = Math.Max(1, f.Page);
        var limit = Math.Clamp(f.Limit, 1, 200);
        var items = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        var result = new PaginatedResult<ListingDto>(
            items.Select(l => MapToDto(l, language, pricingConfig)).ToList(),
            total,
            page,
            limit,
            (page - 1) * limit + items.Count < total
        );

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), SearchTtl, ct);
        return result;
    }

    public async Task<ListingDto?> GetByIdAsync(Guid id, string? language = null)
    {
        var cacheKey = $"listing:{id}:{language ?? "_"}";
        var cached   = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
        {
            try
            {
                var hit = JsonSerializer.Deserialize<ListingDto>(cached);
                if (hit is not null) return hit;
            }
            catch (JsonException)
            {
                await cache.RemoveAsync(cacheKey);
            }
        }

        var pricingConfig = await pricingConfigService.GetAsync();

        // Not AsNoTracking: the non-relational (InMemory) view-count path below
        // mutates this entity and persists via SaveChangesAsync; AsNoTracking
        // would silently drop that bump. The relational path uses ExecuteUpdate.
        var listing = await db.Listings
            .Include(l => l.Supplier)
            .Include(l => l.Location)
            .FirstOrDefaultAsync(l => l.Id == id && l.IsActive
                                   && l.Supplier != null && l.Supplier.IsActive);

        if (listing is null) return null;

        // Storage-only launch guard: treat a listing in a disabled vertical as
        // non-existent (=> controller 404), so deep links and the OG worker get a
        // 404 rather than live data. Checked before the view-count bump.
        var (showMoving, showTrailer) = await GetVerticalVisibilityAsync();
        if ((listing.Type == ListingType.Moving  && !showMoving) ||
            (listing.Type == ListingType.Trailer && !showTrailer))
            return null;

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
        {
            try
            {
                var hit = JsonSerializer.Deserialize<List<ListingDto>>(cached);
                if (hit is not null) return hit;
            }
            catch (JsonException)
            {
                await cache.RemoveAsync(cacheKey);
            }
        };

        // Storage-only launch guard: drop disabled verticals BEFORE Take(4) so the
        // featured rail never surfaces hidden Moving/Trailer listings.
        var (showMoving, showTrailer) = await GetVerticalVisibilityAsync();

        // Filter (active + visible verticals) in SQL with AsNoTracking. The badge
        // set is small and curated, so order by priority + Take(4) in memory —
        // a switch-expression CASE inside OrderBy is not reliably translatable by
        // Npgsql and would 500 the live featured rail (InMemory tests can't catch it).
        var badged = await db.Listings
            .AsNoTracking()
            .Include(l => l.Supplier)
            .Where(l => l.Badge != null && l.IsActive
                      && l.Supplier != null && l.Supplier.IsActive)
            .Where(l => (showMoving  || l.Type != ListingType.Moving)
                     && (showTrailer || l.Type != ListingType.Trailer))
            .ToListAsync();

        // Badge priority: Promoted(4) > BestValue(3) > Closest(2) > Cheapest(1)
        var result = badged
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

    // Storage-only launch guard: Moving/Trailer verticals are hidden behind admin
    // toggles (showMovingService / showTrailerService in PlatformSettings). The
    // public listings API must honour these so deep links and the OG worker can't
    // leak hidden verticals. Settings are stored as string "true"/"false"; a missing
    // row defaults to enabled (mirrors SitemapController). One read per call.
    private async Task<(bool showMoving, bool showTrailer)> GetVerticalVisibilityAsync(CancellationToken ct = default)
    {
        var showMoving = await db.PlatformSettings
            .Where(s => s.Key == "showMovingService")
            .Select(s => s.Value).FirstOrDefaultAsync(ct) != "false";
        var showTrailer = await db.PlatformSettings
            .Where(s => s.Key == "showTrailerService")
            .Select(s => s.Value).FirstOrDefaultAsync(ct) != "false";
        return (showMoving, showTrailer);
    }

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
        SupplierSlug: l.Supplier?.Slug,
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
        FoundingPartner: l.Supplier?.FoundingPartner ?? false,
        BookingEnabled:  l.Supplier?.BookingEnabled ?? false,
        ContractSigningEnabled: l.Supplier?.ContractSigningEnabled ?? false,
        DirectPaymentEnabled: l.Supplier?.DirectPaymentEnabled ?? false,
        RuumlyPaymentEnabled: l.Supplier?.RuumlyPaymentEnabled ?? false);
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
