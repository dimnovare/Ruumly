using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Releasing a provider-seeded offer to the customer without an admin reading
/// it first. Every test here is a brake, not an accelerator: this is the only
/// code path that emails a real customer with nobody in the loop, and it ships
/// switched OFF.
/// </summary>
public class OfferAutoSendTests
{
    private sealed class CapturingQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject)> Emails { get; } = [];
        public void EnqueueEmail(string to, string s, string t, string? h = null) => Emails.Add((to, s));
        public void EnqueueEmail(string to, string s, string t, string? h, string? r) => Emails.Add((to, s));
        public void EnqueueEmailAfter(TimeSpan d, string to, string s, string t, string? h, string? r)
            => Emails.Add((to, s));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static OfferAutoSendService Make(RuumlyDbContext db, CapturingQueue queue) =>
        new(db, queue,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://ruumly.eu" })
                .Build(),
            TestServices.OutcomeNotifier(db, queue),
            NullLogger<OfferAutoSendService>.Instance);

    private static async Task<(RuumlyDbContext Db, Offer Offer)> SeedAsync(
        int quotedOptions = 1,
        bool? enabled = true,
        int? minQuotes = null,
        OfferStatus status = OfferStatus.Draft,
        DemandLeadStatus leadStatus = DemandLeadStatus.Contacted,
        string leadEmail = "customer@example.ee",
        DateTime? offerCreatedAt = null)
    {
        var db = TestDbContext.Create();
        if (enabled is not null)
            db.PlatformSettings.Add(new PlatformSetting
            {
                Key = OfferAutoSendService.EnabledSetting, Value = enabled.Value ? "true" : "false",
            });
        if (minQuotes is not null)
            db.PlatformSettings.Add(new PlatformSetting
            {
                Key = OfferAutoSendService.MinQuotesSetting, Value = minQuotes.Value.ToString(),
            });

        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = leadEmail, City = "Tartu", Language = "et",
            Category = DemandLeadCategory.Warehouse, Status = leadStatus,
        };
        var offer = new Offer
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, Token = "tok-" + Guid.NewGuid(),
            Status = status, Language = "et", CreatedBy = "provider-quote",
            CreatedAt = offerCreatedAt ?? DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        db.Offers.Add(offer);
        for (var i = 0; i < quotedOptions; i++)
            db.OfferOptions.Add(new OfferOption
            {
                Id = Guid.NewGuid(), OfferId = offer.Id, Title = $"Provider {i}",
                PriceAmount = 60 + i, PriceUnit = "/ööpäev", SortOrder = i,
                CreatedFromOutreachId = Guid.NewGuid(),
            });
        await db.SaveChangesAsync();
        return (db, offer);
    }

    [Fact]
    public async Task IsOffUnlessTheSettingSaysOtherwise()
    {
        // No setting row at all — a fresh database must not start emailing
        // customers because a feature was merged.
        var (db, offer) = await SeedAsync(quotedOptions: 5, enabled: null);
        var queue = new CapturingQueue();

        var result = await Make(db, queue).TrySendAsync(offer.Id);

        result.WasSent.Should().BeFalse();
        result.Reason.Should().Be("disabled");
        queue.Emails.Should().BeEmpty();
        db.Offers.Single().Status.Should().Be(OfferStatus.Draft);
    }

    [Fact]
    public async Task ExplicitlyDisabledSendsNothing()
    {
        var (db, offer) = await SeedAsync(quotedOptions: 5, enabled: false);
        var queue = new CapturingQueue();

        (await Make(db, queue).TrySendAsync(offer.Id)).Reason.Should().Be("disabled");
        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task WaitsForASecondQuoteBeforeSending()
    {
        // The customer was promised "2–3 offers". Sending the first price that
        // lands is what makes that promise a lie.
        var (db, offer) = await SeedAsync(quotedOptions: 1, minQuotes: 2);
        var queue = new CapturingQueue();

        var result = await Make(db, queue).TrySendAsync(offer.Id);

        result.WasSent.Should().BeFalse();
        result.Reason.Should().Be("waiting_for_more");
        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task SendsOnceEnoughQuotesHaveArrived()
    {
        var (db, offer) = await SeedAsync(quotedOptions: 2, minQuotes: 2);
        var queue = new CapturingQueue();

        var result = await Make(db, queue).TrySendAsync(offer.Id);

        result.WasSent.Should().BeTrue();
        result.Options.Should().Be(2);
        queue.Emails.Should().ContainSingle().Which.To.Should().Be("customer@example.ee");

        var sent = db.Offers.Single();
        sent.Status.Should().Be(OfferStatus.Sent);
        sent.SentAt.Should().NotBeNull();
        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Quoted);
    }

    [Fact]
    public async Task SendsASingleQuoteOnceTheWaitHasElapsed()
    {
        // Most requests never collect a second quote. A good single offer an
        // hour later beats a perfect pair that never arrives.
        var (db, offer) = await SeedAsync(
            quotedOptions: 1, minQuotes: 2,
            offerCreatedAt: DateTime.UtcNow.AddHours(-4));
        var queue = new CapturingQueue();

        var result = await Make(db, queue).TrySendAsync(offer.Id);

        result.WasSent.Should().BeTrue();
        result.Options.Should().Be(1);
        queue.Emails.Should().ContainSingle();
    }

    [Fact]
    public async Task NeverTouchesAnOfferAnAdminAlreadySent()
    {
        var (db, offer) = await SeedAsync(quotedOptions: 3, status: OfferStatus.Sent);
        var queue = new CapturingQueue();

        (await Make(db, queue).TrySendAsync(offer.Id)).Reason.Should().Be("not_draft");
        queue.Emails.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DemandLeadStatus.Converted)]
    [InlineData(DemandLeadStatus.Dismissed)]
    [InlineData(DemandLeadStatus.Unmatched)]
    public async Task NeverWritesToAClosedRequest(DemandLeadStatus closed)
    {
        // The customer has already been served, or has given up. An offer
        // arriving now is worse than none.
        var (db, offer) = await SeedAsync(quotedOptions: 3, leadStatus: closed);
        var queue = new CapturingQueue();

        (await Make(db, queue).TrySendAsync(offer.Id)).Reason.Should().Be("lead_closed");
        queue.Emails.Should().BeEmpty();
        db.Offers.Single().Status.Should().Be(OfferStatus.Draft);
    }

    [Fact]
    public async Task DoesNothingWithoutACustomerAddress()
    {
        var (db, offer) = await SeedAsync(quotedOptions: 3, leadEmail: "");
        var queue = new CapturingQueue();

        (await Make(db, queue).TrySendAsync(offer.Id)).Reason.Should().Be("no_email");
        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task DoesNothingForAnEmptyDraft()
    {
        var (db, offer) = await SeedAsync(quotedOptions: 0);
        var queue = new CapturingQueue();

        (await Make(db, queue).TrySendAsync(offer.Id)).Reason.Should().Be("no_options");
        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task IsIdempotent_ASecondCallCannotMailTheCustomerTwice()
    {
        var (db, offer) = await SeedAsync(quotedOptions: 2, minQuotes: 2);
        var queue = new CapturingQueue();
        var service = Make(db, queue);

        (await service.TrySendAsync(offer.Id)).WasSent.Should().BeTrue();
        (await service.TrySendAsync(offer.Id)).Reason.Should().Be("not_draft");

        queue.Emails.Should().ContainSingle("the offer is no longer a draft after the first send");
    }
}
