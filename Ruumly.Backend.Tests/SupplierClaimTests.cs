using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The public "claim your profile" flow (2026-08). The introduction campaign
/// tells ~650 researched directory providers they can claim their profile to
/// correct their details; these tests are what stop that sentence from being a
/// lie again, and they pin the two properties the flow exists to guarantee:
/// the stored contact email is never disclosed to an unauthenticated visitor,
/// and a claim session can only ever write to its own supplier.
/// </summary>
public class SupplierClaimTests
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

    private sealed class NoOpCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }

    private static ClaimController MakeController(
        RuumlyDbContext db, IBackgroundEmailQueue queue, string? sessionToken = null)
    {
        var http = new DefaultHttpContext();
        if (sessionToken is not null)
            http.Request.Headers[ClaimController.SessionHeader] = sessionToken;

        return new ClaimController(
            db, queue, TestServices.Config(), new NoOpCache(),
            NullLogger<ClaimController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static Supplier MakeDirectorySupplier(
        RuumlyDbContext db,
        string name = "Kolimisabi OÜ",
        string slug = "kolimisabi",
        string contactEmail = "info@kolimisabi.ee",
        string country = "EE",
        bool published = true,
        bool directory = true)
    {
        var supplier = new Supplier
        {
            Id                     = Guid.NewGuid(),
            Name                   = name,
            Slug                   = slug,
            ContactName            = name,
            ContactEmail           = contactEmail,
            ContactPhone           = "+372 5555 0000",
            Country                = country,
            IsActive               = true,
            IsPartnerPagePublished = published,
            IsDirectoryListing     = directory,
            ServiceTypesJson       = JsonSerializer.Serialize(new List<string> { "moving" }),
        };
        db.Suppliers.Add(supplier);
        db.SupplierLocations.Add(new SupplierLocation
        {
            Id          = Guid.NewGuid(),
            SupplierId  = supplier.Id,
            Name        = name,
            Address     = "Vana tee 1",
            City        = "Tallinn",
            Country     = country,
            Lat         = 59.43,
            Lng         = 24.75,
            IsActive    = true,
            IsSynthetic = false,
            Description = string.Empty,
        });
        db.SaveChanges();
        return supplier;
    }

    /// <summary>Requests a link, redeems it, and returns the live claim session token.</summary>
    private static async Task<string> ClaimAsync(
        RuumlyDbContext db, Supplier supplier, IBackgroundEmailQueue queue)
    {
        await MakeController(db, queue).RequestLink(
            supplier.Slug!, new ClaimRequestLinkRequest(supplier.ContactEmail), default);

        // The raw token lives only in the email; the test recovers it the same
        // way the provider does — by reading the link out of the message.
        var claim = db.SupplierClaims.Single(
            c => c.SupplierId == supplier.Id && c.TokenHash != null && c.VerifiedAt == null);
        var raw   = RawTokenFor(claim, queue);

        var verified = await MakeController(db, queue).Verify(new ClaimVerifyRequest(raw), default);
        return verified.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<ClaimSessionDto>().Subject.SessionToken;
    }

    /// <summary>
    /// Pulls the raw token out of the sent email and asserts it hashes to what
    /// was stored — proof the database holds no usable credential.
    /// </summary>
    private static string RawTokenFor(SupplierClaim claim, IBackgroundEmailQueue queue)
    {
        var emails = ((CapturingEmailQueue)queue).Emails;
        var body   = emails.Last(e => e.TextBody.Contains("token=")).TextBody;
        var raw    = body.Split("token=")[1].Split('\n')[0].Trim();
        ClaimToken.Hash(raw).Should().Be(claim.TokenHash);
        return raw;
    }

    private static List<(string To, string Subject, string TextBody, string? HtmlBody)> Sent(
        IBackgroundEmailQueue queue) => ((CapturingEmailQueue)queue).Emails;

    // ─── What an unverified visitor may learn ─────────────────────────────────

    [Fact]
    public async Task GetClaimProfile_ReturnsPublicFactsOnly_NeverTheStoredContactEmail()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);

        var result = await MakeController(db, new CapturingEmailQueue())
            .GetClaimProfile(supplier.Slug!, default);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PublicClaimProfileDto>().Subject;
        dto.Name.Should().Be("Kolimisabi OÜ");
        dto.City.Should().Be("Tallinn");
        dto.ServiceTypes.Should().Equal("moving");
        dto.IsClaimed.Should().BeFalse();

        // The whole serialized payload must not contain the address or any part
        // of it — not the full string, not the local part, not a masked hint.
        var json = JsonSerializer.Serialize(dto);
        json.Should().NotContain("kolimisabi.ee");
        json.Should().NotContain("info@");
        json.Should().NotContain("5555");
    }

    [Fact]
    public async Task RequestLink_AnswersIdenticallyWhetherOrNotTheAddressIsOnFile()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);

        var hit = await MakeController(db, new CapturingEmailQueue())
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest("info@kolimisabi.ee"), default);
        var miss = await MakeController(db, new CapturingEmailQueue())
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest("attacker@evil.example"), default);

        var hitBody  = hit.Should().BeOfType<AcceptedResult>().Subject;
        var missBody = miss.Should().BeOfType<AcceptedResult>().Subject;

        // Same status code AND byte-identical body: this endpoint must not be an
        // oracle for whether an address is on file.
        hitBody.StatusCode.Should().Be(missBody.StatusCode);
        JsonSerializer.Serialize(hitBody.Value)
            .Should().Be(JsonSerializer.Serialize(missBody.Value));
    }

    [Fact]
    public async Task RequestLink_WrongAddress_SendsNoLinkAndAlertsOpsInstead()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        await MakeController(db, queue)
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest("attacker@evil.example"), default);

        // Nothing was sent to the address the visitor typed, and no token exists.
        Sent(queue).Should().NotContain(e => e.To == "attacker@evil.example");
        db.SupplierClaims.Single().TokenHash.Should().BeNull();
        db.SupplierClaims.Single().EmailMatched.Should().BeFalse();

        var ops = Sent(queue).Single();
        ops.To.Should().Be(OpsInbox.Fallback);
        ops.Subject.Should().Contain("needs review");
        ops.TextBody.Should().Contain("attacker@evil.example");
    }

    [Fact]
    public async Task RequestLink_MatchingAddress_MailsTheStoredAddressAndNowhereElse()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        // Typed in a different case than stored — matching is case-insensitive.
        await MakeController(db, queue)
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest("  INFO@Kolimisabi.EE  "), default);

        Sent(queue).Should().ContainSingle();
        var mail = Sent(queue).Single();
        mail.To.Should().Be("info@kolimisabi.ee");
        mail.TextBody.Should().Contain($"/et/claim/{supplier.Slug}?token=");

        // Stored hashed, never raw: the emailed token must not appear in the DB.
        var claim = db.SupplierClaims.Single();
        claim.EmailMatched.Should().BeTrue();
        claim.TokenHash.Should().NotBeNullOrEmpty();
        mail.TextBody.Should().NotContain(claim.TokenHash!);
        claim.TokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RequestLink_UsesTheSuppliersOwnCountryLanguageWhenThePageDoesNotSayOtherwise()
    {
        var db    = TestDbContext.Create();
        var latvian = MakeDirectorySupplier(
            db, "Pārvākšanās SIA", "parvaksanas", "info@parvaksanas.lv", country: "LV");
        var queue = new CapturingEmailQueue();

        await MakeController(db, queue)
            .RequestLink(latvian.Slug!, new ClaimRequestLinkRequest(latvian.ContactEmail), default);

        // Most claimants are LV/LT — a missing language must never fall back to
        // Estonian for a Latvian provider.
        Sent(queue).Single().TextBody.Should().Contain("/lv/claim/parvaksanas?token=");
        Sent(queue).Single().Subject.Should().Be(EmailTranslations.For("lv").ClaimSubject);
    }

    [Fact]
    public async Task RequestLink_RejectsMalformedEmailWithoutTouchingAnything()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        var result = await MakeController(db, queue)
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest("not-an-email"), default);

        result.Should().BeOfType<BadRequestObjectResult>();
        db.SupplierClaims.Should().BeEmpty();
        Sent(queue).Should().BeEmpty();
    }

    [Fact]
    public async Task NonDirectoryAndUnpublishedProfiles_AreNotClaimable()
    {
        var db = TestDbContext.Create();
        // A real partner with an account is edited by logging in — this flow must
        // never become a second, weaker door into their profile.
        var partner = MakeDirectorySupplier(db, "Real Partner OÜ", "real-partner", "a@x.ee", directory: false);
        var draft   = MakeDirectorySupplier(db, "Draft OÜ", "draft-oy", "b@x.ee", published: false);

        (await MakeController(db, new CapturingEmailQueue()).GetClaimProfile(partner.Slug!, default))
            .Should().BeOfType<NotFoundObjectResult>();
        (await MakeController(db, new CapturingEmailQueue()).GetClaimProfile(draft.Slug!, default))
            .Should().BeOfType<NotFoundObjectResult>();
        (await MakeController(db, new CapturingEmailQueue())
            .RequestLink(partner.Slug!, new ClaimRequestLinkRequest("a@x.ee"), default))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── Token hygiene ────────────────────────────────────────────────────────

    [Fact]
    public async Task MagicLink_IsSingleUse()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        await MakeController(db, queue)
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest(supplier.ContactEmail), default);
        var raw = RawTokenFor(db.SupplierClaims.Single(), queue);

        (await MakeController(db, queue).Verify(new ClaimVerifyRequest(raw), default))
            .Should().BeOfType<OkObjectResult>();
        // Replay of the same URL is indistinguishable from an unknown token.
        (await MakeController(db, queue).Verify(new ClaimVerifyRequest(raw), default))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task MagicLink_ExpiresAfterTwentyFourHours()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        await MakeController(db, queue)
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest(supplier.ContactEmail), default);
        var claim = db.SupplierClaims.Single();
        var raw   = RawTokenFor(claim, queue);

        claim.TokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        db.SaveChanges();

        (await MakeController(db, queue).Verify(new ClaimVerifyRequest(raw), default))
            .Should().BeOfType<NotFoundObjectResult>();
        db.Suppliers.Single(s => s.Id == supplier.Id).ClaimedAt.Should().BeNull();
    }

    [Fact]
    public async Task RequestingANewLink_RetiresTheOutstandingOne()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        await MakeController(db, queue)
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest(supplier.ContactEmail), default);
        var firstRaw = RawTokenFor(db.SupplierClaims.Single(c => c.TokenHash != null), queue);

        await MakeController(db, queue)
            .RequestLink(supplier.Slug!, new ClaimRequestLinkRequest(supplier.ContactEmail), default);

        // Only the newest link works — one left in a shared inbox goes dead as
        // soon as the owner asks for another.
        (await MakeController(db, queue).Verify(new ClaimVerifyRequest(firstRaw), default))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Verify_StampsClaimedAtAndWho_AndKeepsTheFirstTimestampOnReclaim()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        await ClaimAsync(db, supplier, queue);
        var claimed = db.Suppliers.Single(s => s.Id == supplier.Id);
        claimed.ClaimedAt.Should().NotBeNull();
        claimed.ClaimedByEmail.Should().Be("info@kolimisabi.ee");
        var firstClaimedAt = claimed.ClaimedAt;

        Sent(queue).Should().Contain(e => e.Subject.Contains("profile claimed"));

        await ClaimAsync(db, supplier, queue);
        // The uptake metric measures FIRST response — a returning owner must not
        // reset it.
        db.Suppliers.Single(s => s.Id == supplier.Id).ClaimedAt.Should().Be(firstClaimedAt);
    }

    // ─── Scoping: a session can only ever touch its own supplier ──────────────

    [Fact]
    public async Task ClaimSessionForSupplierA_CannotReadOrWriteSupplierB()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();
        var a = MakeDirectorySupplier(db, "Alpha OÜ", "alpha-oy", "a@alpha.ee");
        var b = MakeDirectorySupplier(db, "Beta OÜ", "beta-oy", "b@beta.ee");

        var sessionA = await ClaimAsync(db, a, queue);

        var read = await MakeController(db, queue, sessionA).GetEditableProfile(b.Slug!, default);
        read.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var write = await MakeController(db, queue, sessionA).UpdateProfile(
            b.Slug!,
            new ClaimUpdateProfileRequest("Hijacked", "+372 1", "x@x.ee", null, "Elsewhere 9", ["moving"], null),
            default);
        write.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // Neither row moved: not B (the target) and not A (no silent redirect).
        db.Suppliers.Single(s => s.Id == b.Id).Name.Should().Be("Beta OÜ");
        db.Suppliers.Single(s => s.Id == b.Id).ContactEmail.Should().Be("b@beta.ee");
        db.Suppliers.Single(s => s.Id == a.Id).Name.Should().Be("Alpha OÜ");
        db.SupplierLocations.Single(l => l.SupplierId == b.Id).Address.Should().Be("Vana tee 1");
    }

    [Fact]
    public async Task EditEndpoints_RejectMissingAndExpiredSessions()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();

        (await MakeController(db, queue).GetEditableProfile(supplier.Slug!, default))
            .Should().BeOfType<UnauthorizedObjectResult>();
        (await MakeController(db, queue, "not-a-real-session").GetEditableProfile(supplier.Slug!, default))
            .Should().BeOfType<UnauthorizedObjectResult>();

        var session = await ClaimAsync(db, supplier, queue);
        var claim = db.SupplierClaims.Single(c => c.SessionTokenHash != null);
        claim.SessionExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        db.SaveChanges();

        (await MakeController(db, queue, session).GetEditableProfile(supplier.Slug!, default))
            .Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ─── What a claimed provider may edit ─────────────────────────────────────

    [Fact]
    public async Task ClaimedProvider_EditsTheSmallAllowedSet_AndNothingElse()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();
        // Fields the claimant must NOT be able to move.
        supplier.Tier                   = Ruumly.Backend.Models.Enums.SupplierTier.Premium;
        supplier.IsVerified             = true;
        supplier.PartnerDiscountRate    = 0.15m;
        supplier.IsPartnerPagePublished = true;
        db.SaveChanges();

        var session = await ClaimAsync(db, supplier, queue);

        var result = await MakeController(db, queue, session).UpdateProfile(
            supplier.Slug!,
            new ClaimUpdateProfileRequest(
                Name:         "Kolimisabi OÜ (uus nimi)",
                ContactPhone: "+372 5111 2222",
                ContactEmail: "kontakt@kolimisabi.ee",
                WebsiteUrl:   "https://kolimisabi.ee",
                Address:      "Uus tee 5, Tallinn",
                ServiceTypes: ["moving", "cleaning"],
                Description:  "Kolime kodusid ja kontoreid Tallinnas."),
            default);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<ClaimEditableProfileDto>().Subject;
        dto.Name.Should().Be("Kolimisabi OÜ (uus nimi)");
        dto.Address.Should().Be("Uus tee 5, Tallinn");

        var saved = db.Suppliers.Single(s => s.Id == supplier.Id);
        saved.Name.Should().Be("Kolimisabi OÜ (uus nimi)");
        saved.ContactPhone.Should().Be("+372 5111 2222");
        saved.ContactEmail.Should().Be("kontakt@kolimisabi.ee");
        saved.WebsiteUrl.Should().Be("https://kolimisabi.ee");
        saved.Tagline.Should().Be("Kolime kodusid ja kontoreid Tallinnas.");
        JsonSerializer.Deserialize<List<string>>(saved.ServiceTypesJson!)
            .Should().Equal("moving", "cleaning");
        db.SupplierLocations.Single(l => l.SupplierId == supplier.Id)
            .Address.Should().Be("Uus tee 5, Tallinn");

        // A claim proves control of an inbox — nothing more.
        saved.Tier.Should().Be(Ruumly.Backend.Models.Enums.SupplierTier.Premium);
        saved.IsVerified.Should().BeTrue();
        saved.PartnerDiscountRate.Should().Be(0.15m);
        saved.Slug.Should().Be("kolimisabi");
        saved.IsDirectoryListing.Should().BeTrue();
    }

    [Fact]
    public async Task ClaimedProvider_CannotSaveMarkupBlankEmailOrUnknownServices()
    {
        var db       = TestDbContext.Create();
        var supplier = MakeDirectorySupplier(db);
        var queue    = new CapturingEmailQueue();
        var session  = await ClaimAsync(db, supplier, queue);

        var valid = new ClaimUpdateProfileRequest(
            "Kolimisabi OÜ", "+372 5", "info@kolimisabi.ee", null, "Vana tee 1", ["moving"], "Ok");

        // Provider-authored text renders on public pages, so it gets the same
        // anti-markup hardening the admin bulk import applies to scraped rows.
        var withMarkup = valid with { Description = "<script>alert(1)</script>" };
        var blankEmail = valid with { ContactEmail = "   " };
        var badService = valid with { ServiceTypes = ["moving", "teleportation"] };
        var noService  = valid with { ServiceTypes = [] };
        var badSite    = valid with { WebsiteUrl = "javascript:alert(1)" };

        foreach (var bad in new[] { withMarkup, blankEmail, badService, noService, badSite })
        {
            (await MakeController(db, queue, session).UpdateProfile(supplier.Slug!, bad, default))
                .Should().BeOfType<BadRequestObjectResult>();
        }

        var saved = db.Suppliers.Single(s => s.Id == supplier.Id);
        saved.Name.Should().Be("Kolimisabi OÜ");
        saved.ContactEmail.Should().Be("info@kolimisabi.ee");
        saved.Tagline.Should().BeNull();
    }

    // ─── The intro campaign now points at a route that exists ─────────────────

    [Fact]
    public void IntroCampaign_LinksToTheClaimRouteInEveryLanguage()
    {
        foreach (var (country, lang) in new[] { ("EE", "et"), ("LV", "lv"), ("LT", "lt"), ("FI", "en") })
        {
            var supplier = new Supplier
            {
                Name = "X OÜ", Slug = "x-oy", Country = country,
                IsPartnerPagePublished = true, IsDirectoryListing = true,
            };
            var message = SupplierIntroComposer.Compose(supplier, "https://ruumly.eu");

            message.Language.Should().Be(lang);
            message.TextBody.Should().Contain($"https://ruumly.eu/{lang}/claim/x-oy");
            message.TextBody.Should().NotContain("/partner/x-oy");
            message.HtmlBody.Should().Contain($"https://ruumly.eu/{lang}/claim/x-oy");
        }
    }

    [Fact]
    public void IntroCampaign_FallsBackToMailtoWhenTheClaimPageWouldNotResolve()
    {
        // A cold email must never carry a link that 404s: the guard has to match
        // the claim endpoint's own eligibility exactly.
        var notDirectory = new Supplier
        {
            Name = "X OÜ", Slug = "x-oy", Country = "EE",
            IsPartnerPagePublished = true, IsDirectoryListing = false,
        };
        SupplierIntroComposer.ClaimPageUrl(notDirectory, "https://ruumly.eu", "et").Should().BeNull();
        SupplierIntroComposer.Compose(notDirectory, "https://ruumly.eu")
            .TextBody.Should().NotContain("/claim/");
    }

    /// <summary>
    /// Golden copy of the two supplier-facing "questions?" lines. The founder
    /// retired the support phone in 2026-08 and does not take provider calls:
    /// the only channels are a reply and the contact page. Pinned verbatim in
    /// all five languages so a well-meaning edit cannot quietly reintroduce a
    /// number, a WhatsApp invitation or an untranslated English fallback.
    /// </summary>
    [Fact]
    public void IntroAndOutreachCopy_OfferOnlyReplyAndTheContactPage()
    {
        // Shortened in the 2026-08 intro rewrite. What this test actually guards is
        // unchanged and is the reason it pins whole strings: the only two channels
        // ever offered are a reply and the contact page. No phone number, no form,
        // no third route. The wording may move; that invariant may not.
        var intro = new Dictionary<string, string>
        {
            ["et"] = "Kui teil tekib küsimusi Ruumly, koostöö või päringute kohta, vastake lihtsalt sellele kirjale või kirjutage siit: https://ruumly.eu/et/contact",
            ["en"] = "Questions about Ruumly, working together or the requests? Reply to this email, or write to us here: https://ruumly.eu/en/contact",
            ["ru"] = "Если возникнут вопросы о Ruumly, сотрудничестве или заявках, просто ответьте на это письмо или напишите нам здесь: https://ruumly.eu/ru/contact",
            ["lv"] = "Ja rodas jautājumi par Ruumly, sadarbību vai pieprasījumiem, vienkārši atbildiet uz šo vēstuli vai rakstiet mums šeit: https://ruumly.eu/lv/contact",
            ["lt"] = "Jei kiltų klausimų apie Ruumly, bendradarbiavimą ar užklausas, tiesiog atsakykite į šį laišką arba parašykite mums čia: https://ruumly.eu/lt/contact",
        };

        var outreach = new Dictionary<string, string>
        {
            ["et"] = "Küsimused? Vastake sellele kirjale või kirjutage meile kontaktivormi kaudu: https://ruumly.eu/et/contact",
            ["en"] = "Questions? Reply to this email, or write to us through our contact page: https://ruumly.eu/en/contact",
            ["ru"] = "Вопросы? Ответьте на это письмо или напишите нам через форму на странице контактов: https://ruumly.eu/ru/contact",
            ["lv"] = "Jautājumi? Atbildiet uz šo e-pastu vai rakstiet mums, izmantojot kontaktu lapu: https://ruumly.eu/lv/contact",
            ["lt"] = "Klausimai? Atsakykite į šį laišką arba parašykite mums per kontaktų puslapį: https://ruumly.eu/lt/contact",
        };

        foreach (var (lang, line) in intro)
        {
            var contact = FrontendUrl.Contact("https://ruumly.eu", lang);
            EmailTranslations.For(lang).IntroQuestions(contact).Should().Be(line);
        }

        foreach (var (lang, line) in outreach)
        {
            var contact = FrontendUrl.Contact("https://ruumly.eu", lang);
            EmailTranslations.For(lang).OutreachQuestions(contact).Should().Be(line);
        }
    }

    [Fact]
    public void ClaimEmail_ReadsNativelyInAllFiveLanguages()
    {
        foreach (var lang in new[] { "et", "en", "ru", "lv", "lt" })
        {
            var t = EmailTranslations.For(lang);
            var message = SupplierClaimComposer.Compose(
                lang, "Kolimisabi OÜ", "https://ruumly.eu/et/claim/kolimisabi?token=abc", 24);

            message.Subject.Should().Be(t.ClaimSubject).And.NotBeNullOrWhiteSpace();
            message.TextBody.Should().Contain("Kolimisabi OÜ");
            message.TextBody.Should().Contain("https://ruumly.eu/et/claim/kolimisabi?token=abc");
            // Expiry and "you didn't ask for this" must be present in every
            // language — this is a cold recipient's only reassurance.
            message.TextBody.Should().Contain("24");
            message.TextBody.Should().Contain(t.ClaimIgnore(OpsInbox.Fallback));
            message.HtmlBody.Should().Contain(t.ClaimCta);
        }
    }
}
