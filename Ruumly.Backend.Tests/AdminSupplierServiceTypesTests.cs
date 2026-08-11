using System.Collections;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// A partner added by hand through the admin form used to be born dead: nothing on
/// the admin supplier endpoints ever wrote Supplier.ServiceTypesJson (only the bulk
/// directory importer did), and ProviderCandidateFinder filters on it BEFORE it
/// looks at location or radius. The row had a name, an address and coordinates, it
/// looked complete in admin, and it could never be returned as a candidate for any
/// lead. Same class of failure as an email that bounces silently.
///
/// These tests pin the loop end to end — create through the admin endpoint, then
/// ask the candidate finder for a matching lead — plus the two smaller silent
/// failures found alongside it (a PATCH that 200s on fields it does not persist,
/// and a location saved with an empty city).
/// </summary>
public class AdminSupplierServiceTypesTests
{
    private const string TallinnCity = "Tallinn";
    private const double TallinnLat  = 59.437;
    private const double TallinnLng  = 24.7536;

    // ── The regression that matters ───────────────────────────────────────────

    [Fact]
    public async Task SupplierCreatedThroughAdminEndpoint_IsReturnedAsCandidate_ForMatchingLeadCategory()
    {
        var db = TestDbContext.Create();
        var suppliers = MakeSuppliers(db);

        var created = await suppliers.CreateSupplier(NewSupplier("Kolimine OÜ", ["moving"]));
        var supplierId = ReadSupplierId(created);

        // The founder's next step in admin: give the partner a place to work from.
        await AddLocationAsync(db, supplierId, TallinnCity);

        var lead = await AddLeadAsync(db, TallinnCity, DemandLeadCategory.Moving);

        var result = await MakeLeads(db).GetProviderCandidates(
            lead.Id, q: null, scope: "nearby", category: "lead", radiusKm: 25, limit: 50);

        var items = ReadItems(result.Should().BeOfType<OkObjectResult>().Subject.Value!);
        items.Select(x => Read<Guid>(x, "supplierId"))
            .Should().Contain(supplierId,
                "a partner created through the admin form with service types must be matchable");
    }

    [Fact]
    public async Task HandAddedSupplier_IsNotReturned_ForACategoryItDoesNotSell()
    {
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Kolimine OÜ", ["moving"]));
        var supplierId = ReadSupplierId(created);
        await AddLocationAsync(db, supplierId, TallinnCity);

        var lead = await AddLeadAsync(db, TallinnCity, DemandLeadCategory.Cleaning);

        var result = await MakeLeads(db).GetProviderCandidates(
            lead.Id, null, "nearby", "lead", 25, 50);

        ReadItems(result.Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Select(x => Read<Guid>(x, "supplierId"))
            .Should().NotContain(supplierId);
    }

    [Fact]
    public async Task ServiceTypesEditedThroughPatch_MakeAnExistingSupplierMatchable()
    {
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Koristus OÜ", ["cleaning"]));
        var supplierId = ReadSupplierId(created);
        await AddLocationAsync(db, supplierId, TallinnCity);

        var lead = await AddLeadAsync(db, TallinnCity, DemandLeadCategory.Warehouse);

        // Not a warehouse partner yet.
        ReadItems((await MakeLeads(db).GetProviderCandidates(lead.Id, null, "nearby", "lead", 25, 50))
                .Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Select(x => Read<Guid>(x, "supplierId")).Should().NotContain(supplierId);

        await MakeSuppliers(db).UpdateSupplier(
            supplierId, new UpdateSupplierRequest(ServiceTypes: ["cleaning", "warehouse"]));

        ReadItems((await MakeLeads(db).GetProviderCandidates(lead.Id, null, "nearby", "lead", 25, 50))
                .Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Select(x => Read<Guid>(x, "supplierId")).Should().Contain(supplierId);
    }

    // ── Create: service types are mandatory and validated ─────────────────────

    [Fact]
    public async Task CreateSupplier_PersistsNormalizedServiceTypes()
    {
        var db = TestDbContext.Create();

        var result = await MakeSuppliers(db).CreateSupplier(
            NewSupplier("Veoauto OÜ", ["  VanRental ", "moving", "moving"]));

        var supplierId = ReadSupplierId(result);
        var stored = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId);
        JsonSerializer.Deserialize<List<string>>(stored.ServiceTypesJson!)
            .Should().BeEquivalentTo(["vanrental", "moving"]);
    }

    [Theory]
    [InlineData(false)]   // serviceTypes omitted entirely
    [InlineData(true)]    // serviceTypes: []
    public async Task CreateSupplier_WithoutServiceTypes_IsRejected(bool emptyList)
    {
        var db = TestDbContext.Create();

        var result = await MakeSuppliers(db).CreateSupplier(
            NewSupplier("Nimeta OÜ", emptyList ? [] : null));

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Suppliers.CountAsync()).Should().Be(0,
            "a partner with no service types can never receive a lead, so it must not be created");
    }

    [Fact]
    public async Task CreateSupplier_WithUnknownServiceType_IsRejectedNamingIt()
    {
        var db = TestDbContext.Create();

        var result = await MakeSuppliers(db).CreateSupplier(NewSupplier("Vale OÜ", ["teleport"]));

        ErrorOf(result.Should().BeOfType<BadRequestObjectResult>().Subject)
            .Should().Contain("teleport");
    }

    [Fact]
    public async Task CreateSupplier_WithRetiredConsumerSlug_IsRejected()
    {
        var db = TestDbContext.Create();

        foreach (var retired in new[] { "packing", "insurance" })
        {
            var result = await MakeSuppliers(db).CreateSupplier(NewSupplier($"Retired {retired}", [retired]));
            ErrorOf(result.Should().BeOfType<BadRequestObjectResult>().Subject)
                .Should().Contain(retired);
        }
    }

    // ── Update: stored metadata survives, retired slugs stay un-offerable ─────

    [Fact]
    public async Task UpdateServiceTypes_PreservesStoredPackingMetadata()
    {
        // A mover that also packs was imported as ["moving","packing"]. Editing its
        // consumer services must not silently strip the packing tag — it is supplier
        // metadata we keep, and the admin never gets offered it to re-send.
        var db = TestDbContext.Create();
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Pakkijad OÜ", ContactName = "Pakkijad OÜ",
            ContactEmail = "info@pakkijad.ee", ContactPhone = "+372 555 0100", IsActive = true,
            ServiceTypesJson = """["moving","packing"]""",
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var result = await MakeSuppliers(db).UpdateSupplier(
            supplier.Id, new UpdateSupplierRequest(ServiceTypes: ["moving", "trailer"]));

        result.Should().BeOfType<OkObjectResult>();
        var stored = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplier.Id);
        JsonSerializer.Deserialize<List<string>>(stored.ServiceTypesJson!)
            .Should().BeEquivalentTo(["moving", "trailer", "packing"]);
    }

    [Fact]
    public async Task UpdateServiceTypes_ToEmptyList_IsRejected()
    {
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Ladu OÜ", ["warehouse"]));
        var supplierId = ReadSupplierId(created);

        var result = await MakeSuppliers(db).UpdateSupplier(
            supplierId, new UpdateSupplierRequest(ServiceTypes: []));

        result.Should().BeOfType<BadRequestObjectResult>();
        var stored = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId);
        JsonSerializer.Deserialize<List<string>>(stored.ServiceTypesJson!)
            .Should().BeEquivalentTo(["warehouse"]);
    }

    // ── The PATCH that used to 200 on fields it never wrote ───────────────────

    [Fact]
    public async Task UpdateSupplier_WithIsActive_IsRejectedAndPointsAtTheStatusEndpoint()
    {
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Ladu OÜ", ["warehouse"]));
        var supplierId = ReadSupplierId(created);

        var result = await MakeSuppliers(db).UpdateSupplier(
            supplierId, new UpdateSupplierRequest(IsActive: false));

        var error = ErrorOf(result.Should().BeOfType<BadRequestObjectResult>().Subject);
        error.Should().Contain("isActive").And.Contain("status");
        (await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId))
            .IsActive.Should().BeTrue("the plain PATCH must not change visibility either way");
    }

    [Fact]
    public async Task StatusEndpoint_StillFlipsIsActive()
    {
        // Guard: hardening the plain PATCH must not touch the endpoint that owns this.
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Ladu OÜ", ["warehouse"]));
        var supplierId = ReadSupplierId(created);

        var result = await MakeSuppliers(db).UpdateSupplierStatus(
            supplierId, new UpdateSupplierStatusRequest(false));

        result.Should().BeOfType<OkObjectResult>();
        (await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId))
            .IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSupplier_PersistsCountry_WhichUsedToBeSwallowed()
    {
        // The admin profile form has always sent `country` — it bound to nothing.
        // Country picks the provider-outreach email language.
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Rīga SIA", ["moving"]));
        var supplierId = ReadSupplierId(created);

        var result = await MakeSuppliers(db).UpdateSupplier(
            supplierId, new UpdateSupplierRequest(Country: "lv"));

        result.Should().BeOfType<OkObjectResult>();
        (await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId))
            .Country.Should().Be("LV");
    }

    [Fact]
    public async Task UpdateSupplier_WithUnsupportedCountry_IsRejected()
    {
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Ladu OÜ", ["warehouse"]));
        var supplierId = ReadSupplierId(created);

        var result = await MakeSuppliers(db).UpdateSupplier(
            supplierId, new UpdateSupplierRequest(Country: "DE"));

        ErrorOf(result.Should().BeOfType<BadRequestObjectResult>().Subject).Should().Contain("DE");
    }

    [Fact]
    public void UpdateSupplierRequest_RefusesUnmappedJsonMembers_AndTheRejectionNamesTheField()
    {
        // Model binding rejects the payload before the action ever runs, so this
        // pins the contract at the deserializer: a field the endpoint cannot
        // persist must not be answered with a 200.
        var deserialize = () => JsonSerializer.Deserialize<UpdateSupplierRequest>(
            """{"name":"Ladu OÜ","promotedUntil":"2026-09-01"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var thrown = deserialize.Should().Throw<JsonException>().Which;

        // The JSON input formatter keys ModelState by JsonException.Path, and
        // ValidationProblemFactory turns that key into the client-facing field name.
        // Assert the whole chain so the 400 genuinely names the offending field
        // instead of only saying "invalid" (the message text itself is sanitized
        // on purpose — it would otherwise leak the .NET type name).
        thrown.Path.Should().Be("$.promotedUntil");
        FieldNameFor(thrown.Path!).Should().Be("promotedUntil");
    }

    /// <summary>Mirrors ValidationProblemFactory.CleanKey, which is private.</summary>
    private static string FieldNameFor(string modelStateKey) =>
        modelStateKey.StartsWith("$.", StringComparison.Ordinal)
            ? modelStateKey[2..]
            : modelStateKey;

    [Fact]
    public void UpdateSupplierRequest_StillBindsEveryFieldTheAdminFormSends()
    {
        // Counterpart to the test above: Disallow is only safe if the real payloads
        // still bind. These are the exact keys the admin profile/commercial/partner-page
        // tabs POST today.
        var deserialize = () => JsonSerializer.Deserialize<UpdateSupplierRequest>(
            """
            {"name":"Ladu OÜ","contactName":"Mari","contactEmail":"mari@ladu.ee",
             "contactPhone":"+372 555 0100","country":"EE","registryCode":"12345678",
             "notes":"","tier":"standard","billingModel":"marketplace",
             "partnerDiscountRate":10,"clientDiscountRate":5,"iban":"EE00","bankAccountName":"Ladu",
             "bankName":"Swed","integrationType":"manual","slug":"ladu","isPartnerPagePublished":true,
             "logoUrl":"","heroImageUrl":"","tagline":"","foundedYear":2019,"isVerified":false,
             "foundingPartner":false,"googlePlaceId":"","longDescriptionEt":"","longDescriptionEn":"",
             "longDescriptionRu":"","serviceTypes":["warehouse"]}
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        deserialize.Should().NotThrow();
    }

    // ── A location saved with an empty city ───────────────────────────────────

    [Fact]
    public async Task CreateLocation_WithBlankCity_IsRejected()
    {
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Ladu OÜ", ["warehouse"]));
        var supplierId = ReadSupplierId(created);

        var result = await MakeLocations(db).Create(new CreateLocationRequest(
            SupplierId: supplierId, Name: "Ladu", Address: "Tehnika 1", City: "   ",
            Lat: TallinnLat, Lng: TallinnLng, Notes: null, Images: null,
            Description: null, OpeningHours: null));

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.SupplierLocations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PatchLocation_ClearingCity_IsRejected()
    {
        var db = TestDbContext.Create();
        var created = await MakeSuppliers(db).CreateSupplier(NewSupplier("Ladu OÜ", ["warehouse"]));
        var supplierId = ReadSupplierId(created);
        var location = await AddLocationAsync(db, supplierId, TallinnCity);

        var result = await MakeLocations(db).Patch(location.Id, new PatchLocationRequest(
            Name: null, Address: null, City: "", Lat: null, Lng: null, Notes: null,
            Images: null, Description: null, OpeningHours: null, ExternalId: null));

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.SupplierLocations.AsNoTracking().FirstAsync(l => l.Id == location.Id))
            .City.Should().Be(TallinnCity);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static CreateSupplierRequest NewSupplier(string name, List<string>? serviceTypes) =>
        new(Name: name, BillingModel: null, RegistryCode: null, ContactName: name,
            ContactEmail: "info@example.ee", ContactPhone: "+372 555 0100",
            IntegrationType: null, RecipientEmail: null, ApiEndpoint: null,
            ApiAuthType: null, ApiAuthToken: null, PartnerDiscountRate: null,
            ClientDiscountRate: null, Notes: null, Iban: null, BankAccountName: null,
            BankName: null, BookingEnabled: null, ContractSigningEnabled: null,
            DirectPaymentEnabled: null, RuumlyPaymentEnabled: null,
            ServiceTypes: serviceTypes);

    private static async Task<SupplierLocation> AddLocationAsync(
        RuumlyDbContext db, Guid supplierId, string city)
    {
        var location = new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = supplierId, Name = "Ladu",
            City = city, Address = "Tehnika 1", IsActive = true,
            Lat = TallinnLat, Lng = TallinnLng,
        };
        db.SupplierLocations.Add(location);
        await db.SaveChangesAsync();
        return location;
    }

    private static async Task<DemandLead> AddLeadAsync(
        RuumlyDbContext db, string city, DemandLeadCategory category)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "customer@example.ee", City = city,
            Category = category, Language = "et",
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();
        return lead;
    }

    private sealed class NoopEmailQueue : IBackgroundEmailQueue
    {
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null) { }
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo) { }
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static AdminSuppliersController MakeSuppliers(RuumlyDbContext db)
    {
        IDistributedCache cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));

        // Only Db / pricing / cache are on the create+update paths; the rest of the
        // controller's dependencies are never touched by these tests.
        return WithAdmin(new AdminSuppliersController(
            db,
            httpClientFactory: null!,
            NullLogger<AdminSuppliersController>.Instance,
            tokenProtector: null!,
            new PricingConfigService(db, cache),
            pollingService: null!,
            cache,
            new NoopEmailQueue()));
    }

    private static AdminLeadsController MakeLeads(RuumlyDbContext db) => WithAdmin(new AdminLeadsController(db));

    private static LocationsController MakeLocations(RuumlyDbContext db) => WithAdmin(new LocationsController(db));

    private static T WithAdmin<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Email, "admin@ruumly.eu"),
                    new Claim(ClaimTypes.Role, "Admin"),
                ], "test")),
            },
        };
        return controller;
    }

    private static Guid ReadSupplierId(IActionResult result) =>
        Read<Guid>(result.Should().BeOfType<OkObjectResult>().Subject.Value!, "id");

    private static string ErrorOf(BadRequestObjectResult result) =>
        Read<string>(result.Value!, "error");

    private static List<object> ReadItems(object body) =>
        ((IEnumerable)Read<object>(body, "items")).Cast<object>().ToList();

    private static T Read<T>(object item, string name) =>
        (T)item.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.IgnoreCase)!
            .GetValue(item)!;
}
