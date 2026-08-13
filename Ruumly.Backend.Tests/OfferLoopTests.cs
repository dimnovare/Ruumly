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
        new(db, queue, TestServices.Config(), TestServices.Outreach(db, queue, TestServices.Config()))
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
        DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Quoted);
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
        o.GetType().GetProperties()
            .FirstOrDefault(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.GetValue(o);

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

    [Fact]
    public async Task CreateOffer_ReusesNewestExistingDraft_WithoutMergingRequestOptions()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var admin = MakeAdmin(db, new CapturingEmailQueue());

        var first = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            CustomerNote: "Keep this",
            Options: [new OfferOptionInput("Original")]));
        var second = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            CustomerNote: "Ignore this",
            Options: [new OfferOptionInput("Do not merge")]));

        Prop(first.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")
            .Should().Be(Prop(second.Should().BeOfType<OkObjectResult>().Subject.Value!, "id"));
        var offer = db.Offers.Should().ContainSingle().Subject;
        offer.CustomerNote.Should().Be("Keep this");
        offer.Options.Should().ContainSingle().Which.Title.Should().Be("Original");
    }

    [Fact]
    public async Task DeleteOffer_DeletesDraftAndOptions_ButRejectsSent()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var admin = MakeAdmin(db, new CapturingEmailQueue());

        var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Option")]));
        var id = (Guid)Prop(created.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;

        (await admin.DeleteOffer(id)).Should().BeOfType<NoContentResult>();
        db.Offers.Should().BeEmpty();
        db.OfferOptions.Should().BeEmpty();

        var sent = await MakeSentOffer(db, lead);
        (await admin.DeleteOffer(sent.Id)).Should().BeOfType<ConflictObjectResult>();
        db.Offers.Should().ContainSingle(o => o.Id == sent.Id);
    }

    [Fact]
    public async Task DeliveryPreview_IsSideEffectFree_AndMatchesSendEmailAndPublicProjection()
    {
        var db       = TestDbContext.Create();
        var queue    = new CapturingEmailQueue();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "Preview Provider", "private@provider.ee");
        var admin    = MakeAdmin(db, queue);
        var created  = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            CustomerNote: "Call first",
            Options:
            [
                new OfferOptionInput(
                    "Option", SupplierId: supplier.Id, PriceAmount: 89m, Notes: "Available now"),
            ]));
        var id = (Guid)Prop(created.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;

        var preview = (await admin.GetDeliveryPreview(id))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<OfferDeliveryPreviewDto>().Subject;

        db.Offers.Single().ViewedAt.Should().BeNull();
        db.Offers.Single().Status.Should().Be(OfferStatus.Draft);
        queue.Emails.Should().BeEmpty();

        await admin.SendOffer(id);
        var email = queue.Emails.Should().ContainSingle().Subject;
        preview.Email.Subject.Should().Be(email.Subject);
        preview.Email.TextBody.Should().Be(email.TextBody);

        var publicDto = (await MakePublic(db, new CapturingEmailQueue())
                .GetOffer(db.Offers.Single().Token))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicOfferDto>().Subject;
        preview.Page.Lead.Should().BeEquivalentTo(publicDto.Lead);
        preview.Page.Options.Should().BeEquivalentTo(publicDto.Options, options => options.WithStrictOrdering());
        preview.Page.CustomerNote.Should().Be(publicDto.CustomerNote);

        var pageJson = JsonSerializer.Serialize(preview.Page);
        pageJson.Should().NotContain(db.Offers.Single().Token);
        pageJson.Should().NotContain(lead.Email).And.NotContain(lead.Name!).And.NotContain(lead.Phone!);
        pageJson.Should().NotContain("private@provider.ee").And.NotContain("ops@ruumly.eu");
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
    public async Task UpdateOffer_EchoedIds_KeepTheirRows_WhileDroppedOnesGo()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var admin = MakeAdmin(db, new CapturingEmailQueue());

        var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
            Options: [new OfferOptionInput("Keep me"), new OfferOptionInput("Drop me")]));
        var offerId = (Guid)Prop(created.Should().BeOfType<OkObjectResult>().Subject.Value!, "id")!;
        var keptId  = db.OfferOptions.Single(o => o.Title == "Keep me").Id;
        var goneId  = db.OfferOptions.Single(o => o.Title == "Drop me").Id;

        (await admin.UpdateOffer(offerId, new UpdateOfferRequest(Options:
        [
            // Edited in place — same option, new price.
            new OfferOptionInput("Keep me", PriceAmount: 120m, Id: keptId),
            // No id: a brand-new option the admin just typed.
            new OfferOptionInput("Fresh"),
            // An id from nowhere is not an error the admin can act on — it is
            // simply a new option, and it must never reach across offers.
            new OfferOptionInput("Stranger", Id: Guid.NewGuid()),
        ]))).Should().BeOfType<OkObjectResult>();

        var options = db.OfferOptions.Where(o => o.OfferId == offerId).ToList();
        options.Should().HaveCount(3);
        options.Single(o => o.Title == "Keep me").Id.Should().Be(keptId,
            "an option the payload still carries keeps its row — provenance lives there");
        options.Single(o => o.Title == "Keep me").PriceAmount.Should().Be(120m);
        options.Should().NotContain(o => o.Id == goneId, "membership is still replace-set");
        options.Select(o => o.Id).Should().OnlyHaveUniqueItems();
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
    public async Task ChooseOption_LeavesLeadQuotedUntilAdminConfirms()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var offer = await MakeSentOffer(db, lead);
        var pub   = MakePublic(db, new CapturingEmailQueue());

        (await pub.ChooseOption(offer.Token, new ChooseOptionRequest(offer.Options[0].Id)))
            .Should().BeOfType<OkObjectResult>();

        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Quoted,
            "a customer choice is only a pending preference until provider approval");
        var metricsBody = (await MakeAdminLeads(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;
        Prop(metricsBody, "bookingRate30d").Should().Be(0d);
    }

    [Fact]
    public async Task ConfirmBooking_RequiresChosenOffer_ConvertsOnce_AndCreatesNoCommerceRecords()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db);
        var offer = await MakeSentOffer(db, lead);
        var admin = MakeAdmin(db, new CapturingEmailQueue());

        (await admin.ConfirmBooking(offer.Id))
            .Should().BeOfType<ConflictObjectResult>();

        await MakePublic(db, new CapturingEmailQueue())
            .ChooseOption(offer.Token, new ChooseOptionRequest(offer.Options[0].Id));

        (await admin.ConfirmBooking(offer.Id))
            .Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Converted);
        db.AuditLogs.Should().ContainSingle(a => a.Action == "offer.booking_confirmed");
        db.Bookings.Should().BeEmpty();
        db.Orders.Should().BeEmpty();

        (await admin.ConfirmBooking(offer.Id))
            .Should().BeOfType<OkObjectResult>();
        db.AuditLogs.Should().ContainSingle(a => a.Action == "offer.booking_confirmed");
        db.Bookings.Should().BeEmpty();
        db.Orders.Should().BeEmpty();

        var metricsBody = (await MakeAdminLeads(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;
        Prop(metricsBody, "bookingRate30d").Should().Be(1d,
            "only provider-approved confirmation converts the quoted lead");
    }

    [Fact]
    public async Task ManualLeadPatch_CannotBypassConfirmationOrMutateOtherFields()
    {
        var db = TestDbContext.Create();
        var lead = MakeLead(db);

        var result = await MakeAdminLeads(db).UpdateLead(
            lead.Id,
            new UpdateLeadRequest(Status: "converted", AdminNotes: "changed", Email: "changed@x.ee"));

        result.Should().BeOfType<ConflictObjectResult>();
        var stored = db.DemandLeads.Single();
        stored.Status.Should().Be(DemandLeadStatus.New);
        stored.Email.Should().Be("cust@x.ee");
        stored.AdminNotes.Should().BeNull();
        db.AuditLogs.Should().BeEmpty();
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
    public async Task PreviewOutreach_IsSideEffectFree_AndMatchesDeliveredMessageExceptPerRowQuoteToken()
    {
        var db       = TestDbContext.Create();
        var queue    = new CapturingEmailQueue();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "HasEmail OÜ", "provider@x.ee");
        var admin    = MakeAdmin(db, queue);

        var previewResult = await admin.PreviewOutreach(
            lead.Id, new OutreachPreviewRequest([supplier.Id]));
        var preview = previewResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<OutreachPreviewResponse>().Subject.Recipients
            .Should().ContainSingle().Subject;

        db.ProviderOutreaches.Should().BeEmpty("previewing mints no row and persists no token");
        queue.Emails.Should().BeEmpty();

        await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
        var delivered = queue.Emails.Should().ContainSingle().Subject;

        // Both carry the "Submit your price" quote link; the delivered email's link
        // uses the persisted per-row token, the preview's a throwaway sample token.
        // (The supplier defaults to Estonia, so the link is localized to /et/.)
        preview.TextBody.Should().Contain("https://ruumly.eu/et/quote/");
        delivered.TextBody.Should().Contain(
            "https://ruumly.eu/et/quote/" + db.ProviderOutreaches.Single().QuoteToken,
            "the delivered link uses the token stored on the outreach row");

        static string StripToken(string body) =>
            Regex.Replace(body, "quote/[A-Za-z0-9_-]+", "quote/TOKEN");
        preview.Subject.Should().Be(delivered.Subject);
        StripToken(preview.TextBody!).Should().Be(StripToken(delivered.TextBody),
            "the message is identical apart from the per-recipient token");
    }

    [Fact]
    public async Task PreviewOutreach_ReportsEverySkipReason_AndKeepsTextForExplicitResend()
    {
        var db        = TestDbContext.Create();
        var queue     = new CapturingEmailQueue();
        var lead      = MakeLead(db);
        var contacted = MakeSupplier(db, "Contacted OÜ", "contacted@x.ee");
        var noEmail   = MakeSupplier(db, "NoEmail OÜ");
        var missingId = Guid.NewGuid();
        var admin     = MakeAdmin(db, queue);

        await admin.SendOutreach(lead.Id, new OutreachRequest([contacted.Id]));

        var result = await admin.PreviewOutreach(
            lead.Id, new OutreachPreviewRequest([contacted.Id, noEmail.Id, missingId]));
        var recipients = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<OutreachPreviewResponse>().Subject.Recipients;

        recipients.Should().HaveCount(3);
        var contactedPreview = recipients.Single(r => r.SupplierId == contacted.Id);
        contactedPreview.SkipReason.Should().Be("already_contacted");
        contactedPreview.Subject.Should().NotBeNullOrEmpty();
        contactedPreview.TextBody.Should().NotBeNullOrEmpty(
            "an explicit resend confirmation must display the exact message");
        recipients.Single(r => r.SupplierId == noEmail.Id).SkipReason.Should().Be("no_email");
        var missing = recipients.Single(r => r.SupplierId == missingId);
        missing.SkipReason.Should().Be("not_found");
        missing.Subject.Should().BeNull();
        missing.TextBody.Should().BeNull();
        db.ProviderOutreaches.Should().ContainSingle();
        queue.Emails.Should().ContainSingle();
    }

    [Fact]
    public async Task SendOutreach_DuplicateSkipsUnlessResendIsExplicit()
    {
        var db       = TestDbContext.Create();
        var queue    = new CapturingEmailQueue();
        var lead     = MakeLead(db);
        var supplier = MakeSupplier(db, "HasEmail OÜ", "provider@x.ee");
        var admin    = MakeAdmin(db, queue);

        await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
        var duplicate = await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
        var duplicateBody = duplicate.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var skipped = ((System.Collections.IEnumerable)Prop(duplicateBody, "skipped")!)
            .Cast<object>().Should().ContainSingle().Subject;
        Prop(skipped, "reason").Should().Be("already_contacted");
        db.ProviderOutreaches.Should().ContainSingle();
        queue.Emails.Should().ContainSingle();

        await admin.SendOutreach(
            lead.Id, new OutreachRequest([supplier.Id], Resend: true));
        db.ProviderOutreaches.Should().HaveCount(2);
        queue.Emails.Should().HaveCount(2);
    }

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
