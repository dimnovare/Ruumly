using FluentAssertions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// WHERE the automatic fan-out looks for providers (founder decision, 2026-08-14).
///
/// Two changes this pins down:
///
///  1. A move has TWO relevant places. Only the origin was ever searched, so a
///     Tallinn → Tartu request reached no Tartu mover at all — often the cheaper
///     half of that market, and the half most motivated to take a job ending at
///     its own door. The destination was on the lead the whole time; the search
///     dropped it.
///
///  2. One 25/50/100 km ladder cannot serve the whole catalogue. Storage is a
///     fixed place the CUSTOMER drives to (nobody drives 100 km to their own
///     boxes); movers, vans and trailers TRAVEL to the customer; cleaning crews
///     work a metro area. Each now has its own ladder.
/// </summary>
public class AutoFanOutGeographyTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody, string? HtmlBody, string? ReplyTo)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody, htmlBody, null));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody, htmlBody, replyTo));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    // Real coordinates: the distance between them is what the ladders are about.
    // Tallinn → Tartu is ~163 km, well outside every ladder, so a provider in one
    // can only be reached by anchoring the search on its own city.
    private const double TallinnLat = 59.4370, TallinnLng = 24.7536;
    private const double TartuLat   = 58.3780, TartuLng   = 26.7290;
    // ~35 km east of Tallinn (Kehra): inside the travel ladder, outside storage's
    // 40 km ceiling only once anchor drift is accounted for — see RaplaLat for the
    // unambiguous case.
    private const double NearTallinnLat = 59.3300, NearTallinnLng = 25.3300;
    // Rapla, ~50 km south of Tallinn. This is the coordinate that DISCRIMINATES:
    // the old single ladder reached it at its 50 km step, the storage ladder's
    // 40 km ceiling does not. A test using Tartu instead would pass under both
    // the old and the new code and prove nothing.
    private const double RaplaLat = 58.9953, RaplaLng = 24.7928;

    private static Supplier SeedProvider(
        RuumlyDbContext db, string name, string email, string city,
        double lat, double lng, ListingType type)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C",
            ContactEmail = email, ContactPhone = "1", IsActive = true, Country = "EE",
        };
        db.Suppliers.Add(supplier);
        db.Listings.Add(new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Supplier = supplier,
            Type = type, Title = $"{type} — {name}", City = city,
            Lat = lat, Lng = lng, IsActive = true,
            PriceFrom = 50m, PriceUnit = "onetime", UpdatedAt = DateTime.UtcNow,
        });
        return supplier;
    }

    private static DemandLead SeedLead(
        RuumlyDbContext db, DemandLeadCategory category, string city, string? toCity = null)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", City = city, ToCity = toCity,
            Category = category, Language = "et", Source = "concierge",
            Status = DemandLeadStatus.New, CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        return lead;
    }

    private static async Task<List<string>> FanOutRecipientsAsync(
        RuumlyDbContext db, DemandLead lead)
    {
        var queue = new CapturingEmailQueue();
        await TestServices.Outreach(db, queue).AutoFanOutAsync(lead);
        return queue.Emails.Select(e => e.To).ToList();
    }

    // ─── Destination-city matching for moves ──────────────────────────────────

    [Fact]
    public async Task MovingLead_ReachesProvidersAtBothEndsOfTheRoute()
    {
        var db = TestDbContext.Create();
        SeedProvider(db, "Tallinn Movers", "origin@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Moving);
        SeedProvider(db, "Tartu Movers", "destination@x.ee", "Tartu",
            TartuLat, TartuLng, ListingType.Moving);
        var lead = SeedLead(db, DemandLeadCategory.Moving, "Tallinn", toCity: "Tartu");
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        recipients.Should().Contain("origin@x.ee");
        recipients.Should().Contain("destination@x.ee",
            "a mover at the destination is a real candidate for the job and was never contacted before");
    }

    [Fact]
    public async Task MovingLead_WithoutADestination_SearchesOnlyTheOrigin()
    {
        var db = TestDbContext.Create();
        SeedProvider(db, "Tallinn Movers", "origin@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Moving);
        SeedProvider(db, "Tartu Movers", "far@x.ee", "Tartu",
            TartuLat, TartuLng, ListingType.Moving);
        var lead = SeedLead(db, DemandLeadCategory.Moving, "Tallinn");
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        recipients.Should().Contain("origin@x.ee");
        recipients.Should().NotContain("far@x.ee", "163 km is outside every ladder");
    }

    [Fact]
    public async Task NonMovingLead_IgnoresTheDestination()
    {
        var db = TestDbContext.Create();
        SeedProvider(db, "Tartu Storage", "destination@x.ee", "Tartu",
            TartuLat, TartuLng, ListingType.Warehouse);
        // A customer moving to Tartu still stores (and collects) at the origin —
        // searching the destination would cold-email a business that cannot serve
        // the request at all.
        var lead = SeedLead(db, DemandLeadCategory.Warehouse, "Tallinn", toCity: "Tartu");
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        recipients.Should().NotContain("destination@x.ee");
    }

    [Fact]
    public async Task MovingLead_ToTheSameCity_DoesNotDoubleCountThatCity()
    {
        var db = TestDbContext.Create();
        SeedProvider(db, "Tallinn Movers", "one@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Moving);
        // Case-insensitive: "Tallinn → tallinn" is one place, and searching it
        // twice would let it take both halves of a shared quota.
        var lead = SeedLead(db, DemandLeadCategory.Moving, "Tallinn", toCity: "tallinn");
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        recipients.Should().ContainSingle().And.Contain("one@x.ee");
    }

    [Fact]
    public async Task AProviderServingBothEndsIsEmailedOnce()
    {
        var db = TestDbContext.Create();
        var supplier = SeedProvider(db, "Nationwide Movers", "both@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Moving);
        // Same company, a branch listing at the destination.
        db.Listings.Add(new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Supplier = supplier,
            Type = ListingType.Moving, Title = "Moving — Tartu branch", City = "Tartu",
            Lat = TartuLat, Lng = TartuLng, IsActive = true,
            PriceFrom = 50m, PriceUnit = "onetime", UpdatedAt = DateTime.UtcNow,
        });
        var lead = SeedLead(db, DemandLeadCategory.Moving, "Tallinn", toCity: "Tartu");
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        recipients.Should().ContainSingle(
            "matching at both ends must not cost one company two cold contacts");
    }

    // ─── Per-service radius ladders ───────────────────────────────────────────

    /// <summary>
    /// The pair of tests below is the whole point of per-service ladders, and
    /// they are deliberately built on the SAME distance (~50 km, Tallinn →
    /// Rapla). One provider is reached, the other is not, and the only thing that
    /// differs is the service. Under the old shared 25/50/100 ladder both were
    /// contacted, so both tests are real regression guards rather than
    /// assertions that happen to hold.
    /// </summary>
    [Fact]
    public async Task Storage_50kmAway_IsNoLongerContacted()
    {
        var db = TestDbContext.Create();
        // Anchor the search on Tallinn by giving the city a provider of its own.
        SeedProvider(db, "Tallinn Storage", "near-storage@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Warehouse);
        SeedProvider(db, "Rapla Storage", "far-storage@x.ee", "Rapla",
            RaplaLat, RaplaLng, ListingType.Warehouse);
        var lead = SeedLead(db, DemandLeadCategory.Warehouse, "Tallinn");
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        recipients.Should().Contain("near-storage@x.ee");
        recipients.Should().NotContain("far-storage@x.ee",
            "nobody drives 50 km to visit their own boxes — the storage ladder tops out at 40 km");
    }

    [Fact]
    public async Task AMover_TheSame50kmAway_IsStillContacted()
    {
        var db = TestDbContext.Create();
        SeedProvider(db, "Tallinn Movers", "anchor@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Moving);
        SeedProvider(db, "Rapla Movers", "travelling@x.ee", "Rapla",
            RaplaLat, RaplaLng, ListingType.Moving);
        var lead = SeedLead(db, DemandLeadCategory.Moving, "Tallinn");
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        recipients.Should().Contain("travelling@x.ee",
            "movers travel to the customer, so the wide ladder is right for them");
    }

    [Fact]
    public async Task EachServiceInAMultiServiceRequestUsesItsOwnLadder()
    {
        var db = TestDbContext.Create();
        SeedProvider(db, "Tallinn Movers", "mover@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Moving);
        SeedProvider(db, "Tallinn Storage", "storage@x.ee", "Tallinn",
            TallinnLat, TallinnLng, ListingType.Warehouse);
        SeedProvider(db, "Rapla Movers", "far-mover@x.ee", "Rapla",
            RaplaLat, RaplaLng, ListingType.Moving);
        SeedProvider(db, "Rapla Storage", "far-storage@x.ee", "Rapla",
            RaplaLat, RaplaLng, ListingType.Warehouse);

        var lead = SeedLead(db, DemandLeadCategory.Any, "Tallinn");
        lead.Query = "concierge: moving+warehouse | Tallinn";
        await db.SaveChangesAsync();

        var recipients = await FanOutRecipientsAsync(db, lead);

        // Both services are represented — the shared quota is round-robined, so
        // neither half of the customer's move goes unquoted.
        recipients.Should().Contain("mover@x.ee");
        recipients.Should().Contain("storage@x.ee");
        // And the distant MOVER is reachable while the distant STORAGE is not,
        // in one and the same request — the ladders are per service, not per lead.
        recipients.Should().Contain("far-mover@x.ee");
        recipients.Should().NotContain("far-storage@x.ee");
    }
}
