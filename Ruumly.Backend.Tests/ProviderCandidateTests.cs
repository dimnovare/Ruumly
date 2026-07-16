using System.Collections;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

public class ProviderCandidateTests
{
    [Fact]
    public async Task Nearby_ReturnsSevenUniqueSuppliers_OrderedByDistance()
    {
        var (db, lead, expectedIds) = await CandidateFixture.CreateTartuAsync();
        var controller = MakeAdminLeads(db);

        var result = await controller.GetProviderCandidates(
            lead.Id, q: null, scope: "nearby", category: "lead", radiusKm: 25, limit: 50);

        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var items = ReadItems(body);
        items.Select(x => Read<Guid>(x, "supplierId")).Should().Equal(expectedIds);
        items.Select(x => Read<Guid>(x, "supplierId")).Should().OnlyHaveUniqueItems();
        items.Select(x => Read<double?>(x, "distanceKm")).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Search_AllEstonia_FindsNameCityAddressEmailAndPhone()
    {
        var (db, lead, _) = await CandidateFixture.CreateTartuAsync();
        var controller = MakeAdminLeads(db);

        foreach (var q in new[] { "Panicom", "Tõrvandi", "Ringtee", "sales@", "+372" })
        {
            var result = await controller.GetProviderCandidates(lead.Id, q, "all", "lead", 25, 50);
            ReadItems(result.Should().BeOfType<OkObjectResult>().Subject.Value!)
                .Should().NotBeEmpty(q);
        }
    }

    [Fact]
    public async Task AnyCategory_IsRejectedForNearby_AndAllowedForAll()
    {
        var (db, lead, _) = await CandidateFixture.CreateTartuAsync();
        var controller = MakeAdminLeads(db);

        (await controller.GetProviderCandidates(lead.Id, null, "nearby", "any", 25, 50))
            .Should().BeOfType<BadRequestObjectResult>();
        (await controller.GetProviderCandidates(lead.Id, null, "all", "any", 25, 50))
            .Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task MissingCoordinates_FallsBackToExactCityThenName()
    {
        var (db, lead) = await CandidateFixture.CreateWithoutCoordinatesAsync();
        var result = await MakeAdminLeads(db).GetProviderCandidates(
            lead.Id, null, "nearby", "lead", 25, 50);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        Read<object?>(body, "anchor").Should().BeNull();
        ReadItems(body).Select(x => Read<string>(x, "city")).First().Should().Be("Tartu");
    }

    [Fact]
    public async Task NearbyWithAnchor_KeepsExactCitySupplierWithoutCoordinates()
    {
        var (db, lead, noCoordId, coordExactCityIds) =
            await CandidateFixture.CreateTartuWithNoCoordExactCityAsync();

        var result = await MakeAdminLeads(db).GetProviderCandidates(
            lead.Id, null, "nearby", "lead", 25, 50);

        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        // Anchor is derivable from the coordinated exact-city suppliers.
        Read<object?>(body, "anchor").Should().NotBeNull();

        var items = ReadItems(body);
        var noCoord = items.SingleOrDefault(x => Read<Guid>(x, "supplierId") == noCoordId);
        noCoord.Should().NotBeNull(
            "an exact-city supplier without coordinates must not be dropped from the nearby view");
        Read<bool>(noCoord!, "isExactCity").Should().BeTrue();
        Read<double?>(noCoord!, "distanceKm").Should().BeNull();

        // Coordinated exact-city suppliers still sort ahead of the no-coordinate one.
        var ids = items.Select(x => Read<Guid>(x, "supplierId")).ToList();
        ids.Should().Contain(coordExactCityIds);
        var noCoordIndex = ids.IndexOf(noCoordId);
        foreach (var coordId in coordExactCityIds)
        {
            ids.IndexOf(coordId).Should().BeLessThan(noCoordIndex);
        }
    }

    private static AdminLeadsController MakeAdminLeads(RuumlyDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, "Admin"),
                    ], "test")),
                },
            },
        };

    private static List<object> ReadItems(object body) =>
        ((IEnumerable)Read<object>(body, "items")).Cast<object>().ToList();

    private static T Read<T>(object item, string name) =>
        (T)item.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)!
            .GetValue(item)!;

    private sealed class CandidateFixture
    {
        private const double TartuLat = 58.377;
        private const double TartuLng = 26.729;
        private const double LatitudeDegreesPerKm = 1d / 111.195d;

        public static async Task<(RuumlyDbContext Db, DemandLead Lead, IReadOnlyList<Guid> ExpectedIds)> CreateTartuAsync()
        {
            var db = TestDbContext.Create();
            var lead = new DemandLead
            {
                Id = Guid.NewGuid(), Email = "customer@example.ee", City = "Tartu",
                Category = DemandLeadCategory.Warehouse, Language = "et",
            };

            var tartu06 = AddSupplier(db, "Panicom Storage", "Tartu", 0.6, "Puiestee 1", "sales@panicom.ee", "+372 555 0001", 3, 2.1333333333);
            var tartu12 = AddSupplier(db, "Tartu Lockers", "Tartu", -1.2, "Raatuse 2", "info@lockers.ee", "+372 555 0002");
            var tartu15 = AddSupplier(db, "Emajõe Storage", "Tartu", 1.5, "Vabaduse 3", "info@emajoe.ee", "+372 555 0003");
            var tartu38 = AddSupplier(db, "Lõuna Laod", "Tartu", -3.8, "Riia 4", "info@louna.ee", "+372 555 0004");
            var vahi46 = AddSupplier(db, "Vahi Varahoid", "Vahi", 4.6, "Vahi tee 5", "info@vahi.ee", "+372 555 0005");
            var torvandi69 = AddSupplier(db, "Tõrvandi Keskus", "Tõrvandi", -6.9, "Ringtee 6", "info@torvandi.ee", "+372 555 0006");
            var reola84 = AddSupplier(db, "Reola Storage", "Reola", 8.4, "Reola 7", "info@reola.ee", "+372 555 0007");

            db.DemandLeads.Add(lead);
            await db.SaveChangesAsync();

            return (db, lead,
            [tartu06.Id, tartu12.Id, tartu15.Id, tartu38.Id, vahi46.Id, torvandi69.Id, reola84.Id]);
        }

        public static async Task<(RuumlyDbContext Db, DemandLead Lead)> CreateWithoutCoordinatesAsync()
        {
            var db = TestDbContext.Create();
            var lead = new DemandLead
            {
                Id = Guid.NewGuid(), Email = "customer@example.ee", City = "Tartu",
                Category = DemandLeadCategory.Warehouse, Language = "et",
            };

            AddSupplier(db, "Zulu Vahi", "Vahi", 0, "Vahi tee 1", "info@vahi.ee", "+372 555 0010", missingCoordinates: true);
            AddSupplier(db, "Alpha Tartu", "Tartu", 0, "Riia 1", "info@tartu.ee", "+372 555 0011", missingCoordinates: true);
            db.DemandLeads.Add(lead);
            await db.SaveChangesAsync();

            return (db, lead);
        }

        public static async Task<(RuumlyDbContext Db, DemandLead Lead, Guid NoCoordId, IReadOnlyList<Guid> CoordExactCityIds)> CreateTartuWithNoCoordExactCityAsync()
        {
            var db = TestDbContext.Create();
            var lead = new DemandLead
            {
                Id = Guid.NewGuid(), Email = "customer@example.ee", City = "Tartu",
                Category = DemandLeadCategory.Warehouse, Language = "et",
            };

            // Coordinated exact-city suppliers geocode the Tartu anchor.
            var tartu06 = AddSupplier(db, "Panicom Storage", "Tartu", 0.6, "Puiestee 1", "sales@panicom.ee", "+372 555 0001", 3, 2.1333333333);
            var tartu15 = AddSupplier(db, "Emajõe Storage", "Tartu", 1.5, "Vabaduse 3", "info@emajoe.ee", "+372 555 0003");
            // Exact-city (Tartu) supplier with no valid coordinates (0,0): DistanceKm stays null.
            var tartuNoCoord = AddSupplier(db, "Kesklinn Ladu", "Tartu", 0, "Rüütli 8", "info@kesklinn.ee", "+372 555 0008", missingCoordinates: true);

            db.DemandLeads.Add(lead);
            await db.SaveChangesAsync();

            return (db, lead, tartuNoCoord.Id, [tartu06.Id, tartu15.Id]);
        }

        private static Supplier AddSupplier(
            RuumlyDbContext db,
            string name,
            string city,
            double latitudeOffsetKm,
            string address,
            string email,
            string phone,
            int listingCount = 1,
            double? listingLatitudeOffsetKm = null,
            bool missingCoordinates = false)
        {
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(), Name = name, ContactName = name,
                ContactEmail = email, ContactPhone = phone, IsActive = true,
            };
            var location = new SupplierLocation
            {
                Id = Guid.NewGuid(), SupplierId = supplier.Id, Name = $"{name} location",
                City = city, Address = address, IsActive = true,
                Lat = missingCoordinates ? 0 : TartuLat + latitudeOffsetKm * LatitudeDegreesPerKm,
                Lng = missingCoordinates ? 0 : TartuLng,
            };
            db.Suppliers.Add(supplier);
            db.SupplierLocations.Add(location);

            for (var index = 0; index < listingCount; index++)
            {
                var listingOffset = listingLatitudeOffsetKm ?? latitudeOffsetKm;
                db.Listings.Add(new Listing
                {
                    Id = Guid.NewGuid(), SupplierId = supplier.Id, Type = ListingType.Warehouse,
                    Title = $"{name} unit {index + 1}", City = city, Address = address,
                    Lat = missingCoordinates ? 0 : TartuLat + listingOffset * LatitudeDegreesPerKm,
                    Lng = missingCoordinates ? 0 : TartuLng,
                    PriceFrom = 50m, PriceUnit = "month", IsActive = true,
                });
            }

            return supplier;
        }
    }
}
