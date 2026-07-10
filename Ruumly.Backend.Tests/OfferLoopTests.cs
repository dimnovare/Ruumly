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
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Concierge offer loop (spec 2026-07-10 §5): the admin builds an offer for a
/// demand lead, sends it to a tokenized public page, asks providers for
/// availability without leaking the customer's identity, and the customer
/// picks an option.
/// </summary>
public class OfferLoopTests
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

    private static OffersController MakePublic(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static DemandLead MakeLead(RuumlyDbContext db,
        string email = "cust@x.ee", string? name = "Mari Maasikas", string? phone = "+372 5555 1234")
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = email, Name = name, Phone = phone,
            City = "Tallinn", ToCity = "Tartu", Category = DemandLeadCategory.Moving,
            NeedDate = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            Details = "2-room flat", Language = "en", Source = "concierge",
            Status = DemandLeadStatus.New, CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        db.SaveChanges();
        return lead;
    }

    private static Supplier MakeSupplier(RuumlyDbContext db, string name, string contactEmail = "")
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

    private static async Task<Offer> MakeSentOffer(RuumlyDbContext db, DemandLead lead, int optionCount = 2)
    {
        var offer = new Offer
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, Token = OfferToken.Generate(),
            Status = OfferStatus.Sent, SentAt = DateTime.UtcNow, Language = "en",
            CustomerNote = "We checked availability for your dates.",
            CreatedBy = "ops@ruumly.eu",
        };
        for (var i = 0; i < optionCount; i++)
            offer.Options.Add(new OfferOption
            {
                Id = Guid.NewGuid(), OfferId = offer.Id, Title = $"Option {i + 1}",
                PriceAmount = 100m + i, PriceUnit = "onetime", SortOrder = i,
            });
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        return offer;
    }

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)?.GetValue(o);

    // ─── Token format + uniqueness ────────────────────────────────────────────

    [Fact]
    public void OfferToken_IsUrlSafeBase64_43Chars_AndUnique()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => OfferToken.Generate()).ToList();

        tokens.Should().OnlyHaveUniqueItems("256-bit random tokens must never repeat in practice");
        tokens.Should().AllSatisfy(t =>
            Regex.IsMatch(t, "^[A-Za-z0-9_-]{43}$").Should().BeTrue(
                $"token '{t}' must be url-safe base64 of 32 bytes without padding"));
    }

    [Fact]
    public async Task CreateOffer_GeneratesToken_DefaultsLanguageFromLead_AndStartsDraft()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);  // Language = "en"
        var admin = MakeAdmin(db, new CapturingEmailQueue());

        var result = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Big Movers OÜ", PriceAmount: 250m, PriceUnit: "onetime")]));

        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        Prop(body, "status").Should().Be("draft");
        Prop(body, "language").Should().Be("en", "the offer language defaults to the lead's language");
        Prop(body, "createdBy").Should().Be("ops@ruumly.eu");

        var offer = db.Offers.Single();
        offer.Token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        offer.Options.Should().ContainSingle();
    }

    // ─── Send ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendOffer_EmailsCustomer_MarksSent_AndAutoQuotesLead()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();
        var lead  = MakeLead(db);
        var admin = MakeAdmin(db, queue);

        var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Big Movers OÜ", PriceAmount: 250m)]));
        var offerId = (Guid)Prop(created.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;

        var result = await admin.SendOffer(offerId);
        var body   = result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        Prop(body, "status").Should().Be("sent");
        Prop(body, "sentAt").Should().NotBeNull();

        var storedLead = db.DemandLeads.Single();
        storedLead.Status.Should().Be(DemandLeadStatus.Quoted,
            "an offer in the customer's inbox IS the quote");
        storedLead.ContactedAt.Should().NotBeNull(
            "the shared lifecycle stamps the first admin touch");

        var email = queue.Emails.Should().ContainSingle().Subject;
        email.To.Should().Be("cust@x.ee");
        email.ReplyTo.Should().Be("info@ruumly.eu",
            "the email invites the customer to reply — replies must not vanish into noreply@");
        email.TextBody.Should().Contain(db.Offers.Single().Token, "the email links to the public offer page");
        email.TextBody.Should().Contain("https://ruumly.eu/en/offer/", "link is localized to the offer language");
        email.TextBody.Should().Contain("Big Movers OÜ");
    }

    [Fact]
    public async Task SendOffer_WithoutOptions_OrWithoutLeadEmail_400_AndNothingSent()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();
        var admin = MakeAdmin(db, queue);

        // No options.
        var lead1  = MakeLead(db);
        var no1    = await admin.CreateOffer(lead1.Id, new CreateOfferRequest());
        var offer1 = (Guid)Prop(no1.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;
        (await admin.SendOffer(offer1)).Should().BeOfType<BadRequestObjectResult>();

        // No lead email.
        var lead2  = MakeLead(db, email: "");
        var no2    = await admin.CreateOffer(lead2.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Opt")]));
        var offer2 = (Guid)Prop(no2.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;
        (await admin.SendOffer(offer2)).Should().BeOfType<BadRequestObjectResult>();

        queue.Emails.Should().BeEmpty();
        db.DemandLeads.Should().AllSatisfy(l => l.Status.Should().Be(DemandLeadStatus.New,
            "failed sends must not touch the lead lifecycle"));
    }

    // ─── PATCH (replace-set) ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOffer_ReplacesWholeOptionSet_AndBlocksChosenOffers()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var admin = MakeAdmin(db, new CapturingEmailQueue());

        var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Old A"), new OfferOptionInput("Old B")]));
        var offerId = (Guid)Prop(created.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;

        var patch = await admin.UpdateOffer(offerId, new UpdateOfferRequest(
            CustomerNote: "Updated note",
            Options: [new OfferOptionInput("New only", PriceAmount: 99m)]));
        patch.Should().BeOfType<OkObjectResult>();

        var options = db.OfferOptions.Where(o => o.OfferId == offerId).ToList();
        options.Should().ContainSingle("PATCH options are replace-set, not append").
            Which.Title.Should().Be("New only");
        db.Offers.Single().CustomerNote.Should().Be("Updated note");

        // Chosen offers are frozen.
        var offer = db.Offers.Single();
        offer.Status         = OfferStatus.Chosen;
        offer.ChosenOptionId = options[0].Id;
        await db.SaveChangesAsync();

        (await admin.UpdateOffer(offerId, new UpdateOfferRequest(CustomerNote: "nope")))
            .Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task UpdateOffer_ExplicitSortOrders_InclZero_DriveOrdering_AdminAndPublic()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var admin = MakeAdmin(db, new CapturingEmailQueue());

        var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest());
        var offerId = (Guid)Prop(created.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;

        // Payload order A,B,C — explicit sortOrders (including an explicit 0)
        // must win over payload position.
        var patch = await admin.UpdateOffer(offerId, new UpdateOfferRequest(Options:
        [
            new OfferOptionInput("A", SortOrder: 2),
            new OfferOptionInput("B", SortOrder: 0),
            new OfferOptionInput("C", SortOrder: 1),
        ]));
        var body = patch.Should().BeOfType<OkObjectResult>().Subject.Value!;

        static List<object?> Titles(object dto) =>
            ((System.Collections.IEnumerable)Prop(dto, "options")!)
                .Cast<object>().Select(o => Prop(o, "title")).ToList();

        // (explicit sortOrder: 0 must not be silently replaced by the payload index)
        Titles(body).Should().Equal("B", "C", "A");

        // The customer sees the same deterministic order on the public page.
        (await admin.SendOffer(offerId)).Should().BeOfType<OkObjectResult>();
        var result = await MakePublic(db, new CapturingEmailQueue())
            .GetOffer(db.Offers.Single().Token);
        Titles(result.Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Should().Equal("B", "C", "A");
    }

    // ─── Public GET ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOffer_FirstGetMarksViewed_SecondGetKeepsTimestamp()
    {
        var db     = TestDbContext.Create();
        var lead   = MakeLead(db);
        var offer  = await MakeSentOffer(db, lead);
        var pub    = MakePublic(db, new CapturingEmailQueue());

        (await pub.GetOffer(offer.Token)).Should().BeOfType<OkObjectResult>();
        var stored = db.Offers.Single();
        stored.Status.Should().Be(OfferStatus.Viewed);
        var viewedAt = stored.ViewedAt;
        viewedAt.Should().NotBeNull("the first successful GET is the read receipt");

        (await pub.GetOffer(offer.Token)).Should().BeOfType<OkObjectResult>();
        db.Offers.Single().ViewedAt.Should().Be(viewedAt,
            "ViewedAt is stamped once, on the FIRST view only");
    }

    [Fact]
    public async Task GetOffer_UnknownDraftOrExpiredToken_404()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db);
        var pub  = MakePublic(db, new CapturingEmailQueue());

        (await pub.GetOffer("does-not-exist")).Should().BeOfType<NotFoundObjectResult>();

        var draft = await MakeSentOffer(db, lead);
        draft.Status = OfferStatus.Draft;
        await db.SaveChangesAsync();
        (await pub.GetOffer(draft.Token)).Should().BeOfType<NotFoundObjectResult>(
            "a draft token is not public yet");

        draft.Status = OfferStatus.Expired;
        await db.SaveChangesAsync();
        (await pub.GetOffer(draft.Token)).Should().BeOfType<NotFoundObjectResult>(
            "an expired token behaves exactly like an unknown one");
    }

    [Fact]
    public async Task GetOffer_PublicDto_ContainsNoAdminFields_AndNoCustomerPii()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Big Movers", "big@movers.ee");
        var offer    = await MakeSentOffer(db, lead);
        offer.Options[0].SupplierId = supplier.Id;
        await db.SaveChangesAsync();

        var result = await MakePublic(db, new CapturingEmailQueue()).GetOffer(offer.Token);
        var body   = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var json   = JsonSerializer.Serialize(body);

        // Lead summary the customer submitted themselves is fine…
        json.Should().Contain("Tallinn").And.Contain("moving");
        // …but nothing that identifies them or the admin machinery.
        json.Should().NotContain(offer.Token, "the token must not be echoed in the payload");
        json.Should().NotContain("cust@x.ee").And.NotContain("Mari Maasikas").And.NotContain("+372 5555 1234");
        json.Should().NotContain("ops@ruumly.eu", "CreatedBy is admin metadata");
        json.Should().NotContain("big@movers.ee", "supplier contact details are brokered by the admin");
        json.Should().NotContain("createdBy").And.NotContain("demandLeadId").And.NotContain("supplierId");
        // Provider display name IS part of the public contract.
        json.Should().Contain("Big Movers");
    }

    // ─── Choose ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChooseOption_SetsChosenFields_NotifiesOps_AndIsIdempotent()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();
        var lead  = MakeLead(db);
        var offer = await MakeSentOffer(db, lead);
        var pub   = MakePublic(db, queue);

        var optionA = offer.Options[0].Id;
        var optionB = offer.Options[1].Id;

        (await pub.ChooseOption(offer.Token, new ChooseOptionRequest(optionA)))
            .Should().BeOfType<OkObjectResult>();

        var stored = db.Offers.Single();
        stored.Status.Should().Be(OfferStatus.Chosen);
        stored.ChosenOptionId.Should().Be(optionA);
        stored.ChosenAt.Should().NotBeNull();

        var email = queue.Emails.Should().ContainSingle().Subject;
        email.To.Should().Be("info@ruumly.eu", "ops confirms the match manually");
        email.TextBody.Should().Contain("Option 1");

        // Same option again → 200 (idempotent), no duplicate notification.
        (await pub.ChooseOption(offer.Token, new ChooseOptionRequest(optionA)))
            .Should().BeOfType<OkObjectResult>();
        queue.Emails.Should().ContainSingle();

        // A different option → 409, chosen fields untouched.
        (await pub.ChooseOption(offer.Token, new ChooseOptionRequest(optionB)))
            .Should().BeOfType<ConflictObjectResult>();
        db.Offers.Single().ChosenOptionId.Should().Be(optionA);
    }

    [Fact]
    public async Task ChooseOption_UnknownOptionOrToken_Fails()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var offer = await MakeSentOffer(db, lead);
        var pub   = MakePublic(db, new CapturingEmailQueue());

        (await pub.ChooseOption("does-not-exist", new ChooseOptionRequest(offer.Options[0].Id)))
            .Should().BeOfType<NotFoundObjectResult>();
        (await pub.ChooseOption(offer.Token, new ChooseOptionRequest(Guid.NewGuid())))
            .Should().BeOfType<BadRequestObjectResult>("the option must belong to this offer");

        db.Offers.Single().Status.Should().Be(OfferStatus.Sent);
    }

    // ─── Provider outreach ────────────────────────────────────────────────────

    [Fact]
    public async Task SendOutreach_SkipsEmaillessSuppliers_MovesLeadToContacted_AndHidesCustomerIdentity()
    {
        var db       = TestDbContext.Create();
        var queue    = new CapturingEmailQueue();
        var lead     = MakeLead(db);
        var withMail = MakeSupplier(db, "HasEmail OÜ", "provider@x.ee");
        var noMail   = MakeSupplier(db, "NoEmail OÜ");
        var admin    = MakeAdmin(db, queue);

        var result = await admin.SendOutreach(lead.Id,
            new OutreachRequest([withMail.Id, noMail.Id, Guid.NewGuid()]));
        var body   = result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        var sent    = ((System.Collections.IEnumerable)Prop(body, "sent")!).Cast<object>().ToList();
        var skipped = ((System.Collections.IEnumerable)Prop(body, "skipped")!).Cast<object>().ToList();

        sent.Should().ContainSingle("only the supplier with a ContactEmail gets an email");
        Prop(sent[0], "sentTo").Should().Be("provider@x.ee");
        skipped.Should().HaveCount(2, "the email-less supplier and the unknown id are reported back");
        skipped.Select(s => Prop(s, "reason")).Should().BeEquivalentTo(["no_email", "not_found"]);

        var row = db.ProviderOutreaches.Single();
        row.SupplierId.Should().Be(withMail.Id);
        row.SentTo.Should().Be("provider@x.ee", "the address is snapshotted at send time");
        row.Status.Should().Be(ProviderOutreachStatus.Sent);

        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Contacted,
            "first outreach is the first admin touch");
        db.DemandLeads.Single().ContactedAt.Should().NotBeNull();

        var email = queue.Emails.Should().ContainSingle().Subject;
        email.To.Should().Be("provider@x.ee");
        email.ReplyTo.Should().Be("info@ruumly.eu", "provider replies must land in the ops inbox");
        // Lead facts yes — customer identity never.
        email.TextBody.Should().Contain("Tallinn").And.Contain("2026-08-15");
        email.TextBody.Should().NotContain("cust@x.ee").And.NotContain("Mari Maasikas").And.NotContain("+372 5555 1234");
    }

    [Fact]
    public async Task SendOutreach_LeadPastNew_KeepsItsFunnelStatus()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        lead.Status  = DemandLeadStatus.Quoted;
        await db.SaveChangesAsync();
        var supplier = MakeSupplier(db, "HasEmail OÜ", "provider@x.ee");

        var result = await MakeAdmin(db, new CapturingEmailQueue())
            .SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Quoted,
            "outreach only advances New leads — it never regresses the funnel");
    }

    [Fact]
    public async Task UpdateOutreach_ParsesStatusCaseInsensitively_AndStoresNote()
    {
        var db       = TestDbContext.Create();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "HasEmail OÜ", "provider@x.ee");
        var admin    = MakeAdmin(db, new CapturingEmailQueue());

        await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
        var rowId = db.ProviderOutreaches.Single().Id;

        var result = await admin.UpdateOutreach(rowId, new UpdateOutreachRequest("noanswer", "left voicemail"));
        var body   = result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        Prop(body, "status").Should().Be("noanswer");
        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.NoAnswer);
        db.ProviderOutreaches.Single().Note.Should().Be("left voicemail");

        (await admin.UpdateOutreach(rowId, new UpdateOutreachRequest("bogus")))
            .Should().BeOfType<BadRequestObjectResult>();
    }
}
