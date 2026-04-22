using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class FeaturedPartnersService(RuumlyDbContext db, IDistributedCache cache) : IFeaturedPartnersService
{
    private static readonly DistributedCacheEntryOptions CacheTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) };

    public async Task<List<FeaturedPartnerDto>> GetFeaturedAsync()
    {
        const string cacheKey = "suppliers:featured-partners";
        var cached = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<List<FeaturedPartnerDto>>(cached)!;

        // Eligible: active, verified, Standard or Premium tier
        var candidates = await db.Suppliers
            .Include(s => s.Listings)
            .Where(s => s.IsActive && s.IsVerified && s.Tier >= SupplierTier.Standard)
            .ToListAsync();

        // Daily rotation: deterministic shuffle seeded by today's date
        var daySeed = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var result = candidates
            .OrderBy(s => HashForRotation(s.Id, daySeed))
            .Take(6)
            .Select(s => new FeaturedPartnerDto(
                Id:           s.Id,
                Name:         s.Name,
                Country:      s.Country,
                Rating:       s.Rating,
                ReviewCount:  s.ReviewCount,
                Tier:         s.Tier.ToString(),
                IsVerified:   s.IsVerified,
                ListingCount: s.Listings.Count(l => l.IsActive)))
            .ToList();

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), CacheTtl);
        return result;
    }

    private static int HashForRotation(Guid id, string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{id}{seed}"));
        return BitConverter.ToInt32(bytes, 0);
    }
}
