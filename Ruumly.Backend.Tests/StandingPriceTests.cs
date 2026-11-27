using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// A provider's own published daily rate becomes their quote (founder decision
/// 2026-11-27). Reproduces Pärnu Autorent: claimed profile, 80 € / ööpäev, the
/// same price it typed by hand on every one of its seven answered requests.
/// </summary>
public class StandingPriceTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody, string? HtmlBody)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody, htmlBody));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody, htmlBody));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private const string ParnuNote = "Hind sisaldab käibemaksu. Tagatisraha ei ole vajalik.";

    [Fact]
    public async Task ClaimedDailyRate_SeedsTheDraftOffer_AndTheEmailSaysSo()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental);
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");

        var outreach = await db.ProviderOutreaches.SingleAsync();
        var offer = await db.Offers.Include(o => o.Options).SingleAsync();
        offer.Status.Should().Be(OfferStatus.Draft);
        offer.CreatedBy.Should().Be("standing-price");

        var option = offer.Options.Should().ContainSingle().Subject;
        option.SupplierId.Should().Be(supplier.Id);
        option.CreatedFromOutreachId.Should().Be(outreach.Id,
            "keyed like a quote-page answer, so the real answer updates this option");
        option.PriceAmount.Should().Be(80m);
        option.PriceUnit.Should().Be("/ööpäev");
        option.Notes.Should().Be(ParnuNote, "provider and customer both read Estonian");

        var email = queue.Emails.Should().ContainSingle().Subject;
        email.TextBody.Should().Contain("Meie süsteemis on teie hind 80 €/ööpäev");
        email.TextBody.Should().Contain("Muuda hinda → https://ruumly.eu/et/quote/" + outreach.QuoteToken);
        email.HtmlBody.Should().Contain("80 €/ööpäev").And.Contain("Muuda hinda");

        // The short partner letter: price box, decline, then the request only.
        var quoteUrl = "https://ruumly.eu/et/quote/" + outreach.QuoteToken;
        email.TextBody.Should().Contain("Ma ei saa seda tööd võtta → " + quoteUrl + "?answer=decline");
        email.HtmlBody.Should().Contain(quoteUrl + "?answer=decline");
        email.TextBody.Should().Contain("Klient esitas selle päringu").And.Contain("Asukoht: Pärnu");
        email.TextBody.Should().NotContain("Ruumly aitab inimestel", "a claimed partner knows who we are");
        email.TextBody.Should().NotContain("Esitage oma hind");
        email.TextBody.Should().NotContain("REMOVE");
        email.HtmlBody.Should().NotContain("Ruumly aitab inimestel").And.NotContain("Esitage oma hind");
        email.TextBody.Should().StartWith("Tere, Pärnu Autorent OÜ!\n\nMeie süsteemis on teie hind");
    }

    [Fact]
    public async Task Silence_IsNotCountedAsAnAnswer()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental);
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");

        var outreach = await db.ProviderOutreaches.SingleAsync();
        outreach.Status.Should().Be(ProviderOutreachStatus.Sent);
        outreach.QuotedAt.Should().BeNull("response-time and quote-rate metrics read QuotedAt");
        outreach.QuotedAmount.Should().BeNull();
    }

    [Theory]
    [InlineData(DemandLeadCategory.Warehouse, "pärnu@autorent.ee", "ööpäev")] // a from-price is not a storage quote
    [InlineData(DemandLeadCategory.Moving,    "pärnu@autorent.ee", "ööpäev")]
    [InlineData(DemandLeadCategory.VanRental, null,                "ööpäev")] // nobody claimed the profile
    [InlineData(DemandLeadCategory.VanRental, "pärnu@autorent.ee", "kuu")]    // not a daily rate
    [InlineData(DemandLeadCategory.VanRental, "pärnu@autorent.ee", null)]
    public async Task OutsideTheNarrowGate_NothingIsSeeded_AndTheEmailIsUnchanged(
        DemandLeadCategory category, string? claimedBy, string? unit)
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db, claimedBy: claimedBy, unit: unit);
        var lead = AddLead(db, category);
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");

        (await db.Offers.CountAsync()).Should().Be(0);
        queue.Emails.Should().ContainSingle()
            .Which.TextBody.Should().NotContain("Meie süsteemis on teie hind").And.NotContain("Muuda hinda");
    }

    [Fact]
    public async Task TheProvidersRealAnswer_UpdatesTheSeededOption_NeverAddsASecond()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental);
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");
        var token = (await db.ProviderOutreaches.SingleAsync()).QuoteToken!;

        var result = await MakeQuote(db, queue).SubmitQuote(
            token, new SubmitQuoteRequest(95m, "päev", "Sobib", "Talverehvid hinnas"));

        result.Should().BeOfType<OkObjectResult>();
        var option = (await db.Offers.Include(o => o.Options).SingleAsync())
            .Options.Should().ContainSingle().Subject;
        option.PriceAmount.Should().Be(95m);
        option.PriceUnit.Should().Be("/ööpäev");
        option.Notes.Should().Be("Talverehvid hinnas");
    }

    [Fact]
    public async Task ForeignLanguageCustomer_GetsTheTranslatedUnit_ButNotTheEstonianNote()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental, language: "en");
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");

        var option = (await db.Offers.Include(o => o.Options).SingleAsync())
            .Options.Should().ContainSingle().Subject;
        option.PriceUnit.Should().Be("/day");
        option.Notes.Should().BeNull("the note is free text in Estonian and the offer is read in English");
    }

    [Fact]
    public async Task Resend_DoesNotAddASecondOptionForTheSameProvider()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental);
        await db.SaveChangesAsync();

        var outreach = TestServices.Outreach(db, queue, TestServices.Config());
        await outreach.SendAsync(lead, [supplier.Id], resend: false, actor: "test");
        await outreach.SendAsync(lead, [supplier.Id], resend: true, actor: "test");

        (await db.ProviderOutreaches.CountAsync()).Should().Be(2);
        (await db.OfferOptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExistingDraft_GetsTheOptionAppended()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental);
        var draft = new Offer
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, Token = "existing-draft",
            Status = OfferStatus.Draft, Language = "et", CreatedAt = DateTime.UtcNow, CreatedBy = "admin",
        };
        db.Offers.Add(draft);
        db.OfferOptions.Add(new OfferOption
        {
            Id = Guid.NewGuid(), OfferId = draft.Id, Title = "Admin option", PriceAmount = 70m, SortOrder = 0,
        });
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");

        var offer = await db.Offers.Include(o => o.Options).SingleAsync();
        offer.Id.Should().Be(draft.Id);
        offer.Options.Should().HaveCount(2);
        offer.Options.Single(o => o.SupplierId == supplier.Id).SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task Decline_TakesTheProfilePriceBackOutOfTheDraft()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental);
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");
        var token = (await db.ProviderOutreaches.SingleAsync()).QuoteToken!;
        (await db.OfferOptions.CountAsync()).Should().Be(1);

        var result = await MakeQuote(db, queue).Decline(token, new DeclineQuoteRequest("no_capacity"));

        result.Should().BeOfType<OkObjectResult>();
        (await db.OfferOptions.CountAsync()).Should().Be(0,
            "a provider who said no must not stay on the draft at their profile price");
        (await db.ProviderOutreaches.SingleAsync()).Status.Should().Be(ProviderOutreachStatus.Declined);
    }

    [Fact]
    public async Task Decline_AfterTheOfferWasSent_LeavesItAndWarnsOps()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental);
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");
        var offer = await db.Offers.SingleAsync();
        offer.Status = OfferStatus.Sent;
        await db.SaveChangesAsync();
        var token = (await db.ProviderOutreaches.SingleAsync()).QuoteToken!;
        queue.Emails.Clear();

        await MakeQuote(db, queue).Decline(token, new DeclineQuoteRequest());

        (await db.OfferOptions.CountAsync()).Should().Be(1, "a sent offer is not edited behind the customer's back");
        queue.Emails.Should().ContainSingle().Which.TextBody.Should().StartWith("ACTION NEEDED");
    }

    [Fact]
    public async Task ClosedLead_IsNotSeeded()
    {
        var (db, queue) = Setup();
        var supplier = AddSupplier(db);
        var lead = AddLead(db, DemandLeadCategory.VanRental, status: DemandLeadStatus.Converted);
        await db.SaveChangesAsync();

        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");

        (await db.Offers.CountAsync()).Should().Be(0);
    }

    private static (RuumlyDbContext, CapturingEmailQueue) Setup() => (TestDbContext.Create(), new CapturingEmailQueue());

    private static Supplier AddSupplier(
        RuumlyDbContext db, string? claimedBy = "pärnu@autorent.ee", string? unit = "ööpäev")
    {
        var s = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Pärnu Autorent OÜ", ContactName = "Rainer",
            ContactEmail = "info@parnuautorent.ee", ContactPhone = "+372 555 0000",
            IsActive = true, Country = "EE", ServiceTypesJson = "[\"vanrental\"]",
            ClaimedByEmail = claimedBy, PriceFrom = 80m, PriceUnit = unit, PriceNote = ParnuNote,
        };
        db.Suppliers.Add(s);
        return s;
    }

    private static DemandLead AddLead(
        RuumlyDbContext db, DemandLeadCategory category, string language = "et",
        DemandLeadStatus status = DemandLeadStatus.New)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "klient@example.ee", City = "Pärnu",
            Category = category, Language = language,
            Source = "concierge", Status = status,
        };
        db.DemandLeads.Add(lead);
        return lead;
    }

    private static QuoteController MakeQuote(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue,
            new Ruumly.Backend.Services.Implementations.OfferAutoSendService(
                db, queue,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                TestServices.OutcomeNotifier(db, queue),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Ruumly.Backend.Services.Implementations.OfferAutoSendService>.Instance),
            new TestServices.NoStorage(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QuoteController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
}
