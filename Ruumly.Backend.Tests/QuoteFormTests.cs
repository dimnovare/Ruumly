using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Feature B (spec 2026-07-16 §B): a provider opens the tokenized quote page
/// from their outreach email and submits a price without an account. The page
/// leaks no customer identity; submitting flips the outreach to Replied and
/// auto-seeds the lead's draft offer, but creates no customer email, Booking or
/// Order — the admin later reviews the seeded draft and sends it.
/// </summary>
public class QuoteFormTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody, string? ReplyTo)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody, null));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody, replyTo));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static AdminOffersController MakeAdmin(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue, new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://ruumly.eu" })
                .Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, "Admin"),
                        new Claim(ClaimTypes.Email, "ops@ruumly.eu"),
                    ], "test")),
                },
            },
        };

    private static QuoteController MakePublic(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static DemandLead MakeLead(RuumlyDbContext db)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", Name = "Mari Maasikas", Phone = "+372 5555 1234",
            City = "Tallinn", ToCity = "Tartu", Category = DemandLeadCategory.Moving,
            NeedDate = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            Details = "2-room flat", Language = "en", Source = "concierge",
            Status = DemandLeadStatus.New, CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        db.SaveChanges();
        return lead;
    }

    private static Supplier MakeSupplier(RuumlyDbContext db, string name, string contactEmail)
    {
        var s = new Supplier
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C",
            ContactEmail = contactEmail, ContactPhone = "1", IsActive = true,
        };
        db.Suppliers.Add(s);
        db.SaveChanges();
        return s;
    }

    /// <summary>Sends outreach to one supplier and returns the row's minted quote token.</summary>
    private static async Task<string> SendOutreachAndGetToken(
        RuumlyDbContext db, DemandLead lead, Supplier supplier)
    {
        await MakeAdmin(db, new CapturingEmailQueue())
            .SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
        return db.ProviderOutreaches.Single(o => o.SupplierId == supplier.Id).QuoteToken!;
    }

    // ─── Token minted on send ─────────────────────────────────────────────────

    [Fact]
    public async Task SendOutreach_MintsUniqueUrlSafeQuoteTokenPerRow()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var s1    = MakeSupplier(db, "Alpha OÜ", "a@x.ee");
        var s2    = MakeSupplier(db, "Beta OÜ", "b@x.ee");

        await MakeAdmin(db, new CapturingEmailQueue())
            .SendOutreach(lead.Id, new OutreachRequest([s1.Id, s2.Id]));

        var rows = db.ProviderOutreaches.ToList();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.QuoteToken != null, "every sent row gets its own quote token");
        rows.Select(r => r.QuoteToken).Should().OnlyHaveUniqueItems();
        rows.Should().AllSatisfy(r =>
            Regex.IsMatch(r.QuoteToken!, "^[A-Za-z0-9_-]{43}$").Should().BeTrue(
                "the token is url-safe base64 of 32 bytes without padding"));
    }

    // ─── Public GET ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetQuote_ExposesLeadAsk_ButNoCustomerPii()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);

        var result = await MakePublic(db, new CapturingEmailQueue()).GetQuote(token);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;

        dto.Provider.Name.Should().Be("Big Movers OÜ");
        dto.Lead.Category.Should().Be("moving");
        dto.Lead.City.Should().Be("Tallinn");
        dto.Lead.ToCity.Should().Be("Tartu");
        dto.Currency.Should().Be("EUR");
        dto.AlreadySubmitted.Should().BeFalse();
        dto.Existing.Should().BeNull();

        var json = JsonSerializer.Serialize(dto);
        json.Should().Contain("Tallinn").And.Contain("moving", "the lead ask is the point of the page");
        json.Should().NotContain("cust@x.ee").And.NotContain("Mari Maasikas").And.NotContain("+372 5555 1234");
        json.Should().NotContain(token, "the token must not be echoed in the payload");
        json.Should().NotContain("big@movers.ee", "the provider's contact email is not part of the quote page");
    }

    [Fact]
    public async Task GetQuote_AndSubmit_UnknownToken_404()
    {
        var db  = TestDbContext.Create();
        var pub = MakePublic(db, new CapturingEmailQueue());

        (await pub.GetQuote("does-not-exist")).Should().BeOfType<NotFoundObjectResult>();
        (await pub.SubmitQuote("does-not-exist", new SubmitQuoteRequest(10m)))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetQuote_AfterSubmit_ReturnsAlreadySubmittedWithPrefill()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var pub      = MakePublic(db, new CapturingEmailQueue());

        await pub.SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime", "next week", "2 movers"));

        var dto = (await pub.GetQuote(token))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;
        dto.AlreadySubmitted.Should().BeTrue();
        dto.Existing.Should().NotBeNull();
        dto.Existing!.Amount.Should().Be(250m);
        dto.Existing.Unit.Should().Be("onetime");
        dto.Existing.Availability.Should().Be("next week");
        dto.Existing.Note.Should().Be("2 movers");
    }

    // ─── Public POST ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitQuote_MarksReplied_StoresQuote_SeedsOneOption_Idempotent_NoCommerceOrCustomerEmail()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var opsQueue = new CapturingEmailQueue();
        var pub      = MakePublic(db, opsQueue);

        var result = await pub.SubmitQuote(
            token, new SubmitQuoteRequest(250m, "onetime", "next week", "includes 2 movers"));
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<QuoteSubmittedDto>().Subject;
        dto.Ok.Should().BeTrue();
        dto.Amount.Should().Be(250m);

        // The outreach row carries the answer and flips to Replied.
        var row = db.ProviderOutreaches.Single();
        row.Status.Should().Be(ProviderOutreachStatus.Replied);
        row.QuotedAmount.Should().Be(250m);
        row.QuotedUnit.Should().Be("onetime");
        row.QuotedAvailability.Should().Be("next week");
        row.QuotedNote.Should().Be("includes 2 movers");
        row.QuotedAt.Should().NotBeNull();

        // Exactly one draft offer + one option keyed by the supplier.
        var offer = db.Offers.Should().ContainSingle().Subject;
        offer.Status.Should().Be(OfferStatus.Draft);
        var options = db.OfferOptions.Where(o => o.OfferId == offer.Id).ToList();
        options.Should().ContainSingle();
        options[0].SupplierId.Should().Be(supplier.Id);
        options[0].PriceAmount.Should().Be(250m);
        options[0].PriceUnit.Should().Be("onetime");
        options[0].Title.Should().Contain("Big Movers OÜ").And.Contain("Tallinn");

        // Ops is alerted; the customer is NOT emailed, and no commerce records exist.
        opsQueue.Emails.Should().Contain(e => e.To == "info@ruumly.eu");
        opsQueue.Emails.Should().NotContain(e => e.To == "cust@x.ee",
            "submitting a quote never emails the customer — the admin reviews the seeded draft first");
        db.Bookings.Should().BeEmpty();
        db.Orders.Should().BeEmpty();

        // Re-submit UPDATES the same option (no duplicate, no second offer/row).
        (await pub.SubmitQuote(token, new SubmitQuoteRequest(199m, "onetime", "this week", "revised")))
            .Should().BeOfType<OkObjectResult>();
        db.Offers.Should().ContainSingle("re-submit reuses the newest draft offer");
        var afterOptions = db.OfferOptions.Where(o => o.OfferId == offer.Id).ToList();
        afterOptions.Should().ContainSingle("re-submit updates the same SupplierId-keyed option, never duplicates");
        afterOptions[0].PriceAmount.Should().Be(199m);
        db.ProviderOutreaches.Should().ContainSingle("re-submit never mints a second outreach row");
        db.ProviderOutreaches.Single().QuotedAmount.Should().Be(199m);
    }

    [Fact]
    public async Task SubmitQuote_RejectsNegativeAmountAndAngleBrackets_WithoutMutating()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var pub      = MakePublic(db, new CapturingEmailQueue());

        (await pub.SubmitQuote(token, new SubmitQuoteRequest(-1m)))
            .Should().BeOfType<BadRequestObjectResult>();
        (await pub.SubmitQuote(token, new SubmitQuoteRequest(10m, Note: "a <script> b")))
            .Should().BeOfType<BadRequestObjectResult>();

        var row = db.ProviderOutreaches.Single();
        row.Status.Should().Be(ProviderOutreachStatus.Sent, "a rejected quote leaves the outreach untouched");
        row.QuotedAt.Should().BeNull();
        db.Offers.Should().BeEmpty("a rejected quote seeds no offer");
    }
}
