using FluentAssertions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Supplier.MaxServiceDistanceKm — a provider's own service area.
///
/// Reproduces Ants Viljandi as of 2026-11-25: hands out and takes back its vans
/// only in Viljandi, but received Pärnu requests (~95 km) whenever Pärnu's thin
/// supply widened the search to 100 km.
/// </summary>
public class ProviderServiceDistanceTests
{
    private const double ParnuLat    = 58.3859, ParnuLng    = 24.4971;
    private const double ViljandiLat = 58.3645, ViljandiLng = 25.5878; // ~64 km east as the crow flies

    [Fact]
    public async Task LimitedProvider_IsNotACandidate_ForARequestBeyondItsArea()
    {
        var (db, viljandi, _) = await SeedAsync(viljandiLimitKm: 25);
        var lead = await AddLeadAsync(db, "Pärnu");

        var result = await Search(db, lead, radiusKm: 100);

        result.Items.Should().NotContain(c => c.SupplierId == viljandi.Id,
            "Viljandi only serves requests within 25 km");
    }

    [Fact]
    public async Task LimitedProvider_StillGetsRequestsFromItsOwnTown()
    {
        var (db, viljandi, _) = await SeedAsync(viljandiLimitKm: 25);
        var lead = await AddLeadAsync(db, "Viljandi");

        var result = await Search(db, lead, radiusKm: 25);

        result.Items.Should().Contain(c => c.SupplierId == viljandi.Id);
    }

    [Fact]
    public async Task LimitedProvider_IsExcludedEvenFromTheAllEstoniaView()
    {
        var (db, viljandi, _) = await SeedAsync(viljandiLimitKm: 25);
        var lead = await AddLeadAsync(db, "Pärnu");

        var result = await ProviderCandidateFinder.SearchAsync(
            db, lead, new ProviderCandidateSearch(null, AllEstonia: true, false, RadiusKm: 25, Limit: 50));

        result.Items.Should().NotContain(c => c.SupplierId == viljandi.Id);
    }

    [Fact]
    public async Task WithoutALimit_TheWidenedRadiusStillReachesTheNextTown()
    {
        var (db, viljandi, parnu) = await SeedAsync(viljandiLimitKm: null);
        var lead = await AddLeadAsync(db, "Pärnu");

        var result = await Search(db, lead, radiusKm: 100);

        result.Items.Select(c => c.SupplierId).Should().Contain([parnu.Id, viljandi.Id]);
    }

    [Fact]
    public async Task UnlimitedProviders_AreUnaffectedByAnotherProvidersLimit()
    {
        var (db, _, parnu) = await SeedAsync(viljandiLimitKm: 25);
        var lead = await AddLeadAsync(db, "Pärnu");

        var result = await Search(db, lead, radiusKm: 100);

        result.Items.Should().Contain(c => c.SupplierId == parnu.Id);
    }

    private static Task<DTOs.Responses.ProviderCandidateResponse> Search(RuumlyDbContext db, DemandLead lead, double radiusKm) =>
        ProviderCandidateFinder.SearchAsync(
            db, lead, new ProviderCandidateSearch(null, false, false, RadiusKm: radiusKm, Limit: 50));

    private static async Task<(RuumlyDbContext Db, Supplier Viljandi, Supplier Parnu)> SeedAsync(int? viljandiLimitKm)
    {
        var db = TestDbContext.Create();

        Supplier Add(string name, string email, string city, double lat, double lng, int? maxKm)
        {
            var s = new Supplier
            {
                Id = Guid.NewGuid(), Name = name, ContactName = name, ContactEmail = email,
                ContactPhone = "+372 555 0000", IsActive = true,
                ServiceTypesJson = "[\"vanrental\"]",
                MaxServiceDistanceKm = maxKm,
            };
            db.Suppliers.Add(s);
            db.SupplierLocations.Add(new SupplierLocation
            {
                Id = Guid.NewGuid(), SupplierId = s.Id, Name = name, City = city,
                Address = "Keskus 1", IsActive = true, Lat = lat, Lng = lng,
            });
            return s;
        }

        var parnu    = Add("Pärnu Rent OÜ", "info@parnurent.ee", "Pärnu", ParnuLat, ParnuLng, null);
        var viljandi = Add("Viljandi Rent OÜ", "info@viljandirent.ee", "Viljandi", ViljandiLat, ViljandiLng, viljandiLimitKm);
        await db.SaveChangesAsync();
        return (db, viljandi, parnu);
    }

    private static async Task<DemandLead> AddLeadAsync(RuumlyDbContext db, string city)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "klient@example.ee", City = city,
            Category = DemandLeadCategory.VanRental, Language = "et",
            Source = "concierge", Status = DemandLeadStatus.New,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();
        return lead;
    }
}
