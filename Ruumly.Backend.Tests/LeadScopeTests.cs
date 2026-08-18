using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The intake's scoping answers, kept as STRUCTURE instead of prose.
///
/// The funnel asks "how big is the home", "floor and lift", "how long do you
/// need it" as one-tap chips and used to throw the structure away: it rendered
/// each answer to a sentence in the CUSTOMER's language and glued it into the
/// free-text Details blob, which the provider email prints verbatim. So a
/// Russian-speaking customer's answers arrived, in Russian, in an Estonian
/// mover's inbox — and nothing was queryable.
///
/// The regression test for the whole feature is
/// <see cref="RussianCustomer_RendersEstonianScopeLines_ToAnEstonianMover"/>.
/// Everything else here defends the two properties that make it safe to ship:
/// a bad ScopeJson can never take down the outreach email that carries the
/// request itself, and the customer's street address never reaches a provider.
/// </summary>
public class LeadScopeTests
{
    private static readonly string[] AllLanguages = ["et", "en", "ru", "lv", "lt"];

    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody, string? HtmlBody)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody, htmlBody));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody, htmlBody));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private sealed class NoOpNotifications : INotificationService
    {
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null)
            => Task.CompletedTask;
    }

    private static SupportController MakeSupport(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue, new NoOpNotifications(), TestServices.Config(),
            TestServices.Outreach(db, queue),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SupportController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static QuoteController MakeQuote(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue,
            new Ruumly.Backend.Services.Implementations.OfferAutoSendService(
                db, queue, TestServices.Config(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Ruumly.Backend.Services.Implementations.OfferAutoSendService>.Instance),
            new TestServices.NoStorage(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QuoteController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static DemandLead Lead(
        string language = "ru",
        string? scopeJson = null,
        string? details = "Klaver on raske",
        string? fromAddress = null,
        string? toAddress = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = "olga@example.com",
        Name = "Olga Ivanova",
        Phone = "+372 5555 1234",
        City = "Tallinn",
        ToCity = "Tartu",
        Category = DemandLeadCategory.Moving,
        Details = details,
        ScopeJson = scopeJson,
        FromAddress = fromAddress,
        ToAddress = toAddress,
        Language = language,
        Source = "concierge",
        Status = DemandLeadStatus.New,
        CreatedAt = DateTime.UtcNow,
    };

    private static Supplier Provider(string country = "EE") => new()
    {
        Id = Guid.NewGuid(), Name = "Kolimisabi OÜ", ContactName = "C",
        ContactEmail = "provider@x.ee", ContactPhone = "1",
        Country = country, IsActive = true,
    };

    /// <summary>Scope answers as the browser sends them — raw JSON, parsed to JsonElements.</summary>
    private static Dictionary<string, JsonElement> Submitted(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    // ─── The regression test for the whole feature ────────────────────────────

    [Fact]
    public void RussianCustomer_RendersEstonianScopeLines_ToAnEstonianMover()
    {
        // A request filled in on the Russian site: 2-bedroom home, 4th floor
        // with no lift, movers to pack the fragile things.
        var lead = Lead(
            language: "ru",
            scopeJson: """{"movingSize":3,"movingAccess":4,"packingHelp":2}""");

        var message = ProviderOutreachComposer.Compose(lead, Provider("EE"));

        message.Language.Should().Be("et");
        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
        {
            body.Should().Contain("Kodu suurus").And.Contain("3-toaline korter");
            body.Should().Contain("Korrus ja lift").And.Contain("4. korrus või kõrgem, liftita");
            body.Should().Contain("Pakkimisabi").And.Contain("Ainult õrnad ja suured esemed");

            // The failure this feature exists to end: the customer's own
            // language pasted into a cold email written in someone else's.
            body.Should().NotContain("Размер жилья").And.NotContain("3-комнатная квартира");
            body.Should().NotContain("Этаж и лифт").And.NotContain("4 этаж и выше, без лифта");
        }
    }

    [Fact]
    public void SameLead_RendersEachProvidersOwnLanguage()
    {
        var lead = Lead(language: "ru", scopeJson: """{"movingAccess":3}""");

        var estonian = ProviderOutreachComposer.Compose(lead, Provider("EE"));
        var latvian  = ProviderOutreachComposer.Compose(lead, Provider("LV"));
        var english  = ProviderOutreachComposer.Compose(lead, Provider("FI"));

        estonian.TextBody.Should().Contain("Korrus ja lift").And.Contain("2.–3. korrus, liftita");
        latvian.TextBody.Should().Contain("Stāvs un lifts").And.Contain("2.–3. stāvs, bez lifta");
        english.TextBody.Should().Contain("Floor and lift").And.Contain("2nd–3rd floor, no lift");
    }

    [Fact]
    public void ScopeLines_FollowTheCatalogueOrder_NotTheBrowsersKeyOrder()
    {
        // Same answers, submitted in the opposite order. The email must not
        // change: two identical requests have to produce identical mail.
        var lead = Lead(language: "et", scopeJson: """{"packingHelp":1,"movingAccess":2,"movingSize":1}""");
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;

        text.IndexOf("Kodu suurus", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("Korrus ja lift", StringComparison.Ordinal));
        text.IndexOf("Korrus ja lift", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("Pakkimisabi", StringComparison.Ordinal));
    }

    [Fact]
    public void ScopeFacts_SitAboveTheCustomersOwnWords()
    {
        var lead = Lead(language: "et", scopeJson: """{"movingSize":1}""", details: "Klaver on raske");
        var t    = EmailTranslations.For("et");
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;

        text.IndexOf(t.OutreachLabelDate, StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("Kodu suurus", StringComparison.Ordinal));
        text.IndexOf("Kodu suurus", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf(t.OutreachLabelDetails, StringComparison.Ordinal));
    }

    // ─── A bad scope must never cost us the outreach email ────────────────────

    [Theory]
    // Not JSON at all / not an object.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    [InlineData("\"movingSize\"")]
    [InlineData("{}")]
    // A question this build does not know (a stale cached bundle, or one we retired).
    [InlineData("""{"nope":1}""")]
    [InlineData("""{"MOVINGSIZE":1}""")]
    // Out of range in both directions, including the 0 an off-by-one would send.
    [InlineData("""{"movingSize":0}""")]
    [InlineData("""{"movingSize":-1}""")]
    [InlineData("""{"movingSize":7}""")]
    [InlineData("""{"movingSize":2147483648}""")]
    // Right id, wrong type.
    [InlineData("""{"movingSize":null}""")]
    [InlineData("""{"movingSize":"3"}""")]
    [InlineData("""{"movingSize":2.5}""")]
    [InlineData("""{"movingSize":true}""")]
    [InlineData("""{"movingSize":{"a":1}}""")]
    [InlineData("""{"movingSize":[1]}""")]
    public void BadScopeJson_IsDropped_AndLeavesTheEmailByteIdenticalToNoScopeAtAll(string stored)
    {
        // ONE lead instance throughout: the subject carries a reference derived
        // from the lead id, so a fresh lead per case would differ for the wrong
        // reason.
        var lead     = Lead(language: "et", scopeJson: null);
        var supplier = Provider("EE");
        var baseline = ProviderOutreachComposer.Compose(lead, supplier);

        LeadScope.Answers(stored).Should().BeEmpty();

        lead.ScopeJson = stored;
        var message = ProviderOutreachComposer.Compose(lead, supplier);

        message.Subject.Should().Be(baseline.Subject);
        message.TextBody.Should().Be(baseline.TextBody,
            "a malformed extra must never change the mail that carries the request itself");
        message.HtmlBody.Should().Be(baseline.HtmlBody);
    }

    [Fact]
    public void PartlyBadScopeJson_KeepsTheGoodAnswers()
    {
        // One junk value must cost that one answer, never the whole object —
        // the reason this parses element by element instead of deserializing
        // straight into a Dictionary<string,int>.
        var answers = LeadScope.Answers(
            """{"movingSize":2,"movingAccess":"x","nope":1,"packingHelp":99,"trailerType":5}""");

        answers.Should().BeEquivalentTo(
            new[] { new ScopeAnswer("movingSize", 2), new ScopeAnswer("trailerType", 5) },
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void NoScopeAnswers_ComposeExactlyAsBefore()
    {
        var lead = Lead(language: "et", scopeJson: null);
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;
        var t    = EmailTranslations.For("et");

        // The four fact lines that existed before this feature, and nothing else.
        var labels = text.Split('\n')
            .Where(l => l.Contains(": ", StringComparison.Ordinal))
            .Select(l => l[..l.IndexOf(':', StringComparison.Ordinal)])
            .ToList();
        labels.Should().Contain([
            t.OutreachLabelService, t.OutreachLabelLocation,
            t.OutreachLabelDate, t.OutreachLabelDetails,
        ]);
        foreach (var question in ScopeQuestions.All)
            text.Should().NotContain(t.ScopeLabel(question.Id)!);
    }

    // ─── The catalogue ────────────────────────────────────────────────────────

    [Fact]
    public void EveryQuestion_BelongsToARealService()
    {
        ScopeQuestions.All.Should().AllSatisfy(q =>
            ServiceCategories.BySlug.Should().ContainKey(q.Service,
                "a question that claims a service which does not exist can never be grouped or filtered"));
        ScopeQuestions.All.Select(q => q.Id).Should().OnlyHaveUniqueItems();
        ScopeQuestions.All.Should().AllSatisfy(q => q.Options.Should().BeGreaterThan(1));
    }

    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void EveryLanguage_HasALabelAndEveryOption_ForEveryQuestion(string language)
    {
        // The guard that keeps "adding a question is one entry" honest: a
        // question with no copy renders no line, so a missing translation is
        // invisible in production and has to fail here instead.
        var t = EmailTranslations.For(language);

        foreach (var question in ScopeQuestions.All)
        {
            t.ScopeLabel(question.Id).Should().NotBeNullOrWhiteSpace(
                $"'{question.Id}' has no {language} label");
            for (var option = 1; option <= question.Options; option++)
                t.ScopeOption(question.Id, option).Should().NotBeNullOrWhiteSpace(
                    $"'{question.Id}' option {option} has no {language} wording");
        }
    }

    [Fact]
    public void NoLanguageLeavesTheScopeLabelsOnTheEnglishFallback()
    {
        var en = EmailTranslations.For("en");

        foreach (var language in AllLanguages.Where(l => l != "en"))
        {
            var t = EmailTranslations.For(language);
            foreach (var question in ScopeQuestions.All)
                t.ScopeLabel(question.Id).Should().NotBe(en.ScopeLabel(question.Id),
                    $"'{language}' scope copy must be translated, not left on the English fallback");
        }
    }

    [Fact]
    public void UnknownQuestionOrOption_HasNoWording_SoTheLineIsSkipped()
    {
        var t = EmailTranslations.For("et");

        t.ScopeLabel("someFutureQuestion").Should().BeNull();
        t.ScopeOption(ScopeQuestions.MovingSize, 99).Should().BeNull();
    }

    // ─── The seven questions the funnel added after the catalogue shipped ─────
    //
    // Dropping an unknown id in silence is the RIGHT behaviour for a stale
    // cached bundle and a catastrophic one for an id we simply forgot to
    // register: nothing throws, nothing is logged, and a customer who answered
    // "4th floor, no lift, and a piano" gets a mover whose email does not
    // mention it. Nothing but a test can tell those two cases apart.

    /// <summary>The ids the intake started sending, each with a real chip position.</summary>
    public static TheoryData<string, int> QuestionsTheIntakeAdded => new()
    {
        { ScopeQuestions.WarehouseGoods,   5 },  // climate-sensitive goods
        { ScopeQuestions.MovingAccessFrom, 1 },  // ground floor where it is picked up
        { ScopeQuestions.MovingAccessTo,   4 },  // 4th-floor walk-up at the other end
        { ScopeQuestions.MovingHeavyItems, 2 },  // a piano
        { ScopeQuestions.TrailerTow,       4 },  // no tow bar — needs a vehicle too
        { ScopeQuestions.VanRentalDriver,  3 },  // with a driver and loaders
        { ScopeQuestions.CleaningExtras,   5 },  // windows, oven and fridge
    };

    [Theory]
    [MemberData(nameof(QuestionsTheIntakeAdded))]
    public void EachQuestionTheIntakeAdded_SurvivesTheRoundTrip_AndRendersALocalizedLine(
        string questionId, int option)
    {
        // The whole path these answers were falling out of: browser payload →
        // Normalize → ScopeJson → LeadScope → the provider's email.
        var stored = ScopeQuestions.Serialize(ScopeQuestions.Normalize(
            Submitted(JsonSerializer.Serialize(new Dictionary<string, int> { [questionId] = option }))));

        LeadScope.Answers(stored).Should().BeEquivalentTo(
            new[] { new ScopeAnswer(questionId, option) },
            $"the intake sends '{questionId}' — an id this build has not registered is discarded silently");

        // Answered on the Russian site, read by an Estonian mover.
        var message = ProviderOutreachComposer.Compose(Lead(language: "ru", scopeJson: stored), Provider("EE"));
        var t       = EmailTranslations.For("et");

        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
            body.Should().Contain(t.ScopeLabel(questionId)!)
                .And.Contain(t.ScopeOption(questionId, option)!);
    }

    [Fact]
    public void TheTwoAccessEnds_AreDifferentLabels_OverIdenticalChipWording()
    {
        // A single "floor and lift" answer could not say WHICH end it described,
        // so the funnel now asks twice. The labels must differ — that is the
        // entire point — and the chips must not: a provider comparing the two
        // rows is comparing two floors, and any wording difference between them
        // would read as a difference in what was asked.
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);

            t.ScopeLabel(ScopeQuestions.MovingAccessFrom).Should()
                .NotBe(t.ScopeLabel(ScopeQuestions.MovingAccessTo),
                    $"'{language}' has to name which end of the move each row is about");

            for (var option = 1; option <= 5; option++)
                t.ScopeOption(ScopeQuestions.MovingAccessFrom, option).Should()
                    .Be(t.ScopeOption(ScopeQuestions.MovingAccessTo, option),
                        $"'{language}' chip {option} is the same floor whichever end it describes");
        }
    }

    [Fact]
    public void BothAccessEnds_RenderAsTwoDistinctLines_PickupFirst()
    {
        // Ground floor out, 4th-floor walk-up in: the job a single access
        // question mispriced in both directions.
        var lead = Lead(language: "ru", scopeJson: """{"movingAccessTo":4,"movingAccessFrom":1}""");
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;
        var t    = EmailTranslations.For("et");

        var from = text.IndexOf(t.ScopeLabel(ScopeQuestions.MovingAccessFrom)!, StringComparison.Ordinal);
        var to   = text.IndexOf(t.ScopeLabel(ScopeQuestions.MovingAccessTo)!,   StringComparison.Ordinal);

        from.Should().BeGreaterThan(-1);
        to.Should().BeGreaterThan(from, "the move is read in the order it happens: pickup, then destination");
        text.Should().Contain("Maja või esimene korrus").And.Contain("4. korrus või kõrgem, liftita");
    }

    [Fact]
    public void LegacyLead_WithTheRetiredSingleAccessQuestion_StillRenders()
    {
        // The funnel stopped sending 'movingAccess' when it split into two ends.
        // Leads created before that still carry it, and their outreach is
        // re-composed every time the admin fans out to one more provider — so
        // retiring the catalogue entry would quietly delete the access answer
        // those customers actually gave.
        ScopeQuestions.Find(ScopeQuestions.MovingAccess).Should().NotBeNull(
            "production rows written before the two-ended split still store this id");

        var lead    = Lead(language: "ru", scopeJson: """{"movingSize":3,"movingAccess":4,"packingHelp":2}""");
        var message = ProviderOutreachComposer.Compose(lead, Provider("EE"));

        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
            body.Should().Contain("Korrus ja lift").And.Contain("4. korrus või kõrgem, liftita");

        // And it still renders where it always did — between size and packing.
        var text = message.TextBody;
        text.IndexOf("Kodu suurus", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("Korrus ja lift", StringComparison.Ordinal));
        text.IndexOf("Korrus ja lift", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("Pakkimisabi", StringComparison.Ordinal));
    }

    [Fact]
    public void VanRentalDriver_IsReadBeforeTheRentalPeriodAndSize()
    {
        // With-or-without-a-driver decides whether the request is van rental at
        // all or a moving job with a crew. A provider who reads "one day, 12 m³"
        // first has been told the shape of a job they may not even be being
        // asked for — so the catalogue puts it first, and the answers are
        // submitted here in the opposite order to prove the catalogue wins.
        var lead = Lead(language: "ru",
            scopeJson: """{"vanrentalSize":2,"vanrentalDuration":3,"vanrentalDriver":2}""");
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;
        var t    = EmailTranslations.For("et");

        var driver   = text.IndexOf(t.ScopeLabel(ScopeQuestions.VanRentalDriver)!,   StringComparison.Ordinal);
        var duration = text.IndexOf(t.ScopeLabel(ScopeQuestions.VanRentalDuration)!, StringComparison.Ordinal);
        var size     = text.IndexOf(t.ScopeLabel(ScopeQuestions.VanRentalSize)!,     StringComparison.Ordinal);

        driver.Should().BeGreaterThan(-1);
        duration.Should().BeGreaterThan(driver, "the provider learns what is being asked for before how long");
        size.Should().BeGreaterThan(duration);
    }

    // ─── The intake persists structure, not just prose ────────────────────────

    [Fact]
    public async Task RequestConcierge_PersistsValidatedScope_AndKeepsWritingDetails()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        var result = await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "olga@example.com", City: "Tallinn", ToCity: "Tartu",
            Categories: ["moving"], Language: "ru",
            // Still glued into details by the browser — nothing may regress
            // while the frontend keeps sending it.
            Details: "Какого размера жильё? 3-комнатная квартира",
            Scope: Submitted("""
                {"movingSize":3,"movingAccess":4,"nope":1,"packingHelp":9,"movingSize2":2}
                """)));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.Details.Should().Be("Какого размера жильё? 3-комнатная квартира");
        LeadScope.Answers(lead.ScopeJson).Should().BeEquivalentTo(
            new[] { new ScopeAnswer("movingSize", 3), new ScopeAnswer("movingAccess", 4) },
            o => o.WithStrictOrdering(),
            "unknown ids and out-of-range options are dropped, never trusted");

        // The stored shape is the documented one: id → 1-based chip position.
        JsonSerializer.Deserialize<Dictionary<string, int>>(lead.ScopeJson!)
            .Should().BeEquivalentTo(new Dictionary<string, int>
            {
                ["movingSize"] = 3, ["movingAccess"] = 4,
            });
    }

    [Fact]
    public async Task RequestConcierge_WithNoScope_StoresNullNotAnEmptyObject()
    {
        var db = TestDbContext.Create();

        await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(new ConciergeRequest(
            Email: "olga@example.com", City: "Tallinn", Categories: ["moving"]));

        db.DemandLeads.Single().ScopeJson.Should().BeNull();
    }

    [Fact]
    public async Task RequestConcierge_MintsAUniqueUrlSafeStatusToken()
    {
        var db = TestDbContext.Create();
        var support = MakeSupport(db, new CapturingEmailQueue());

        await support.RequestConcierge(new ConciergeRequest(
            Email: "olga@example.com", City: "Tallinn", Categories: ["moving"]));
        await support.RequestConcierge(new ConciergeRequest(
            // A different city, so this is a new request and not swallowed by
            // the 10-minute duplicate window.
            Email: "olga@example.com", City: "Tartu", Categories: ["moving"]));

        var tokens = db.DemandLeads.Select(l => l.StatusToken).ToList();
        tokens.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        tokens.Should().AllSatisfy(token =>
            Regex.IsMatch(token!, "^[A-Za-z0-9_-]{43}$").Should().BeTrue(
                "the status token is url-safe base64 of 32 bytes without padding — it goes in a link"));
    }

    // ─── The address is the customer's home, not the provider's business ──────

    [Fact]
    public async Task RequestConcierge_PersistsBothAddresses()
    {
        var db = TestDbContext.Create();

        await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(new ConciergeRequest(
            Email: "olga@example.com", City: "Tallinn", ToCity: "Tartu", Categories: ["moving"],
            FromAddress: "Lihula mnt 10-3, Haapsalu", ToAddress: "Riia 12-4, Tartu"));

        var lead = db.DemandLeads.Single();
        lead.FromAddress.Should().Be("Lihula mnt 10-3, Haapsalu");
        lead.ToAddress.Should().Be("Riia 12-4, Tartu");
    }

    [Fact]
    public void ProviderOutreach_ShowsTheCity_NeverTheStreetAddress()
    {
        var lead = Lead(
            language: "et",
            scopeJson: """{"movingSize":2}""",
            fromAddress: "Lihula mnt 10-3, Haapsalu",
            toAddress: "Riia 12-4, Tartu");

        var message = ProviderOutreachComposer.Compose(lead, Provider("EE"), "https://ruumly.eu", OfferToken.Generate());

        foreach (var body in new[] { message.Subject, message.TextBody, message.HtmlBody! })
        {
            body.Should().NotContain("Lihula mnt").And.NotContain("Riia 12");
            body.Should().NotContain("10-3").And.NotContain("12-4");
        }
        message.TextBody.Should().Contain("Tallinn").And.Contain("Tartu",
            "the provider still sees the route — the city, exactly as before");
    }

    [Fact]
    public async Task PublicQuoteDto_CarriesTheScopeAsSlugs_AndNoAddress()
    {
        var db  = TestDbContext.Create();
        var pub = MakeQuote(db, new CapturingEmailQueue());

        var lead = Lead(
            language: "ru",
            scopeJson: """{"movingSize":3,"movingAccess":4}""",
            fromAddress: "Lihula mnt 10-3, Haapsalu",
            toAddress: "Riia 12-4, Tartu");
        db.DemandLeads.Add(lead);

        var supplier = Provider("EE");
        db.Suppliers.Add(supplier);

        var token = OfferToken.Generate();
        db.ProviderOutreaches.Add(new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = supplier.Id,
            SentTo = supplier.ContactEmail!, SentAt = DateTime.UtcNow,
            Status = ProviderOutreachStatus.Sent, QuoteToken = token,
        });
        await db.SaveChangesAsync();

        var dto = (await pub.GetQuote(token))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;

        dto.Lead.Scope.Should().BeEquivalentTo(
            new[] { new PublicQuoteScopeDto("movingSize", 3), new PublicQuoteScopeDto("movingAccess", 4) },
            o => o.WithStrictOrdering(),
            "the page renders its own localized labels — it needs the slug and the position, not our wording");

        var json = JsonSerializer.Serialize(dto);
        json.Should().NotContain("Lihula").And.NotContain("Riia 12").And.NotContain("10-3");
        json.Should().NotContain("olga@example.com").And.NotContain("Olga Ivanova").And.NotContain("+372 5555 1234");
        // Slugs and numbers, never rendered strings.
        json.Should().NotContain("3-toaline").And.NotContain("3-комнатная");
        json.Should().Contain("Tallinn", "the city is what the provider is quoting against");
    }

    [Fact]
    public async Task PublicQuoteDto_WithNoScope_SendsAnEmptyList()
    {
        var db  = TestDbContext.Create();
        var pub = MakeQuote(db, new CapturingEmailQueue());

        var lead     = Lead(language: "et", scopeJson: null);
        var supplier = Provider("EE");
        db.DemandLeads.Add(lead);
        db.Suppliers.Add(supplier);

        var token = OfferToken.Generate();
        db.ProviderOutreaches.Add(new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = supplier.Id,
            SentTo = supplier.ContactEmail!, SentAt = DateTime.UtcNow,
            Status = ProviderOutreachStatus.Sent, QuoteToken = token,
        });
        await db.SaveChangesAsync();

        var dto = (await pub.GetQuote(token))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;

        dto.Lead.Scope.Should().NotBeNull().And.BeEmpty(
            "an empty list is a page with no chips; null would be a page that has to guess");
    }
}
