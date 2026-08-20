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
using Microsoft.Extensions.Logging.Abstractions;

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
        new(db, queue, TestServices.Config(), TestServices.Outreach(db, queue, TestServices.Config()),
            TestServices.OutcomeNotifier(db, queue),
            NullLogger<AdminOffersController>.Instance)
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
        // The REAL auto-send service, with no offerAutoSend setting row in this
        // database — so every existing expectation in this file now also proves
        // that a provider quote does not email the customer by default.
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

    /// <summary>Exact-case property read — the admin DTO field names ARE the contract.</summary>
    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    private static List<object> ReadList(IActionResult result) =>
        ((System.Collections.IEnumerable)result.Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Cast<object>().ToList();

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
        // The customer's copy is written in the customer's language: "onetime" is
        // the raw value the form and our own listings store, not words anybody
        // reads. The provider's own wording survives untouched on QuotedUnit
        // above, which is the half ops needs to see.
        options[0].PriceUnit.Should().Be("one-time", "the lead is English");
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

    // ─── Admin DTO surface (what the workspace renders) ───────────────────────

    [Fact]
    public async Task GetLeadOutreach_ExposesQuoteFields_NullBeforeSubmit_PopulatedAfter()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var admin    = MakeAdmin(db, new CapturingEmailQueue());

        // Before the provider answers, every quote field is null (same shape a
        // legacy token-less row returns).
        var before = ReadList(await admin.GetLeadOutreach(lead.Id)).Should().ContainSingle().Subject;
        Prop(before, "status").Should().Be("sent");
        Prop(before, "quotedAmount").Should().BeNull();
        Prop(before, "quotedUnit").Should().BeNull();
        Prop(before, "quotedAvailability").Should().BeNull();
        Prop(before, "quotedNote").Should().BeNull();
        Prop(before, "quotedAt").Should().BeNull();

        await MakePublic(db, new CapturingEmailQueue())
            .SubmitQuote(token, new SubmitQuoteRequest(250m, "kuu", "next week", "2 movers"));

        // After: the history row can render "Quoted 250 kuu".
        var after = ReadList(await admin.GetLeadOutreach(lead.Id)).Should().ContainSingle().Subject;
        Prop(after, "status").Should().Be("replied");
        Prop(after, "quotedAmount").Should().Be(250m);
        Prop(after, "quotedUnit").Should().Be("kuu", "the provider's localized unit is stored verbatim");
        Prop(after, "quotedAvailability").Should().Be("next week");
        Prop(after, "quotedNote").Should().Be("2 movers");
        Prop(after, "quotedAt").Should().NotBeNull();
    }

    [Fact]
    public async Task GetLeadOffers_FlagsAutoSeededOptionFromProviderQuote_ButNotManualOnes()
    {
        var db      = TestDbContext.Create();
        var lead    = MakeLead(db);
        var quoting = MakeSupplier(db, "Quoting OÜ", "q@x.ee");
        var manual  = MakeSupplier(db, "Manual OÜ", "m@x.ee");
        var admin   = MakeAdmin(db, new CapturingEmailQueue());

        var token = await SendOutreachAndGetToken(db, lead, quoting);
        await MakePublic(db, new CapturingEmailQueue())
            .SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime"));

        // Alongside the auto-seeded option the admin adds two of their own: one
        // for a provider that never quoted, one free-form with no supplier.
        var offerId = db.Offers.Single().Id;
        db.OfferOptions.Add(new OfferOption
        {
            Id = Guid.NewGuid(), OfferId = offerId, SupplierId = manual.Id,
            Title = "Manual OÜ — Tallinn", PriceAmount = 300m, SortOrder = 1,
        });
        db.OfferOptions.Add(new OfferOption
        {
            Id = Guid.NewGuid(), OfferId = offerId,
            Title = "Free-form option", PriceAmount = 400m, SortOrder = 2,
        });
        await db.SaveChangesAsync();

        var offer   = ReadList(await admin.GetLeadOffers(lead.Id)).Should().ContainSingle().Subject;
        var options = ((System.Collections.IEnumerable)Prop(offer, "options")!).Cast<object>().ToList();

        options.Should().HaveCount(3);
        Prop(options[0], "fromProviderQuote").Should().Be(true,
            "this option's provider answered the outreach with a price");
        Prop(options[1], "fromProviderQuote").Should().Be(false,
            "this provider was never quoted — the admin added the option by hand");
        Prop(options[2], "fromProviderQuote").Should().Be(false,
            "a free-form option has no supplier to attribute a quote to");
    }

    // ─── Seeding into an EXISTING draft (the branch every other test skips) ───

    [Fact]
    public async Task SubmitQuote_IntoPreExistingAdminDraft_SeedsOption_WithoutLosingTheQuote()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var admin    = MakeAdmin(db, new CapturingEmailQueue());

        // The admin pre-builds the draft (the Stage-2 flow the spec supports), so
        // the quote must attach to an ALREADY-TRACKED offer rather than create one.
        (await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Admin's own option", PriceAmount: 500m)])))
            .Should().BeOfType<OkObjectResult>();

        (await MakePublic(db, new CapturingEmailQueue())
                .SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime", "next week", "2 movers")))
            .Should().BeOfType<OkObjectResult>();

        // The quote survived (this is what a rollback would destroy).
        var row = db.ProviderOutreaches.Single();
        row.Status.Should().Be(ProviderOutreachStatus.Replied);
        row.QuotedAmount.Should().Be(250m);

        db.Offers.Should().ContainSingle("the pre-existing draft is reused, never duplicated");
        var options = db.OfferOptions.Where(o => o.OfferId == db.Offers.Single().Id).ToList();
        options.Should().HaveCount(2, "the seeded option joins the admin's own");
        options.Should().ContainSingle(o => o.SupplierId == supplier.Id)
            .Which.PriceAmount.Should().Be(250m);
    }

    [Fact]
    public async Task SubmitQuote_TwoProvidersOnOneLead_BothSeedOptions_ForCompare()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var first = MakeSupplier(db, "First OÜ", "first@x.ee");
        var second = MakeSupplier(db, "Second OÜ", "second@x.ee");

        await MakeAdmin(db, new CapturingEmailQueue())
            .SendOutreach(lead.Id, new OutreachRequest([first.Id, second.Id]));
        var firstToken  = db.ProviderOutreaches.Single(o => o.SupplierId == first.Id).QuoteToken!;
        var secondToken = db.ProviderOutreaches.Single(o => o.SupplierId == second.Id).QuoteToken!;
        var pub = MakePublic(db, new CapturingEmailQueue());

        // The first quote creates the draft; the SECOND must attach to it — the
        // whole point of the feature is comparing providers side by side.
        (await pub.SubmitQuote(firstToken, new SubmitQuoteRequest(250m, "onetime")))
            .Should().BeOfType<OkObjectResult>();
        (await pub.SubmitQuote(secondToken, new SubmitQuoteRequest(300m, "onetime")))
            .Should().BeOfType<OkObjectResult>();

        db.ProviderOutreaches.Should().AllSatisfy(o =>
            o.Status.Should().Be(ProviderOutreachStatus.Replied));
        db.Offers.Should().ContainSingle();
        var options = db.OfferOptions.Where(o => o.OfferId == db.Offers.Single().Id).ToList();
        options.Should().HaveCount(2, "each provider gets its own option to compare");
        options.Select(o => o.PriceAmount).Should().BeEquivalentTo([250m, 300m]);
    }

    // ─── Provider quotes never destroy admin-authored work ────────────────────

    [Fact]
    public async Task SubmitQuote_NeverOverwritesAdminAuthoredOptionForTheSameSupplier()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var admin    = MakeAdmin(db, new CapturingEmailQueue());

        // The admin hand-authors an option for THIS SAME supplier (a price they
        // negotiated by phone, tied to a specific depot).
        (await admin.CreateOffer(lead.Id, new CreateOfferRequest(Options:
        [
            new OfferOptionInput("Negotiated price — Lasnamäe depot", SupplierId: supplier.Id,
                PriceAmount: 500m, PriceUnit: "onetime", Notes: "Call Jaan first"),
        ]))).Should().BeOfType<OkObjectResult>();

        (await MakePublic(db, new CapturingEmailQueue())
                .SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime")))
            .Should().BeOfType<OkObjectResult>();

        var options = db.OfferOptions.Where(o => o.OfferId == db.Offers.Single().Id).ToList();
        options.Should().HaveCount(2, "the quote adds its own option beside the admin's, never over it");

        var adminOption = options.Single(o => o.CreatedFromOutreachId == null);
        adminOption.Title.Should().Be("Negotiated price — Lasnamäe depot", "the admin's title survives");
        adminOption.PriceAmount.Should().Be(500m);
        adminOption.Notes.Should().Be("Call Jaan first", "a provider quote must not blank the admin's notes");

        options.Single(o => o.CreatedFromOutreachId != null).PriceAmount.Should().Be(250m);

        // The badge distinguishes them even though both point at the same supplier.
        var offer  = ReadList(await admin.GetLeadOffers(lead.Id)).Should().ContainSingle().Subject;
        var mapped = ((System.Collections.IEnumerable)Prop(offer, "options")!).Cast<object>().ToList();
        mapped.Where(o => (bool)Prop(o, "fromProviderQuote")!).Should().ContainSingle(
            "only the quote-seeded option is from a provider quote — the admin's is not");
    }

    [Fact]
    public async Task SubmitQuote_AfterAnAdminEdit_CorrectsItsOwnOption_InsteadOfShowingTheProviderTwice()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var admin    = MakeAdmin(db, new CapturingEmailQueue());

        (await MakePublic(db, new CapturingEmailQueue())
                .SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime", "next week", "2 movers")))
            .Should().BeOfType<OkObjectResult>();

        // The admin opens the seeded draft and saves an edit — a customer note
        // and a placeholder of their own. The workspace PATCHes the WHOLE option
        // set every time, so this save is where the quote's option used to be
        // deleted and silently reborn as an anonymous row.
        var draft   = ReadList(await admin.GetLeadOffers(lead.Id)).Should().ContainSingle().Subject;
        var offerId = (Guid)Prop(draft, "id")!;
        var seeded  = ((System.Collections.IEnumerable)Prop(draft, "options")!)
            .Cast<object>().Should().ContainSingle().Subject;

        (await admin.UpdateOffer(offerId, new UpdateOfferRequest(
            CustomerNote: "Two quotes so far — a third is coming.",
            Options:
            [
                new OfferOptionInput(
                    Title:       (string)Prop(seeded, "title")!,
                    SupplierId:  (Guid?)Prop(seeded, "supplierId"),
                    PriceAmount: (decimal?)Prop(seeded, "priceAmount"),
                    PriceUnit:   (string?)Prop(seeded, "priceUnit"),
                    Notes:       (string?)Prop(seeded, "notes"),
                    Id:          (Guid?)Prop(seeded, "id")),
                new OfferOptionInput("Placeholder — waiting on Kiirkolimine", PriceAmount: 400m),
            ],
            Version: (int?)Prop(draft, "version")))).Should().BeOfType<OkObjectResult>();

        var outreachId = db.ProviderOutreaches.Single(o => o.SupplierId == supplier.Id).Id;
        db.OfferOptions.Where(o => o.CreatedFromOutreachId == outreachId).Should().ContainSingle(
            "an admin's edit must not cost the option the link to the quote that seeded it");

        // The provider comes back with a corrected price.
        (await MakePublic(db, new CapturingEmailQueue())
                .SubmitQuote(token, new SubmitQuoteRequest(199m, "onetime")))
            .Should().BeOfType<OkObjectResult>();

        var options = db.OfferOptions.Where(o => o.OfferId == offerId).ToList();
        options.Where(o => o.SupplierId == supplier.Id).Should().ContainSingle(
            "the correction updates the provider's own option — the customer must never be " +
            "shown one company twice at two different prices");
        var quoted = options.Single(o => o.SupplierId == supplier.Id);
        quoted.PriceAmount.Should().Be(199m, "the corrected price is the one that counts");
        quoted.CreatedFromOutreachId.Should().Be(outreachId);
        options.Should().HaveCount(2, "the admin's placeholder is still there beside it");
        options.Single(o => o.CreatedFromOutreachId == null).PriceAmount.Should().Be(400m);

        // And the admin can still tell a real quote from their own guess.
        var reloaded = ReadList(await admin.GetLeadOffers(lead.Id)).Should().ContainSingle().Subject;
        ((System.Collections.IEnumerable)Prop(reloaded, "options")!).Cast<object>()
            .Where(o => (bool)Prop(o, "fromProviderQuote")!).Should().ContainSingle(
                "the badge survives the edit that used to erase it");
    }

    [Fact]
    public async Task SubmitQuote_BlankNoteOnResubmit_KeepsThePreviousNote()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var pub      = MakePublic(db, new CapturingEmailQueue());

        await pub.SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime", "next week", "2 movers"));
        (await pub.SubmitQuote(token, new SubmitQuoteRequest(199m, "onetime", "this week", Note: null)))
            .Should().BeOfType<OkObjectResult>();

        db.ProviderOutreaches.Single().QuotedNote.Should().Be("2 movers",
            "an omitted note leaves the previously submitted one intact");
        var option = db.OfferOptions.Single();
        option.Notes.Should().Be("2 movers");
        option.PriceAmount.Should().Be(199m, "the price itself still updates");
    }

    // ─── Public-input hardening ───────────────────────────────────────────────

    [Fact]
    public async Task SubmitQuote_AbsurdPrice_IsA400_NotAColumnOverflow500()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);

        (await MakePublic(db, new CapturingEmailQueue())
                .SubmitQuote(token, new SubmitQuoteRequest(9_999_999_999m)))
            .Should().BeOfType<BadRequestObjectResult>();

        db.Offers.Should().BeEmpty();
        db.ProviderOutreaches.Single().QuotedAt.Should().BeNull();
    }

    [Fact]
    public async Task SubmitQuote_OnClosedLead_409_LeadClosed_SeedsNothing_AndGetSaysClosed()
    {
        foreach (var terminal in new[]
        {
            DemandLeadStatus.Converted, DemandLeadStatus.Dismissed, DemandLeadStatus.Unmatched,
        })
        {
            var db       = TestDbContext.Create();
            var lead     = MakeLead(db);
            var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
            var token    = await SendOutreachAndGetToken(db, lead, supplier);
            var pub      = MakePublic(db, new CapturingEmailQueue());

            lead.Status = terminal;
            await db.SaveChangesAsync();

            var conflict = (await pub.SubmitQuote(token, new SubmitQuoteRequest(250m)))
                .Should().BeOfType<ConflictObjectResult>($"{terminal} is a closed request").Subject;
            Prop(conflict.Value!, "reason").Should().Be("lead_closed");

            db.Offers.Should().BeEmpty("a closed request must never spawn a fresh draft");
            db.ProviderOutreaches.Single().QuotedAt.Should().BeNull();
            db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Sent);

            // The page can render the closed state without a failed submit first.
            var dto = (await pub.GetQuote(token))
                .Should().BeOfType<OkObjectResult>().Subject.Value
                .Should().BeOfType<PublicQuoteDto>().Subject;
            dto.Closed.Should().BeTrue();
        }
    }

    // ─── Lost-update guard on the admin's replace-set PATCH ───────────────────

    [Fact]
    public async Task UpdateOffer_StaleVersion_409_AndDoesNotDeleteTheProviderSeededOption()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers OÜ", "big@movers.ee");
        var token    = await SendOutreachAndGetToken(db, lead, supplier);
        var admin    = MakeAdmin(db, new CapturingEmailQueue());

        // The admin opens the draft — and notes the version they read.
        var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Admin option", PriceAmount: 500m)]));
        var body         = created.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var offerId      = (Guid)Prop(body, "id")!;
        var staleVersion = (int)Prop(body, "version")!;

        // A provider quote lands BEFORE the admin gets around to saving.
        (await MakePublic(db, new CapturingEmailQueue())
                .SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime")))
            .Should().BeOfType<OkObjectResult>();
        db.OfferOptions.Count(o => o.OfferId == offerId).Should().Be(2);

        // Their save carries the pre-quote option set — it must be refused, not applied.
        (await admin.UpdateOffer(offerId, new UpdateOfferRequest(
                Options: [new OfferOptionInput("Admin option", PriceAmount: 500m)],
                Version: staleVersion)))
            .Should().BeOfType<ConflictObjectResult>();
        db.OfferOptions.Count(o => o.OfferId == offerId).Should().Be(2,
            "the stale replace-set was rejected — the provider's quote survives");

        // After reloading, the same edit applies with the current version.
        var fresh = ReadList(await admin.GetLeadOffers(lead.Id)).Should().ContainSingle().Subject;
        (await admin.UpdateOffer(offerId, new UpdateOfferRequest(
                Options: [new OfferOptionInput("Admin option", PriceAmount: 500m)],
                Version: (int)Prop(fresh, "version")!)))
            .Should().BeOfType<OkObjectResult>();
        db.OfferOptions.Count(o => o.OfferId == offerId).Should().Be(1,
            "a deliberate replace-set from a fresh read still applies");
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
