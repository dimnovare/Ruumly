using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

public sealed record ProviderCandidateSearch(
    string? Query,
    bool AllEstonia,
    bool AllCategories,
    double RadiusKm,
    int Limit);

public static class ProviderCandidateFinder
{
    private const double EarthRadiusKm = 6371.0088;

    public static async Task<ProviderCandidateResponse> SearchAsync(
        RuumlyDbContext db,
        DemandLead lead,
        ProviderCandidateSearch search,
        CancellationToken ct = default)
    {
        var suppliers = await db.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Include(s => s.Listings.Where(l => l.IsActive))
            .ToListAsync(ct);

        var supplierIds = suppliers.Select(s => s.Id).ToList();
        var locations = await db.SupplierLocations
            .AsNoTracking()
            .Where(l => l.IsActive && supplierIds.Contains(l.SupplierId))
            .ToListAsync(ct);

        var contacted = await db.ProviderOutreaches
            .AsNoTracking()
            .Where(o => o.DemandLeadId == lead.Id)
            .GroupBy(o => o.SupplierId)
            .Select(g => new { SupplierId = g.Key, Last = g.Max(x => x.SentAt) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Last, ct);

        return BuildResponse(suppliers, locations, contacted, lead, search);
    }

    internal static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double value) => value * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2))
              * Math.Pow(Math.Sin(dLng / 2), 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static ProviderCandidateResponse BuildResponse(
        IReadOnlyList<Supplier> suppliers,
        IReadOnlyList<SupplierLocation> locations,
        IReadOnlyDictionary<Guid, DateTime> contacted,
        DemandLead lead,
        ProviderCandidateSearch search)
    {
        var supplierLocations = locations
            .GroupBy(l => l.SupplierId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchingSuppliers = suppliers
            .Where(s => MatchesCategory(s, lead, search.AllCategories))
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .ToList();

        var anchor = BuildAnchor(matchingSuppliers, locations, lead.City);
        var candidates = matchingSuppliers
            .Select(s => BuildCandidate(
                s,
                supplierLocations.GetValueOrDefault(s.Id, []),
                // The contacted map holds non-nullable DateTime, so GetValueOrDefault
                // returns DateTime.MinValue (NOT null) for a never-contacted supplier —
                // which BuildCandidate would then read as alreadyContacted=true for every
                // provider on a fresh lead. Only pass a real timestamp when a row exists.
                contacted.TryGetValue(s.Id, out var lastOutreach) ? lastOutreach : (DateTime?)null,
                lead,
                search,
                anchor))
            .Where(c => MatchesQuery(c, search.Query))
            .ToList();

        if (!search.AllEstonia)
        {
            candidates = anchor is not null
                ? candidates.Where(c => c.Item.IsExactCity || (c.Item.DistanceKm is { } distance && distance <= search.RadiusKm)).ToList()
                : candidates.Where(c => c.Item.IsExactCity || !string.IsNullOrWhiteSpace(search.Query)).ToList();
        }

        candidates = candidates
            .OrderByDescending(c => c.Item.IsExactCity)
            .ThenBy(c => c.Item.DistanceKm ?? double.MaxValue)
            .ThenBy(c => c.Item.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = candidates.Count;
        return new ProviderCandidateResponse(
            candidates.Take(search.Limit).Select(c => c.Item).ToList(),
            total,
            search.AllEstonia ? "all" : "nearby",
            search.RadiusKm,
            anchor);
    }

    private static CandidateRow BuildCandidate(
        Supplier supplier,
        IReadOnlyList<SupplierLocation> locations,
        DateTime? lastOutreachAt,
        DemandLead lead,
        ProviderCandidateSearch search,
        ProviderCandidateAnchorDto? anchor)
    {
        var matchingListings = supplier.Listings
            .Where(l => l.IsActive && MatchesListingCategory(l, lead.Category, search.AllCategories))
            .OrderByDescending(l => IsExactCity(l.City, lead.City))
            .ThenByDescending(l => l.UpdatedAt)
            .ThenBy(l => l.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var locationRows = locations
            .Select(l => new CandidateLocation(l.Id, l.Name, l.City, l.Address, l.Lat, l.Lng))
            .ToList();
        if (locationRows.Count == 0 && matchingListings.FirstOrDefault() is { } fallbackListing)
        {
            locationRows.Add(new CandidateLocation(
                fallbackListing.LocationId,
                fallbackListing.Title,
                fallbackListing.City,
                fallbackListing.Address,
                fallbackListing.Lat,
                fallbackListing.Lng));
        }

        var rankedLocations = locationRows
            .Select(l => l with { DistanceKm = Distance(l.Lat, l.Lng, anchor) })
            .OrderByDescending(l => anchor is null && IsExactCity(l.City, lead.City))
            .ThenBy(l => l.DistanceKm ?? double.MaxValue)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = rankedLocations.FirstOrDefault();
        var primaryListing = primary is null
            ? matchingListings.FirstOrDefault()
            : matchingListings
                .OrderByDescending(l => l.LocationId == primary.Id)
                .ThenByDescending(l => IsExactCity(l.City, lead.City))
                .ThenByDescending(l => l.UpdatedAt)
                .ThenBy(l => l.Title, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        var serviceTypes = ServiceCategories.ParseServiceTypes(supplier.ServiceTypesJson);

        var item = new ProviderCandidateDto(
            supplier.Id,
            supplier.Name,
            supplier.ContactEmail,
            supplier.ContactPhone,
            serviceTypes,
            primary?.Id,
            primary?.Name,
            primary?.City,
            primary?.Address,
            ValidCoordinate(primary?.Lat, primary?.Lng) ? primary!.Lat : null,
            ValidCoordinate(primary?.Lat, primary?.Lng) ? primary!.Lng : null,
            primary?.DistanceKm,
            primary is not null && IsExactCity(primary.City, lead.City),
            primaryListing?.Id,
            primaryListing?.Title,
            primaryListing?.PriceFrom,
            primaryListing?.PriceUnit,
            lastOutreachAt is not null,
            lastOutreachAt,
            supplier.ContactEmailUnusable,
            supplier.ContactEmailBouncedAt,
            rankedLocations.Skip(1).Where(l => l.Id is not null).Select(l => new ProviderCandidateLocationDto(
                l.Id!.Value,
                l.Name,
                l.City,
                l.Address,
                ValidCoordinate(l.Lat, l.Lng) ? l.Lat : null,
                ValidCoordinate(l.Lat, l.Lng) ? l.Lng : null,
                l.DistanceKm)).ToList());

        return new CandidateRow(item, rankedLocations);
    }

    private static bool MatchesCategory(Supplier supplier, DemandLead lead, bool allCategories)
    {
        if (allCategories || lead.Category == DemandLeadCategory.Any) return true;

        var hasListing = supplier.Listings.Any(l => l.IsActive && MatchesListingCategory(l, lead.Category, false));
        var slug = ServiceCategories.SlugFor(lead.Category);
        var hasDirectoryService = supplier.IsDirectoryListing
            && ServiceCategories.ParseServiceTypes(supplier.ServiceTypesJson)
                .Contains(slug, StringComparer.OrdinalIgnoreCase);
        return hasListing || hasDirectoryService;
    }

    private static bool MatchesListingCategory(Listing listing, DemandLeadCategory category, bool allCategories) =>
        allCategories || category == DemandLeadCategory.Any || category switch
        {
            DemandLeadCategory.Warehouse => listing.Type == ListingType.Warehouse,
            DemandLeadCategory.Moving => listing.Type == ListingType.Moving,
            DemandLeadCategory.Trailer => listing.Type == ListingType.Trailer,
            _ => false,
        };

    private static ProviderCandidateAnchorDto? BuildAnchor(
        IReadOnlyList<Supplier> suppliers,
        IReadOnlyList<SupplierLocation> locations,
        string leadCity)
    {
        var supplierIds = suppliers.Select(s => s.Id).ToHashSet();
        var coordinates = locations
            .Where(l => supplierIds.Contains(l.SupplierId) && IsExactCity(l.City, leadCity))
            .Select(l => (Lat: (double?)l.Lat, Lng: (double?)l.Lng))
            .Concat(suppliers.SelectMany(s => s.Listings)
                .Where(l => l.IsActive && IsExactCity(l.City, leadCity))
                .Select(l => (Lat: (double?)l.Lat, Lng: (double?)l.Lng)))
            .Where(c => ValidCoordinate(c.Lat, c.Lng))
            .ToList();

        return coordinates.Count == 0
            ? null
            : new ProviderCandidateAnchorDto(
                coordinates.Average(c => c.Lat!.Value),
                coordinates.Average(c => c.Lng!.Value));
    }

    private static bool MatchesQuery(CandidateRow candidate, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        return Contains(candidate.Item.SupplierName, query)
            || Contains(candidate.Item.ContactEmail, query)
            || Contains(candidate.Item.ContactPhone, query)
            || candidate.Locations.Any(l => Contains(l.Name, query)
                                            || Contains(l.City, query)
                                            || Contains(l.Address, query));
    }

    private static bool Contains(string? value, string query) =>
        value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static double? Distance(double? lat, double? lng, ProviderCandidateAnchorDto? anchor) =>
        anchor is not null && ValidCoordinate(lat, lng)
            ? Math.Round(HaversineKm(anchor.Lat, anchor.Lng, lat!.Value, lng!.Value), 1)
            : null;

    private static bool ValidCoordinate(double? lat, double? lng) =>
        lat is >= -90 and <= 90
        && lng is >= -180 and <= 180
        && (lat != 0 || lng != 0);

    private static bool IsExactCity(string? city, string leadCity) =>
        !string.IsNullOrWhiteSpace(leadCity)
        && string.Equals(city?.Trim(), leadCity.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record CandidateRow(ProviderCandidateDto Item, IReadOnlyList<CandidateLocation> Locations);

    private sealed record CandidateLocation(
        Guid? Id,
        string Name,
        string City,
        string Address,
        double? Lat,
        double? Lng,
        double? DistanceKm = null);
}
