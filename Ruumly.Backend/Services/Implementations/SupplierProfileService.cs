using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class SupplierProfileService(RuumlyDbContext db, IDistributedCache cache)
    : ISupplierProfileService
{
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    { "dashboard", "onboarding", "new", "edit", "admin", "api" };

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<SupplierProfileDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (ReservedSlugs.Contains(slug)) return null;

        var cacheKey = $"supplier:profile:{slug}";
        var cached   = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<SupplierProfileDto>(cached);

        var supplier = await db.Suppliers
            .Include(s => s.Listings)
            .FirstOrDefaultAsync(s => s.Slug == slug && s.IsActive, ct);
        if (supplier is null) return null;

        // Single round-trip: locations + each location's active-listing count.
        var locationRows = await db.SupplierLocations
            .Where(l => l.SupplierId == supplier.Id && l.IsActive && !l.IsSynthetic)
            .OrderBy(l => l.City).ThenBy(l => l.Name)
            .Select(l => new
            {
                Loc = l,
                ListingCount = db.Listings.Count(li => li.LocationId == l.Id && li.IsActive),
            })
            .ToListAsync(ct);

        Dictionary<string, string>? longDescription = null;
        if (!string.IsNullOrWhiteSpace(supplier.LongDescriptionTranslationsJson))
        {
            try
            {
                longDescription = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    supplier.LongDescriptionTranslationsJson);
            }
            catch (JsonException)
            {
                longDescription = null;
            }
        }

        var locations = locationRows.Select(r => new SupplierProfileLocationDto(
            r.Loc.Id,
            r.Loc.Name,
            r.Loc.Address,
            r.Loc.City,
            r.Loc.Country,
            r.Loc.Lat,
            r.Loc.Lng,
            r.Loc.OpeningHours,
            r.Loc.Description,
            r.Loc.Images,
            r.ListingCount)).ToList();

        var dto = new SupplierProfileDto(
            Id:              supplier.Id,
            Slug:            supplier.Slug!,
            Name:            supplier.Name,
            Country:         supplier.Country,
            Tagline:         supplier.Tagline,
            LongDescription: longDescription,
            LogoUrl:         supplier.LogoUrl,
            HeroImageUrl:    supplier.HeroImageUrl,
            WebsiteUrl:      supplier.WebsiteUrl,
            FoundedYear:     supplier.FoundedYear,
            Rating:          supplier.Rating,
            ReviewCount:     supplier.ReviewCount,
            Tier:            supplier.Tier.ToString(),
            IsVerified:      supplier.IsVerified,
            FoundingPartner: supplier.FoundingPartner,
            LocationCount:   locations.Count,
            ListingCount:    supplier.Listings.Count(l => l.IsActive),
            Locations:       locations);

        await cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(dto),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
            ct);

        return dto;
    }
}
