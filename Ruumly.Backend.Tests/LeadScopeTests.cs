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
    // An array against a SINGLE-choice question is the wrong type for it, and is
    // dropped exactly like the other wrong types above. Picking one of the
    // positions would be inventing an answer on a fact a provider will price.
    [InlineData("""{"movingSize":[1]}""")]
    [InlineData("""{"movingSize":[1,2]}""")]
    // The empty array a tick-all-that-apply question sends when the visitor
    // unticks their last box. NO ANSWER, not a broken one — identical to never
    // having sent the key.
    [InlineData("""{"movingHeavyItems":[]}""")]
    // …and an array whose every element is junk collapses to the same thing.
    [InlineData("""{"movingHeavyItems":[0,7,"2",null,2.5,[2],{"a":2}]}""")]
    [InlineData("""{"movingHeavyItems":{}}""")]
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
    public void TheTwoRentalPeriods_AreDifferentLabels_InEveryLanguage()
    {
        // A trailer and a van can be asked for in the SAME request, and both
        // services ask how long it is needed. While both labels were the bare
        // "Rental period", one provider email printed that heading twice with
        // different values under it — which does not read as two questions, it
        // reads as a template that lost track of itself.
        //
        // Only the labels are made specific. The chips stay identical, for the
        // same reason the two access ends share theirs: a day is a day, and a
        // wording difference between the rows would look like a difference in
        // what was asked.
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);

            t.ScopeLabel(ScopeQuestions.TrailerDuration).Should()
                .NotBe(t.ScopeLabel(ScopeQuestions.VanRentalDuration),
                    $"'{language}' has to name which vehicle each rental period is about");

            for (var option = 1; option <= 5; option++)
                t.ScopeOption(ScopeQuestions.TrailerDuration, option).Should()
                    .Be(t.ScopeOption(ScopeQuestions.VanRentalDuration, option),
                        $"'{language}' chip {option} is the same span of time for either vehicle");
        }
    }

    [Fact]
    public void ATrailerAndAVanInOneRequest_RenderTwoNamedRentalPeriods()
    {
        // The lead that produced the duplicated heading: one customer, both
        // vehicles, one email to one provider.
        var lead = Lead(language: "et", scopeJson: """{"trailerDuration":2,"vanrentalDuration":4}""");
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;
        var t    = EmailTranslations.For("et");

        text.Should().Contain($"{t.ScopeLabel(ScopeQuestions.TrailerDuration)}: Üks päev")
            .And.Contain($"{t.ScopeLabel(ScopeQuestions.VanRentalDuration)}: Nädal või rohkem");
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

    // ─── Tick-all-that-apply, without disturbing anything already stored ──────
    //
    // Two questions genuinely are lists — "anything heavy or awkward?" and
    // "windows, oven or fridge as well?" — and were squeezed into single choice
    // to match the shape of the rest. Both paid for it with a chip describing
    // the CONTROL rather than the job ("several of these", "all three"), which
    // is a guess dressed as an answer on the one fact that decides the price.
    //
    // The stored column now carries either a number or an array. THE HARD
    // REQUIREMENT IS THAT NOTHING ALREADY IN IT MOVED: outreach is re-composed
    // from the stored row every time an admin fans a lead out to one more
    // provider, so a change in how an old row renders is a change to mail about
    // requests that were taken months ago.

    /// <summary>The one fact line a question produced, or null when it produced none.</summary>
    private static string? FactLine(string text, string label) =>
        text.Split('\n').FirstOrDefault(l => l.StartsWith($"{label}: ", StringComparison.Ordinal));

    [Fact]
    public void EveryStoredSingleNumber_StillRendersExactlyTheOptionWording()
    {
        // The whole catalogue, every chip, every language: a lead stored in the
        // single-number shape renders the option's wording and NOTHING else —
        // no separator, no conjunction, no bracket. That is what "existing rows
        // render exactly as they did" means, stated as something a machine can
        // check rather than as an intention.
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);
            foreach (var question in ScopeQuestions.All)
            {
                for (var option = 1; option <= question.Options; option++)
                {
                    var lead = Lead(scopeJson: $$"""{"{{question.Id}}":{{option}}}""");
                    var text = ProviderOutreachComposer.ComposeInLanguage(language, lead).TextBody;

                    FactLine(text, t.ScopeLabel(question.Id)!).Should()
                        .Be($"{t.ScopeLabel(question.Id)}: {t.ScopeOption(question.Id, option)}",
                            $"'{question.Id}' option {option} in '{language}'");
                }
            }
        }
    }

    [Theory]
    [InlineData("movingHeavyItems", 2)]
    [InlineData("cleaningExtras", 3)]
    public void OneSelection_ComposesIdentically_WhetherStoredAsANumberOrAOneItemArray(
        string questionId, int option)
    {
        // The two shapes are the same answer, so they have to produce the same
        // letter — subject, text and HTML. Anything else would mean the day a
        // question became tick-all-that-apply, every lead already holding a
        // single answer to it started reading differently.
        var supplier = Provider("EE");
        var lead     = Lead(language: "ru", scopeJson: $$"""{"{{questionId}}":{{option}}}""");
        var asNumber = ProviderOutreachComposer.Compose(lead, supplier);

        lead.ScopeJson = $$"""{"{{questionId}}":[{{option}}]}""";
        var asArray = ProviderOutreachComposer.Compose(lead, supplier);

        asArray.Subject.Should().Be(asNumber.Subject);
        asArray.TextBody.Should().Be(asNumber.TextBody);
        asArray.HtmlBody.Should().Be(asNumber.HtmlBody);
    }

    [Theory]
    [InlineData("movingHeavyItems", "Mitu neist")]
    [InlineData("cleaningExtras",   "Aknad, ahi ja külmik")]
    public void TheRetiredCatchAllChip_StillRenders_ForTheLeadsThatCarryIt(
        string questionId, string estonianWording)
    {
        // Position 5 of both questions was the single-choice era's escape hatch.
        // The intake has stopped offering it, and the catalogue deliberately did
        // NOT renumber to close the gap: the position is the identity of the
        // answer, so shifting "not sure" down into 5 would rewrite what every
        // lead already taken said.
        var lead = Lead(language: "ru", scopeJson: $$"""{"{{questionId}}":5}""");
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;

        text.Should().Contain(estonianWording);
    }

    [Fact]
    public void SeveralSelections_RenderOnOneFactLine_JoinedInTheProvidersLanguage()
    {
        // A piano AND an aquarium — the request "several of these" could only
        // gesture at. One row, because two rows under one heading reads as a
        // template that lost track of itself.
        var lead = Lead(language: "ru", scopeJson: """{"movingHeavyItems":[2,4]}""");

        var estonian = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;
        var latvian  = ProviderOutreachComposer.Compose(lead, Provider("LV")).TextBody;
        var english  = ProviderOutreachComposer.Compose(lead, Provider("FI")).TextBody;

        FactLine(estonian, "Rasked või keerukad esemed").Should()
            .Be("Rasked või keerukad esemed: Klaver ja Akvaarium, kunstiteos või muu õrn ese");
        FactLine(latvian, "Smagi vai neērti priekšmeti").Should()
            .Be("Smagi vai neērti priekšmeti: Klavieres un Akvārijs, mākslas darbs vai kas trausls");
        FactLine(english, "Heavy or awkward items").Should()
            .Be("Heavy or awkward items: A piano and An aquarium, artwork or something fragile");
    }

    [Fact]
    public void ThreeSelections_UseTheSeparatorForAllButTheLastPair()
    {
        // Windows, oven and fridge — which is what the retired "all three" chip
        // meant, now said by the customer rather than inferred by us.
        var lead = Lead(language: "et", scopeJson: """{"cleaningExtras":[2,3,4]}""");
        var text = ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody;

        FactLine(text, "Lisatööd").Should().Be("Lisatööd: Aknad, Ahi ja Külmik");
    }

    [Fact]
    public void EveryLanguage_JoinsSeveralAnswers_WithItsOwnWordAndNotWithAComma()
    {
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);

            t.ScopeJoin(["Aknad"]).Should().Be("Aknad",
                $"'{language}' must leave a single answer exactly as it was — every row " +
                "stored before this feature has one, and none of them may change");

            t.ScopeJoin(["A", "B"]).Should().NotBe("A, B",
                $"'{language}' joins with a real conjunction; a bare comma is punctuation " +
                "we chose, not language, and it reads as machine output in a cold email");
        }

        // …and it is a DIFFERENT word in each, which is the reason it is copy
        // rather than a constant in the composer.
        var conjunctions = AllLanguages
            .Select(l => EmailTranslations.For(l).ScopeJoin(["A", "B"]))
            .ToList();
        conjunctions.Should().OnlyHaveUniqueItems(
            "no language may be left sitting on another's conjunction");
    }

    [Fact]
    public void AnUnwordedChip_CostsThatChipOnly_NotTheWholeFactLine()
    {
        // A chip retired between the build that took the request and the build
        // reading it. Three ticked boxes minus one wording is still two facts a
        // mover needs — dropping the line would tell them about none of them.
        var answer = new ScopeAnswer(ScopeQuestions.CleaningExtras, new[] { 2, 4 });
        var stored = ScopeQuestions.Serialize([answer]);
        var lead   = Lead(language: "et", scopeJson: stored);

        FactLine(ProviderOutreachComposer.Compose(lead, Provider("EE")).TextBody, "Lisatööd")
            .Should().Be("Lisatööd: Aknad ja Külmik");
    }

    [Fact]
    public void SelectionsAreReadInChipOrder_Deduplicated_AndJunkCostsOnlyItsOwnElement()
    {
        // Tap order is chosen by a thumb, not by us, and outreach is re-composed
        // on every fan-out — so two customers who ticked the same boxes have to
        // produce the same letter, exactly as two customers who answered the
        // same QUESTIONS already do.
        LeadScope.Answers("""{"movingHeavyItems":[4,2,4,"x",99,0,null,2]}""")
            .Should().BeEquivalentTo(
                new[] { new ScopeAnswer("movingHeavyItems", new[] { 2, 4 }) },
                o => o.WithStrictOrdering());
    }

    [Fact]
    public void MixedShapes_InOneObject_BothSurvive()
    {
        LeadScope.Answers("""{"movingSize":3,"movingHeavyItems":[2,4],"cleaningExtras":2}""")
            .Should().BeEquivalentTo(
                new[]
                {
                    new ScopeAnswer("movingSize", 3),
                    new ScopeAnswer("movingHeavyItems", new[] { 2, 4 }),
                    new ScopeAnswer("cleaningExtras", 2),
                },
                o => o.WithStrictOrdering());
    }

    [Fact]
    public void Serialize_WritesABareNumberForOneSelection_AndAnArrayOnlyForSeveral()
    {
        // What lands in the column. One selection stays a bare number whether or
        // not the question accepts several, so an array in ScopeJson always
        // means the same thing — the customer really did tick more than one box
        // — and every other row looks precisely as it always did.
        ScopeQuestions.Serialize([
            new ScopeAnswer("movingSize", 3),
            new ScopeAnswer("movingHeavyItems", new[] { 2 }),
            new ScopeAnswer("cleaningExtras", new[] { 2, 4 }),
        ]).Should().Be("""{"movingSize":3,"movingHeavyItems":2,"cleaningExtras":[2,4]}""");

        ScopeQuestions.Serialize([]).Should().BeNull();
    }

    [Fact]
    public async Task ArrayAnswers_RoundTripThroughIntake_AndReachTheProviderLocalized()
    {
        // The whole path, on the shape that could not be expressed at all
        // before: browser payload → Normalize → ScopeJson → LeadScope → an
        // Estonian mover's inbox, for a request filled in on the Russian site.
        var db = TestDbContext.Create();

        var result = await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(new ConciergeRequest(
            Email: "olga@example.com", City: "Tallinn", ToCity: "Tartu",
            Categories: ["moving"], Language: "ru",
            Scope: Submitted("""
                {"movingSize":3,"movingHeavyItems":[4,2],"cleaningExtras":[]}
                """)));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.ScopeJson.Should().Be("""{"movingSize":3,"movingHeavyItems":[2,4]}""",
            "the empty array is no answer at all, and the chips are stored in catalogue order");

        var message = ProviderOutreachComposer.Compose(lead, Provider("EE"));
        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
            body.Should().Contain("Klaver ja Akvaarium, kunstiteos või muu õrn ese")
                .And.NotContain("Пианино");
    }

    [Fact]
    public async Task PublicQuoteDto_CarriesEveryChipOfAMultiSelectAnswer_AndStillNoPii()
    {
        var db  = TestDbContext.Create();
        var pub = MakeQuote(db, new CapturingEmailQueue());

        var lead = Lead(
            language: "ru",
            scopeJson: """{"movingSize":3,"movingHeavyItems":[2,4]}""",
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
            new[]
            {
                // A single answer puts exactly the bytes on the wire it always
                // did — a quote page served from a service-worker cache predates
                // the list and reads only `option`.
                new PublicQuoteScopeDto("movingSize", 3),
                // …and where there IS more than one chip, the legacy field
                // carries the first of them rather than nothing.
                new PublicQuoteScopeDto("movingHeavyItems", 2, new[] { 2, 4 }),
            },
            o => o.WithStrictOrdering());

        var json = JsonSerializer.Serialize(dto);
        json.Should().NotContain("Lihula").And.NotContain("Riia 12").And.NotContain("10-3");
        json.Should().NotContain("olga@example.com").And.NotContain("Olga Ivanova").And.NotContain("+372 5555 1234");
        // Slugs and positions, never our wording — the page owns the copy deck.
        // (`Details` is the customer's own free text and is carried verbatim by
        // design, so the chips checked here are ones it does not contain.)
        json.Should().NotContain("Akvaarium").And.NotContain("Пианино");

        // The single answer is on the wire exactly as it was before the list
        // existed: a null would be a new field for a service-worker-cached page
        // to trip over, so it is simply absent.
        json.Should().Contain("""{"Question":"movingSize","Option":3}""");
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
