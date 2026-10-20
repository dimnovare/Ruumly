using FluentAssertions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The geographic anchor is WHERE the town is, and must not depend on which
/// service the customer asked for.
///
/// Reproduces Valga as it was on 2026-10-19: a moving company in town, no van
/// rental in town, van rentals in Võru ~56 km away. The anchor used to be built
/// from same-service providers only, so it came out null, and a null anchor
/// keeps nothing at any radius — the request reached no-one while five Võru
/// rentals sat inside the 100 km ring.
/// </summary>
public class ProviderCandidateAnchorTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<string> Recipients { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null) => Recipients.Add(to);
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo) => Recipients.Add(to);
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private const double ValgaLat = 57.7769, ValgaLng = 26.0474;
    private const double VoruLat  = 57.8339, VoruLng  = 26.9928; // ~56 km east

    [Fact]
    public async Task TownWithNoProviderOfTheService_StillAnchors_AndFindsTheNextTown()
    {
        var (db, lead, voru, valgaMover) = await ValgaAsync();

        var result = await ProviderCandidateFinder.SearchAsync(
            db, lead, new ProviderCandidateSearch(null, false, false, RadiusKm: 100, Limit: 50));

        result.Anchor.Should().NotBeNull("Valga has a provider in town, of a different service");
        result.Items.Should().ContainSingle(c => c.SupplierId == voru.Id)
            .Which.DistanceKm.Should().BeInRange(50, 62);
    }

    [Fact]
    public async Task TheInTownProviderOfAnotherService_IsNeverACandidate()
    {
        // It anchors the town. It must not be ASKED: a moving company is not a
        // van rental, and widening where we look from must not widen who we ask.
        var (db, lead, _, valgaMover) = await ValgaAsync();

        var result = await ProviderCandidateFinder.SearchAsync(
            db, lead, new ProviderCandidateSearch(null, false, false, RadiusKm: 100, Limit: 50));

        result.Items.Should().NotContain(c => c.SupplierId == valgaMover.Id);
    }

    [Fact]
    public async Task TheRadiusStillHolds()
    {
        var (db, lead, voru, _) = await ValgaAsync();

        var result = await ProviderCandidateFinder.SearchAsync(
            db, lead, new ProviderCandidateSearch(null, false, false, RadiusKm: 25, Limit: 50));

        result.Items.Should().NotContain(c => c.SupplierId == voru.Id, "Võru is ~56 km, outside 25");
    }

    [Fact]
    public async Task AutoFanOut_ReachesTheNextTown_InsteadOfNobody()
    {
        var (db, lead, voru, _) = await ValgaAsync();
        var queue = new CapturingEmailQueue();

        await TestServices.Outreach(db, queue).AutoFanOutAsync(lead);

        queue.Recipients.Should().Equal(voru.ContactEmail);
    }

    private static async Task<(RuumlyDbContext Db, DemandLead Lead, Supplier Voru, Supplier ValgaMover)> ValgaAsync()
    {
        var db = TestDbContext.Create();

        Supplier Add(string name, string email, string city, double lat, double lng, string service)
        {
            var s = new Supplier
            {
                Id = Guid.NewGuid(), Name = name, ContactName = name, ContactEmail = email,
                ContactPhone = "+372 555 0000", IsActive = true,
                ServiceTypesJson = $"[\"{service}\"]",
            };
            db.Suppliers.Add(s);
            db.SupplierLocations.Add(new SupplierLocation
            {
                Id = Guid.NewGuid(), SupplierId = s.Id, Name = name, City = city,
                Address = "Keskus 1", IsActive = true, Lat = lat, Lng = lng,
            });
            return s;
        }

        var valgaMover = Add("Valga Kolimine OÜ", "info@valgakolimine.ee", "Valga", ValgaLat, ValgaLng, "moving");
        var voru       = Add("Võru Rent OÜ", "info@vorurent.ee", "Võru", VoruLat, VoruLng, "vanrental");

        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "klient@example.ee", City = "Valga",
            Category = DemandLeadCategory.VanRental, Language = "et",
            Source = "concierge", Status = DemandLeadStatus.New,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();
        return (db, lead, voru, valgaMover);
    }
}
