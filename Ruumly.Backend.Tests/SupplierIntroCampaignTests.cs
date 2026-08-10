using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// The one-off supplier INTRODUCTION campaign. Two properties dominate: it
/// sends NOTHING unless explicitly asked (dryRun defaults to true), and it
/// never sends twice to the same supplier — a duplicate blast to 435 cold
/// businesses is unrecoverable. Everything else here guards the copy itself.
/// </summary>
public class SupplierIntroCampaignTests
{
    private static readonly string[] AllLanguages = ["et", "en", "ru", "lv", "lt"];

    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(TimeSpan Delay, string To, string Subject, string TextBody, string? HtmlBody, string? ReplyTo)>
            Emails { get; } = [];

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((TimeSpan.Zero, to, subject, textBody, htmlBody, null));

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((TimeSpan.Zero, to, subject, textBody, htmlBody, replyTo));

        public void EnqueueEmailAfter(
            TimeSpan delay, string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((delay, to, subject, textBody, htmlBody, replyTo));

        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static IConfiguration Config(
        string? appUrl = "https://ruumly.eu",
        string? fromName = null,
        string? fromAddress = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppUrl"]             = appUrl,
                ["Email:FromName"]     = fromName,
                ["Email:FromAddress"]  = fromAddress,
            })
            .Build();

    /// <summary>
    /// Stands in for the DNS sweep. Default behaviour is "every domain can receive
    /// mail", so the existing tests keep describing what they were written to
    /// describe; the deliverability tests below pass the dead domains explicitly.
    /// It also RECORDS what it was asked, which is how we assert that suppliers who
    /// would never be mailed never cost a lookup.
    /// </summary>
    private sealed class FakeMailDomains(params string[] undeliverable) : IMailDomainVerifier
    {
        private readonly HashSet<string> dead = new(undeliverable, StringComparer.OrdinalIgnoreCase);

        public List<string> Asked { get; } = [];

        public Task<IReadOnlySet<string>> FindUndeliverableAsync(
            IEnumerable<string> domains, CancellationToken ct = default)
        {
            var seen = domains.ToList();
            Asked.AddRange(seen);
            return Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(seen.Where(dead.Contains), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static AdminSupplierIntroController Make(
        RuumlyDbContext db,
        IBackgroundEmailQueue queue,
        IConfiguration? config = null,
        IMailDomainVerifier? mailDomains = null) =>
        new(db, queue, mailDomains ?? new FakeMailDomains(),
            config ?? Config(), NullLogger<AdminSupplierIntroController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Email, "admin@ruumly.eu"),
                        new Claim(ClaimTypes.Role, "Admin"),
                    ], "test")),
                },
            },
        };

    private static Supplier Provider(
        string name = "Kolimisabi OÜ",
        string country = "EE",
        string? email = "info@kolimisabi.ee",
        bool active = true,
        bool directory = true,
        string? slug = "kolimisabi-ou",
        bool published = true,
        DateTime? introSentAt = null) => new()
    {
        Id                     = Guid.NewGuid(),
        Name                   = name,
        ContactName            = name,
        ContactEmail           = email ?? "",
        ContactPhone           = "+372 5555 0000",
        Country                = country,
        IsActive               = active,
        IsDirectoryListing     = directory,
        Slug                   = slug,
        IsPartnerPagePublished = published,
        IntroEmailSentAt       = introSentAt,
    };

    private static SupplierIntroCampaignResponse Body(IActionResult result) =>
        (SupplierIntroCampaignResponse)((OkObjectResult)result).Value!;

    private static async Task<RuumlyDbContext> DbWith(params Supplier[] suppliers)
    {
        var db = TestDbContext.Create();
        db.Suppliers.AddRange(suppliers);
        await db.SaveChangesAsync();
        return db;
    }

    // ─── Dry run is the default, and it sends nothing ─────────────────────────

    [Fact]
    public async Task DryRunIsTheDefault_SendsNothing_AndReturnsRenderedCopy()
    {
        var db    = await DbWith(Provider());
        var queue = new CapturingEmailQueue();

        // No body at all — the most likely way to fire this by accident.
        var response = Body(await Make(db, queue).RunIntroCampaign(null, default));

        response.DryRun.Should().BeTrue("omitting the body must never mean 'send it'");
        response.Sent.Should().Be(0);
        queue.Emails.Should().BeEmpty("a dry run must not queue a single email");

        response.WouldSend.Should().Be(1);
        response.Samples.Should().ContainSingle();
        var sample = response.Samples[0];
        sample.Subject.Should().NotBeNullOrWhiteSpace();
        sample.TextBody.Should().NotBeNullOrWhiteSpace();
        sample.HtmlBody.Should().NotBeNullOrWhiteSpace();
        sample.To.Should().Be("info@kolimisabi.ee");

        // Nothing was stamped either — a dry run must be repeatable.
        (await db.Suppliers.SingleAsync()).IntroEmailSentAt.Should().BeNull();
    }

    [Fact]
    public async Task DryRun_WithExplicitNullDryRunField_StillSendsNothing()
    {
        var db    = await DbWith(Provider());
        var queue = new CapturingEmailQueue();

        var response = Body(await Make(db, queue)
            .RunIntroCampaign(new SupplierIntroCampaignRequest(DryRun: null), default));

        response.DryRun.Should().BeTrue();
        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_ReportsBreakdownByCountryAndLanguage_AndOneSamplePerLanguage()
    {
        var db = await DbWith(
            Provider("EE One", "EE", "a@x.ee",  slug: "ee-one"),
            Provider("EE Two", "EE", "b@x.ee",  slug: "ee-two"),
            Provider("LV One", "LV", "c@x.lv",  slug: "lv-one"),
            Provider("LT One", "LT", "d@x.lt",  slug: "lt-one"),
            Provider("FI One", "FI", "e@x.fi",  slug: "fi-one"));

        var response = Body(await Make(db, new CapturingEmailQueue()).RunIntroCampaign(null, default));

        response.Matched.Should().Be(5);
        response.WouldSend.Should().Be(5);
        response.ByCountry.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["EE"] = 2, ["LV"] = 1, ["LT"] = 1, ["FI"] = 1,
        });
        response.ByLanguage.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["et"] = 2, ["lv"] = 1, ["lt"] = 1, ["en"] = 1,
        });

        response.Samples.Select(s => s.Language).Should().BeEquivalentTo(
            ["en", "et", "lt", "lv"],
            "the founder reviews one fully rendered email per language, not one per supplier");
    }

    // ─── Skips ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuppliersWithoutAnEmail_AreSkipped()
    {
        var db = await DbWith(
            Provider("No Email",   email: null,   slug: "no-email"),
            Provider("Blank Email", email: "   ", slug: "blank-email"),
            Provider("Junk Email", email: "not-an-address", slug: "junk-email"),
            Provider("Good",       email: "good@x.ee",      slug: "good"));

        var response = Body(await Make(db, new CapturingEmailQueue()).RunIntroCampaign(null, default));

        response.WouldSend.Should().Be(1);
        response.Samples.Should().ContainSingle().Which.To.Should().Be("good@x.ee");
        Reason(response, "no_email").Should().Be(2);
        Reason(response, "invalid_email").Should().Be(1);
    }

    [Fact]
    public async Task InactiveAndNonDirectorySuppliers_AreSkipped()
    {
        var db = await DbWith(
            Provider("Inactive",     active: false,   slug: "inactive"),
            Provider("Signed Up",    directory: false, slug: "signed-up"),
            Provider("Directory",    slug: "directory"));

        var response = Body(await Make(db, new CapturingEmailQueue()).RunIntroCampaign(null, default));

        response.WouldSend.Should().Be(1);
        Reason(response, "inactive").Should().Be(1);
        Reason(response, "not_directory").Should().Be(1);
    }

    [Fact]
    public async Task DuplicateContactEmail_IsOnlyMailedOnce()
    {
        // Scraped directory rows share addresses (franchises, one info@ for
        // several brands) — one inbox must not receive the intro twice.
        var db = await DbWith(
            Provider("Brand A", email: "info@group.ee", slug: "brand-a"),
            Provider("Brand B", email: "INFO@group.ee", slug: "brand-b"));

        var response = Body(await Make(db, new CapturingEmailQueue()).RunIntroCampaign(null, default));

        response.WouldSend.Should().Be(1);
        Reason(response, "duplicate_email").Should().Be(1);
    }

    // ─── Deliverability ───────────────────────────────────────────────────────
    // A syntactically perfect address on a domain with no MX bounces, and bounces
    // cost sender reputation. The 2026-08-09 audit found five such addresses live
    // in the directory; this campaign is the largest send the domain has ever made.

    [Fact]
    public async Task AnAddressOnADomainWithNoMx_IsSkipped_NotMailed()
    {
        var db = await DbWith(
            Provider("Dead Domain", email: "info@kapsel24.ee", slug: "kapsel-minilaod"),
            Provider("Live Domain", email: "info@kolimisabi.ee", slug: "live"));

        var response = Body(await Make(db, new CapturingEmailQueue(),
            mailDomains: new FakeMailDomains("kapsel24.ee")).RunIntroCampaign(null, default));

        response.WouldSend.Should().Be(1);
        response.Samples.Should().ContainSingle().Which.To.Should().Be("info@kolimisabi.ee");
        Reason(response, "undeliverable_domain").Should().Be(1);
    }

    [Fact]
    public async Task UndeliverableIsReportedAheadOfDuplicate_SoTheRealProblemIsVisible()
    {
        // Two branches sharing one dead inbox must both report the dead domain.
        // Reporting the second as `duplicate_email` would hide the actual defect.
        var db = await DbWith(
            Provider("Branch A", email: "info@t49.ee", slug: "branch-a"),
            Provider("Branch B", email: "info@t49.ee", slug: "branch-b"));

        var response = Body(await Make(db, new CapturingEmailQueue(),
            mailDomains: new FakeMailDomains("t49.ee")).RunIntroCampaign(null, default));

        response.WouldSend.Should().Be(0);
        Reason(response, "undeliverable_domain").Should().Be(2);
        Reason(response, "duplicate_email").Should().Be(0);
    }

    [Fact]
    public async Task LiveSend_NeverQueuesMailToAnUndeliverableDomain()
    {
        var db    = await DbWith(
            Provider("Dead", email: "esvo@esvo.ee",      slug: "esvo"),
            Provider("Live", email: "hello@kolimine.ee", slug: "live"));
        var queue = new CapturingEmailQueue();

        var response = Body(await Make(db, queue, mailDomains: new FakeMailDomains("esvo.ee"))
            .RunIntroCampaign(new SupplierIntroCampaignRequest(DryRun: false), default));

        response.Sent.Should().Be(1);
        queue.Emails.Should().ContainSingle().Which.To.Should().Be("hello@kolimine.ee");

        // The skipped supplier must NOT be stamped — the address may be repaired
        // later, and a stamp would permanently exclude it from the one campaign
        // that ever runs.
        var dead = await db.Suppliers.SingleAsync(s => s.Slug == "esvo");
        dead.IntroEmailSentAt.Should().BeNull();
    }

    [Fact]
    public async Task OnlySuppliersThatWouldActuallyBeMailed_CostADnsLookup()
    {
        var db = await DbWith(
            Provider("Mailable",  email: "a@good.ee",     slug: "mailable"),
            Provider("Inactive",  email: "b@inactive.ee", slug: "inactive",  active: false),
            Provider("NonDir",    email: "c@nondir.ee",   slug: "nondir",    directory: false),
            Provider("Sent",      email: "d@sent.ee",     slug: "sent",      introSentAt: DateTime.UtcNow),
            Provider("NoEmail",   email: null,            slug: "no-email"),
            Provider("Junk",      email: "not-an-address", slug: "junk"));

        var fake = new FakeMailDomains();
        await Make(db, new CapturingEmailQueue(), mailDomains: fake).RunIntroCampaign(null, default);

        fake.Asked.Should().BeEquivalentTo(["good.ee"],
            "resolving domains for suppliers that are skipped anyway is wasted latency " +
            "on every preview, against ~500 domains");
    }

    [Fact]
    public async Task ADomainTheResolverCannotAnswerFor_IsStillMailed()
    {
        // Failing open is deliberate: an unreachable resolver is not evidence
        // against a provider, and failing the other way would silently shrink the
        // campaign whenever DNS hiccups.
        var db = await DbWith(Provider(email: "info@unknown.ee", slug: "unknown"));

        var response = Body(await Make(db, new CapturingEmailQueue(),
            mailDomains: new FakeMailDomains()).RunIntroCampaign(null, default));

        response.WouldSend.Should().Be(1);
        Reason(response, "undeliverable_domain").Should().Be(0);
    }

    [Fact]
    public async Task TheFiveAddressesTheAuditFound_WouldAllBeHeldBack()
    {
        // Regression guard tied to docs/research/partners-2026-08-08/email-mx-audit.json.
        // These are cleared on the live directory now, but the campaign must refuse
        // them on its own rather than relying on that cleanup having happened.
        var db = await DbWith(
            Provider("Kapsel Minilaod",  "EE", "info@kapsel24.ee",   slug: "kapsel-minilaod"),
            Provider("Esvo Transport",   "EE", "esvo@esvo.ee",       slug: "esvo-transport-turi"),
            Provider("Noortegija",       "EE", "info@noortegija.ee", slug: "noortegija"),
            Provider("T49",              "EE", "info@t49.ee",        slug: "t49"),
            Provider("Rekota",           "LT", "rekota@zebra.lt",    slug: "rekota-siauliai"),
            Provider("Healthy",          "EE", "info@ramirent.ee",   slug: "healthy"));

        var response = Body(await Make(db, new CapturingEmailQueue(),
                mailDomains: new FakeMailDomains(
                    "kapsel24.ee", "esvo.ee", "noortegija.ee", "t49.ee", "zebra.lt"))
            .RunIntroCampaign(null, default));

        response.WouldSend.Should().Be(1);
        Reason(response, "undeliverable_domain").Should().Be(5);
    }

    // ─── Idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ASupplierAlreadySentTo_IsSkippedOnASecondCall()
    {
        var db    = await DbWith(Provider());
        var queue = new CapturingEmailQueue();
        var controller = Make(db, queue);
        var live = new SupplierIntroCampaignRequest(DryRun: false);

        var first = Body(await controller.RunIntroCampaign(live, default));
        first.Sent.Should().Be(1);
        queue.Emails.Should().ContainSingle();
        (await db.Suppliers.SingleAsync()).IntroEmailSentAt.Should().NotBeNull(
            "the sent stamp is the only thing standing between us and a duplicate blast");

        var second = Body(await controller.RunIntroCampaign(live, default));
        second.Sent.Should().Be(0);
        Reason(second, "already_sent").Should().Be(1);
        queue.Emails.Should().ContainSingle("the second call must queue nothing at all");
    }

    [Fact]
    public async Task ADryRunAfterALiveSend_ReportsTheSupplierAsAlreadySent()
    {
        var db    = await DbWith(Provider());
        var queue = new CapturingEmailQueue();
        var controller = Make(db, queue);

        await controller.RunIntroCampaign(new SupplierIntroCampaignRequest(DryRun: false), default);
        var preview = Body(await controller.RunIntroCampaign(null, default));

        preview.WouldSend.Should().Be(0);
        preview.Samples.Should().BeEmpty();
        Reason(preview, "already_sent").Should().Be(1);
    }

    // ─── Live send: pacing, addresses, filters ────────────────────────────────

    [Fact]
    public async Task LiveSend_PacesTheBatch_AndSetsInfoAsReplyTo()
    {
        var suppliers = Enumerable.Range(0, AdminSupplierIntroController.BatchSize + 1)
            .Select(i => Provider($"Provider {i:D3}", email: $"p{i}@x.ee", slug: $"p-{i}"))
            .ToArray();
        var db    = await DbWith(suppliers);
        var queue = new CapturingEmailQueue();

        var response = Body(await Make(db, queue)
            .RunIntroCampaign(new SupplierIntroCampaignRequest(DryRun: false), default));

        response.Sent.Should().Be(21);
        queue.Emails.Should().HaveCount(21);
        queue.Emails.Should().AllSatisfy(e => e.ReplyTo.Should().Be("info@ruumly.eu",
            "a provider's reply — including an opt-out — must reach a human, not noreply@"));

        // First of each batch, then a smooth 15 s drip inside the batch.
        queue.Emails[0].Delay.Should().Be(TimeSpan.Zero);
        queue.Emails[1].Delay.Should().Be(TimeSpan.FromSeconds(15));
        queue.Emails[20].Delay.Should().Be(TimeSpan.FromMinutes(5),
            "recipient 21 opens the second batch");

        response.Pacing!.Batches.Should().Be(2);
        response.Pacing.EmailsPerMinute.Should().Be(4);
        response.Pacing.LastSendAt.Should().BeAfter(response.Pacing.FirstSendAt);
    }

    [Fact]
    public async Task DryRun_ReportsTheExactFromLineThatResendWillUse()
    {
        var db = await DbWith(Provider());

        var withDefaults = Body(await Make(db, new CapturingEmailQueue()).RunIntroCampaign(null, default));
        withDefaults.From.Should().Be("Ruumly <noreply@ruumly.eu>",
            "the founder must see the real From before approving, not an aspiration");
        withDefaults.ReplyTo.Should().Be("info@ruumly.eu");

        var configured = Config(fromName: "Ruumly", fromAddress: "info@ruumly.eu");
        var withInfoFrom = Body(await Make(db, new CapturingEmailQueue(), configured)
            .RunIntroCampaign(null, default));
        withInfoFrom.From.Should().Be("Ruumly <info@ruumly.eu>");
    }

    [Fact]
    public async Task LimitAndCountryFilters_NarrowTheAudience()
    {
        var db = await DbWith(
            Provider("EE A", "EE", "a@x.ee", slug: "ee-a"),
            Provider("EE B", "EE", "b@x.ee", slug: "ee-b"),
            Provider("EE C", "EE", "c@x.ee", slug: "ee-c"),
            Provider("LV A", "LV", "d@x.lv", slug: "lv-a"));

        var limited = Body(await Make(db, new CapturingEmailQueue())
            .RunIntroCampaign(new SupplierIntroCampaignRequest(Limit: 2), default));
        limited.WouldSend.Should().Be(2);
        Reason(limited, "over_limit").Should().Be(2);

        var latvian = Body(await Make(db, new CapturingEmailQueue())
            .RunIntroCampaign(new SupplierIntroCampaignRequest(Country: "lv"), default));
        latvian.WouldSend.Should().Be(1);
        latvian.ByLanguage.Should().BeEquivalentTo(new Dictionary<string, int> { ["lv"] = 1 });
    }

    [Fact]
    public async Task ExplicitSupplierIds_TargetJustThose_AndReportMissingOnes()
    {
        var wanted = Provider("Wanted", email: "wanted@x.ee", slug: "wanted");
        var db = await DbWith(wanted, Provider("Other", email: "other@x.ee", slug: "other"));
        var missing = Guid.NewGuid();

        var response = Body(await Make(db, new CapturingEmailQueue()).RunIntroCampaign(
            new SupplierIntroCampaignRequest(SupplierIds: [wanted.Id, missing]), default));

        response.WouldSend.Should().Be(1);
        response.Samples.Should().ContainSingle().Which.To.Should().Be("wanted@x.ee");
        Reason(response, "not_found").Should().Be(1);
    }

    // ─── The copy itself ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void EveryLanguage_ProducesSubjectTextAndHtml(string language)
    {
        var message = SupplierIntroComposer.ComposeInLanguage(
            language, "Kolimisabi OÜ", "https://ruumly.eu/et/claim/kolimisabi-ou");

        message.Language.Should().Be(language);
        message.Subject.Should().NotBeNullOrWhiteSpace();
        message.TextBody.Should().NotBeNullOrWhiteSpace();
        message.HtmlBody.Should().NotBeNullOrWhiteSpace();
        message.HtmlBody.Should().Contain("<html").And.Contain("</html>");
        // No images, no tracking pixel, no external CSS — a tracking pixel in a
        // first-contact cold email is exactly what makes it read as spam.
        message.HtmlBody.Should().NotContain("<img").And.NotContain("<link").And.NotContain("<script");
    }

    [Fact]
    public void NoLanguageFallsBackToEnglishCopy()
    {
        var en = EmailTranslations.For("en");

        foreach (var language in AllLanguages.Where(l => l != "en"))
        {
            var t = EmailTranslations.For(language);
            var because = $"'{language}' intro copy must be translated, not left on the English fallback";

            t.IntroSubject.Should().NotBe(en.IntroSubject, because);
            t.IntroWhoWeAre.Should().NotBe(en.IntroWhoWeAre, because);
            t.IntroListedTpl.Should().NotBe(en.IntroListedTpl, because);
            t.IntroWhatToExpect.Should().NotBe(en.IntroWhatToExpect, because);
            t.IntroQuestionsTpl.Should().NotBe(en.IntroQuestionsTpl, because);
            t.IntroClaimIntro.Should().NotBe(en.IntroClaimIntro, because);
            t.IntroClaimCta.Should().NotBe(en.IntroClaimCta, because);
            t.IntroClaimByEmailTpl.Should().NotBe(en.IntroClaimByEmailTpl, because);
            t.IntroOptOutTpl.Should().NotBe(en.IntroOptOutTpl, because);
            t.IntroOptOutLinkLabel.Should().NotBe(en.IntroOptOutLinkLabel, because);
        }
    }

    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void EveryLanguage_CarriesTheOptOut_InBothBodies(string language)
    {
        var t       = EmailTranslations.For(language);
        var message = SupplierIntroComposer.ComposeInLanguage(
            language, "Kolimisabi OÜ", "https://ruumly.eu/et/claim/kolimisabi-ou");

        var optOut = t.IntroOptOut(SupplierIntroComposer.OptOutKeyword);
        optOut.Should().NotBeNullOrWhiteSpace();
        message.TextBody.Should().Contain(optOut,
            "B2B marketing mail in the EU must carry a working opt-out (ePrivacy) — in every language");
        message.HtmlBody.Should().Contain(optOut);

        // One click, not a form: a real mailto in the HTML body.
        message.HtmlBody.Should().Contain("href=\"mailto:info@ruumly.eu?subject=REMOVE",
            "removal has to be one click, never a form");
        message.HtmlBody.Should().Contain(t.IntroOptOutLinkLabel);
    }

    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void EveryLanguage_HasNonEmptyIntroStrings(string language)
    {
        var t = EmailTranslations.For(language);

        new[]
        {
            t.IntroSubject, t.IntroGreeting, t.IntroWhoWeAre, t.IntroListedTpl,
            t.IntroWhatToExpect, t.IntroQuestionsTpl, t.IntroClaimIntro, t.IntroClaimCta,
            t.IntroClaimByEmailTpl, t.IntroOptOutTpl, t.IntroOptOutLinkLabel, t.IntroSignature,
        }.Should().AllSatisfy(s => s.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void SupplierCountry_PicksTheProvidersLanguage()
    {
        SupplierIntroComposer.Compose(Provider(country: "EE")).Language.Should().Be("et");
        SupplierIntroComposer.Compose(Provider(country: "LV")).Language.Should().Be("lv");
        SupplierIntroComposer.Compose(Provider(country: "LT")).Language.Should().Be("lt");
        SupplierIntroComposer.Compose(Provider(country: "FI")).Language.Should().Be("en");
        SupplierIntroComposer.Compose(Provider(country: "ee")).Language.Should().Be("et",
            "country casing is not consistent in the imported directory");
    }

    [Fact]
    public void SaysWhoWeAre_ThatTheyAreListedFree_AndHowToReachAHuman()
    {
        var t = EmailTranslations.For("et");

        var message = SupplierIntroComposer.Compose(
            Provider(name: "Kolimisabi OÜ"), "https://ruumly.eu");

        message.TextBody.Should().Contain(t.IntroWhoWeAre);
        message.TextBody.Should().Contain("Kolimisabi OÜ",
            "the 'you are already listed' line names the actual business");
        message.TextBody.Should().Contain(t.IntroWhatToExpect);
        message.TextBody.Should().Contain(SupplierIntroComposer.DefaultOpsPhone,
            "the campaign promises a human on the phone — that number is not optional copy");
    }

    [Fact]
    public void ClaimLink_PointsAtTheRealClaimFlow()
    {
        // Until 2026-08 this pointed at the partner page, whose only claim
        // mechanism was a mailto — the campaign promised "correct your details
        // and add prices" and the link could not deliver it. It now opens the
        // verification flow (/{lang}/claim/{slug}).
        var supplier = Provider(slug: "kolimisabi-ou", published: true);

        var message = SupplierIntroComposer.Compose(supplier, "https://ruumly.eu");

        const string url = "https://ruumly.eu/et/claim/kolimisabi-ou";
        message.TextBody.Should().Contain(url);
        message.HtmlBody.Should().Contain($"href=\"{url}\"");
        message.TextBody.Should().NotContain("/partner/kolimisabi-ou");
    }

    [Fact]
    public void NoClaimablePage_FallsBackToAMailtoInsteadOfALinkThat404s()
    {
        foreach (var supplier in new[]
                 {
                     Provider(slug: null),
                     Provider(slug: "unpublished", published: false),
                     // The claim endpoint refuses non-directory rows, so the
                     // guard here has to refuse them too.
                     Provider(slug: "real-partner", directory: false),
                 })
        {
            var message = SupplierIntroComposer.Compose(supplier, "https://ruumly.eu");

            message.TextBody.Should().NotContain("/claim/",
                "a cold email must never contain a link that 404s");
            message.TextBody.Should().Contain("info@ruumly.eu");
            message.HtmlBody.Should().Contain("mailto:info@ruumly.eu?subject=CLAIM");
        }
    }

    [Fact]
    public void HtmlEncodesTheCompanyName()
    {
        // Directory rows were scraped; a name is not guaranteed to be tame.
        var message = SupplierIntroComposer.Compose(
            Provider(name: "A & B <script>alert('x')</script>", slug: null));

        message.HtmlBody.Should().NotContain("<script>");
        message.HtmlBody.Should().Contain("&lt;script&gt;").And.Contain("&amp;");
    }

    [Fact]
    public void StaysShortEnoughToBeReadOnAPhone()
    {
        foreach (var language in AllLanguages)
        {
            var message = SupplierIntroComposer.ComposeInLanguage(
                language, "Kolimisabi OÜ", "https://ruumly.eu/et/claim/kolimisabi-ou");

            var words = message.TextBody.Split(
                [' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
            words.Should().BeLessThan(200,
                $"'{language}' runs {words} words — past ~200 a small operator stops reading");
        }
    }

    private static int Reason(SupplierIntroCampaignResponse response, string reason) =>
        response.Skipped.FirstOrDefault(s => s.Reason == reason)?.Count ?? 0;
}
