using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Closing the loop with the providers who priced a job.
///
/// The properties that matter: a provider hears exactly once per event, one
/// company behind two branch rows hears once (not twice), a dead address is not
/// written to, the losing letter never carries the winning price or the winner's
/// name, and the winner/loser verdicts can be delivered at different moments
/// without the two passes tripping over each other.
/// </summary>
public class ProviderOutcomeNotifierTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static ProviderOutcomeNotifier Make(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue, NullLogger<ProviderOutcomeNotifier>.Instance);

    private static Supplier Sup(string name, string email, string country = "LV", bool unusable = false) =>
        new()
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C", ContactEmail = email,
            ContactPhone = "1", IsActive = true, Country = country,
            ContactEmailUnusable = unusable,
        };

    /// <summary>A lead plus a Sent offer carrying one option per supplier.</summary>
    private static (Offer Offer, DemandLead Lead) Seed(
        RuumlyDbContext db, params (Supplier Supplier, decimal Price)[] quotes)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", Name = "Mari",
            City = "Tukums", ToCity = "Riga", Category = DemandLeadCategory.Moving,
            Language = "lv", Source = "concierge", Status = DemandLeadStatus.Quoted,
            CreatedAt = DateTime.UtcNow, ContactedAt = DateTime.UtcNow,
        };
        var offer = new Offer
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, Token = OfferToken.Generate(),
            Status = OfferStatus.Sent, SentAt = DateTime.UtcNow, Language = "lv",
            CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        db.Offers.Add(offer);

        var order = 0;
        foreach (var (supplier, price) in quotes)
        {
            db.Suppliers.Add(supplier);
            db.OfferOptions.Add(new OfferOption
            {
                Id = Guid.NewGuid(), OfferId = offer.Id, SupplierId = supplier.Id,
                Title = $"{supplier.Name} — Tukums", PriceAmount = price,
                PriceUnit = "vienreizējs", SortOrder = order++,
                CreatedFromOutreachId = Guid.NewGuid(),
            });
        }
        db.SaveChanges();
        return (offer, lead);
    }

    // ─── "Your price went to the customer" ────────────────────────────────────

    [Fact]
    public async Task OfferSent_NotifiesEveryProvider_Once_AndIsSafeToRunAgainOnResend()
    {
        var db = TestDbContext.Create();
        var (offer, _) = Seed(db,
            (Sup("Komanda24", "info@komanda24.lv"), 150m),
            (Sup("JK Movers", "info@jk.lv"), 210m));
        var queue = new CapturingEmailQueue();

        var first = await Make(db, queue).NotifyOfferSentAsync(offer.Id);
        first.Notified.Should().BeEquivalentTo(["info@komanda24.lv", "info@jk.lv"]);
        queue.Emails.Should().HaveCount(2);

        // POST /admin/offers/{id}/send is repeatable — an admin fixing a price
        // and re-sending must not re-announce the offer to everyone.
        var second = await Make(db, queue).NotifyOfferSentAsync(offer.Id);
        second.Notified.Should().BeEmpty();
        queue.Emails.Should().HaveCount(2, "the second pass must be a no-op");

        db.OfferOptions.Should().OnlyContain(o => o.ProviderNotifiedSentAt != null);
    }

    [Fact]
    public async Task OfferSent_EmailsOneCompanyOnce_WhenTwoBranchRowsShareAnInbox()
    {
        var db = TestDbContext.Create();
        // The directory is imported one row per BRANCH; several rows routinely
        // stand behind one head-office inbox.
        var (offer, _) = Seed(db,
            (Sup("Zebra Cargo Rīga", "zebra@zebracargo.com"), 180m),
            (Sup("Zebra Cargo Mārupe", "zebra@zebracargo.com"), 195m));
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).NotifyOfferSentAsync(offer.Id);

        queue.Emails.Should().HaveCount(1, "one company, one inbox, one letter");
        result.Skipped.Should().ContainSingle(s => s.Reason == "duplicate_email");
        // BOTH options are stamped: leaving the sibling unstamped would let a
        // later pass deliver exactly the duplicate this pass refused.
        db.OfferOptions.Should().OnlyContain(o => o.ProviderNotifiedSentAt != null);
    }

    [Fact]
    public async Task OfferSent_SkipsDeadAddressesAndMissingOnes_WithoutSendingOrThrowing()
    {
        var db = TestDbContext.Create();
        var (offer, _) = Seed(db,
            (Sup("Bounced", "dead@x.lv", unusable: true), 100m),
            (Sup("Blank", "", "LV"), 120m),
            (Sup("Good", "good@x.lv"), 140m));
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).NotifyOfferSentAsync(offer.Id);

        result.Notified.Should().BeEquivalentTo(["good@x.lv"]);
        result.Skipped.Select(s => s.Reason).Should().BeEquivalentTo(["email_bounced", "no_email"]);
        queue.Emails.Should().ContainSingle();
    }

    // ─── Winner and losers, delivered at different moments ────────────────────

    [Fact]
    public async Task WinnerOnly_TellsTheChosenProvider_AndLeavesTheLosersCompletelyUntouched()
    {
        var db = TestDbContext.Create();
        var (offer, _) = Seed(db,
            (Sup("Winner", "win@x.lv"), 150m),
            (Sup("Loser", "lose@x.lv"), 210m));
        var winning = db.OfferOptions.Single(o => o.PriceAmount == 150m);
        offer.Status = OfferStatus.Chosen;
        offer.ChosenOptionId = winning.Id;
        offer.ChosenAt = DateTime.UtcNow;
        db.SaveChanges();
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).NotifyOutcomeAsync(offer.Id, OutcomeAudience.WinnerOnly);

        result.Notified.Should().BeEquivalentTo(["win@x.lv"]);
        queue.Emails.Should().ContainSingle();
        // The loser must be left entirely alone — unstamped and unmailed — so the
        // later ConfirmBooking pass still finds them.
        db.OfferOptions.Single(o => o.PriceAmount == 210m)
            .ProviderNotifiedOutcomeAt.Should().BeNull();
    }

    [Fact]
    public async Task LosersOnly_TellsTheRest_AndNeverRevealsTheWinningPriceOrTheWinner()
    {
        var db = TestDbContext.Create();
        var (offer, _) = Seed(db,
            (Sup("Winner Co", "win@x.lv"), 150m),
            (Sup("Loser", "lose@x.lv"), 210m));
        var winning = db.OfferOptions.Single(o => o.PriceAmount == 150m);
        offer.Status = OfferStatus.Chosen;
        offer.ChosenOptionId = winning.Id;
        db.SaveChanges();
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).NotifyOutcomeAsync(offer.Id, OutcomeAudience.LosersOnly);

        result.Notified.Should().BeEquivalentTo(["lose@x.lv"]);
        var letter = queue.Emails.Should().ContainSingle().Subject;
        letter.TextBody.Should().NotContain("150",
            "the winning price is that provider's confidential commercial data");
        letter.TextBody.Should().NotContain("Winner Co",
            "a losing provider is never told who beat them");
        letter.TextBody.Should().NotContain("cust@x.ee").And.NotContain("Mari",
            "no provider letter carries the customer's identity");
        letter.TextBody.Should().Contain("210", "their OWN price names the job they priced");
    }

    /// <summary>
    /// Founder decision 2026-08-20: when the customer chooses, EVERYONE hears.
    /// With winner and losers in one pass, a company holding two branch rows
    /// behind one inbox must be told the GOOD news — the losing row must not win
    /// the race for the shared address.
    /// </summary>
    [Fact]
    public async Task AllPass_GivesASharedInboxTheWinningLetter_NotTheLosingOne()
    {
        var db = TestDbContext.Create();
        // The LOSING branch sorts first, so plain SortOrder would hand it the inbox.
        var (offer, _) = Seed(db,
            (Sup("Zebra Mārupe", "zebra@zebracargo.com"), 210m),
            (Sup("Zebra Rīga", "zebra@zebracargo.com"), 150m));
        var winning = db.OfferOptions.Single(o => o.PriceAmount == 150m);
        offer.Status = OfferStatus.Chosen;
        offer.ChosenOptionId = winning.Id;
        db.SaveChanges();
        var queue = new CapturingEmailQueue();

        await Make(db, queue).NotifyOutcomeAsync(offer.Id, OutcomeAudience.All);

        var letter = queue.Emails.Should().ContainSingle().Subject;
        letter.TextBody.Should().Contain("150",
            "the winning row must claim the shared inbox, so the company reads that it won");
        letter.TextBody.Should().NotContain("210");
    }

    [Fact]
    public async Task TwoPasses_DoNotTellOneInbox_BothThatItWonAndThatItLost()
    {
        var db = TestDbContext.Create();
        // Same company, two branch rows, one of each verdict.
        var (offer, _) = Seed(db,
            (Sup("Zebra Rīga", "zebra@zebracargo.com"), 150m),
            (Sup("Zebra Mārupe", "zebra@zebracargo.com"), 210m));
        var winning = db.OfferOptions.Single(o => o.PriceAmount == 150m);
        offer.Status = OfferStatus.Chosen;
        offer.ChosenOptionId = winning.Id;
        db.SaveChanges();
        var queue = new CapturingEmailQueue();

        var notifier = Make(db, queue);
        await notifier.NotifyOutcomeAsync(offer.Id, OutcomeAudience.WinnerOnly);
        var losers = await notifier.NotifyOutcomeAsync(offer.Id, OutcomeAudience.LosersOnly);

        queue.Emails.Should().ContainSingle(
            "the inbox that just read 'you won' must not then read 'somebody else was chosen'");
        losers.Skipped.Should().ContainSingle(s => s.Reason == "duplicate_email");
    }

    [Fact]
    public async Task Outcome_DoesNothing_BeforeTheCustomerHasChosen()
    {
        var db = TestDbContext.Create();
        var (offer, _) = Seed(db, (Sup("A", "a@x.lv"), 150m));
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).NotifyOutcomeAsync(offer.Id, OutcomeAudience.All);

        result.Notified.Should().BeEmpty();
        queue.Emails.Should().BeEmpty();
        db.OfferOptions.Single().ProviderNotifiedOutcomeAt.Should().BeNull();
    }

    [Fact]
    public async Task Outcome_IsIdempotent_SoARepeatedConfirmDoesNotRepeatTheLetters()
    {
        var db = TestDbContext.Create();
        var (offer, _) = Seed(db,
            (Sup("Winner", "win@x.lv"), 150m),
            (Sup("Loser", "lose@x.lv"), 210m));
        offer.Status = OfferStatus.Chosen;
        offer.ChosenOptionId = db.OfferOptions.Single(o => o.PriceAmount == 150m).Id;
        db.SaveChanges();
        var queue = new CapturingEmailQueue();
        var notifier = Make(db, queue);

        await notifier.NotifyOutcomeAsync(offer.Id, OutcomeAudience.All);
        await notifier.NotifyOutcomeAsync(offer.Id, OutcomeAudience.All);

        queue.Emails.Should().HaveCount(2, "two providers, one letter each, however often it runs");
    }

    // ─── Language and content ─────────────────────────────────────────────────

    [Fact]
    public async Task Letters_AreWrittenInTheProvidersOwnLanguage_NotTheCustomers()
    {
        var db = TestDbContext.Create();
        // Customer wrote in Latvian; this provider is Estonian.
        var (offer, _) = Seed(db, (Sup("Eesti Kolija", "info@kolija.ee", "EE"), 150m));
        var queue = new CapturingEmailQueue();

        await Make(db, queue).NotifyOfferSentAsync(offer.Id);

        var letter = queue.Emails.Should().ContainSingle().Subject;
        letter.TextBody.Should().Contain("Edastasime",
            "Supplier.Country decides the language, never Offer.Language");
        // Formal Estonian throughout the provider corpus.
        letter.TextBody.Should().Contain("teile").And.NotContain("sulle");
    }

    /// <summary>
    /// QuoteController stores the unit already translated into the CUSTOMER's
    /// language (the provider's literal words stay on ProviderOutreach). So a
    /// Latvian mover whose customer wrote in Estonian must not get their own
    /// price echoed back at them in Estonian.
    /// </summary>
    [Fact]
    public void Composer_PrintsThePriceUnit_InTheProvidersLanguage_NotTheCustomers()
    {
        var supplier = Sup("Latvian Mover", "info@lv.lv", "LV");
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Moving, Language = "et",
            Source = "concierge", Status = DemandLeadStatus.Quoted, CreatedAt = DateTime.UtcNow,
        };

        // "ühekordne" is what the option carries once QuoteController has
        // translated the provider's unit into the Estonian customer's language.
        var letter = ProviderOutcomeComposer.Compose(
            ProviderOutcomeComposer.Outcome.Lost, supplier, lead, 150m, "ühekordne");

        letter.TextBody.Should().Contain("vienreizējs",
            "a Latvian provider reads their own price in Latvian");
        letter.TextBody.Should().NotContain("ühekordne",
            "the option's unit is stored in the CUSTOMER's language and must not leak into a provider letter");
        letter.TextBody.Should().Contain("150 €");
    }

    /// <summary>
    /// A blank City is reachable (an admin edit, an older row). A raw
    /// {city} replace then produced a subject opening with a bare ": ".
    /// </summary>
    [Fact]
    public void Composer_NeverProducesASubjectStartingWithAStrayColon_WhenCityIsBlank()
    {
        var supplier = Sup("Mover", "info@x.ee", "EE");
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "", ToCity = "",
            Category = DemandLeadCategory.Moving, Language = "et",
            Source = "concierge", Status = DemandLeadStatus.Quoted, CreatedAt = DateTime.UtcNow,
        };

        var letter = ProviderOutcomeComposer.Compose(
            ProviderOutcomeComposer.Outcome.OfferSent, supplier, lead, 150m, "korra");

        letter.Subject.Should().NotStartWith(":").And.NotContain("{city}");
        letter.Subject.TrimStart().Should().NotStartWith(":");
    }

    // NOTE: the "two concurrent passes cannot both send" property is enforced by
    // a conditional UPDATE ... WHERE marker IS NULL, which the InMemory provider
    // cannot execute — so it is asserted in
    // Integration/ProviderOutcomeNotifierNpgsqlTests against real PostgreSQL,
    // where the guarantee actually lives. Writing it here would have tested the
    // in-memory fallback and quietly proved nothing about production.

    [Fact]
    public void Composer_CarriesTheOperatorLegalLine_AndTheLeadReference()
    {
        var supplier = Sup("Komanda24", "info@komanda24.lv");
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tukums", ToCity = "Riga",
            Category = DemandLeadCategory.Moving, Language = "lv",
            Source = "concierge", Status = DemandLeadStatus.Quoted, CreatedAt = DateTime.UtcNow,
        };

        var letter = ProviderOutcomeComposer.Compose(
            ProviderOutcomeComposer.Outcome.Won, supplier, lead, 150m, "vienreizējs");

        letter.Subject.Should().Contain(ProviderOutreachComposer.Reference(lead.Id),
            "Reply-To is one shared inbox — the handle is what makes a reply attributable");
        letter.Subject.Should().Contain("Tukums");
        letter.TextBody.Should().Contain("Diip Solutions OÜ", "cold-recipient trust signal");
        letter.TextBody.Should().Contain("Tukums → Riga");
        letter.TextBody.Should().Contain("150 € / vienreizējs");
    }
}
