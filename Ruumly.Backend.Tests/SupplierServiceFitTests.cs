using FluentAssertions;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Who a provider will actually work for, and the fan-out honouring it.
///
/// A service slug says WHAT a company does, never WHO for. One Viimsi request
/// for weekly home cleaning went to seventeen providers; four replied and three
/// said, in different words, "not for someone like you" — one was B2B only, two
/// take one-off specialist work but never a recurring arrangement. Nothing in
/// the data could express that, so the matcher could not act on it.
///
/// The property that matters most here is the DEFAULT. Both flags start true,
/// and a scaffolding accident that made them false would skip every provider in
/// every fan-out — 1,187 suppliers, silently, with the send reporting a tidy
/// "business_only" for each.
/// </summary>
public class SupplierServiceFitTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<string> Recipients { get; } = [];
        public void EnqueueEmail(string to, string s, string b, string? h = null) => Recipients.Add(to);
        public void EnqueueEmail(string to, string s, string b, string? h, string? r) => Recipients.Add(to);
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static Supplier Sup(string name, string email, bool consumers = true, bool recurring = true) =>
        new()
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C", ContactEmail = email,
            ContactPhone = "1", IsActive = true, Country = "EE",
            ServesConsumers = consumers, ServesRecurring = recurring,
        };

    private static DemandLead CleaningLead(RuumlyDbContext db, string? scopeJson)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Viimsi",
            Category = DemandLeadCategory.Cleaning, Language = "ru", Source = "concierge",
            Status = DemandLeadStatus.New, CreatedAt = DateTime.UtcNow,
            ScopeJson = scopeJson,
        };
        db.DemandLeads.Add(lead);
        return lead;
    }

    // ─── The default is the whole safety story ────────────────────────────────

    [Fact]
    public void ANewSupplier_ServesEveryone_UntilSomeoneRecordsOtherwise()
    {
        var fresh = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Untouched", ContactName = "C",
            ContactEmail = "x@x.ee", ContactPhone = "1", IsActive = true,
        };

        fresh.ServesConsumers.Should().BeTrue(
            "1,187 rows nobody has classified must keep receiving requests");
        fresh.ServesRecurring.Should().BeTrue();
    }

    // ─── Business-only providers ──────────────────────────────────────────────

    [Fact]
    public async Task ABusinessOnlyProvider_IsSkipped_WithAReasonRatherThanSilently()
    {
        var db = TestDbContext.Create();
        var lead = CleaningLead(db, null);
        var b2b = Sup("Lux Puhastus", "info@luxpuhastus.ee", consumers: false);
        var normal = Sup("Viimsi Koristus", "info@viimsikoristus.ee");
        db.Suppliers.AddRange(b2b, normal);
        await db.SaveChangesAsync();
        var queue = new CapturingEmailQueue();

        var result = await TestServices.Outreach(db, queue)
            .SendAsync(lead, [b2b.Id, normal.Id], resend: false, actor: "test");

        queue.Recipients.Should().BeEquivalentTo(["info@viimsikoristus.ee"]);
        result.Skipped.Should().ContainSingle(s => s.Reason == "business_only",
            "an operator has to learn WHY, not just watch a row get no email");
    }

    // ─── One-off providers, and the gate that only bites when it should ───────

    [Fact]
    public async Task AOneOffOnlyProvider_IsSkipped_WhenTheCustomerWantsSomethingRecurring()
    {
        var db = TestDbContext.Create();
        // cleaningFrequency 2 = weekly.
        var lead = CleaningLead(db, """{"cleaningFrequency":2}""");
        var oneOff = Sup("Kendra", "info@kendra.ee", recurring: false);
        db.Suppliers.Add(oneOff);
        await db.SaveChangesAsync();
        var queue = new CapturingEmailQueue();

        var result = await TestServices.Outreach(db, queue)
            .SendAsync(lead, [oneOff.Id], resend: false, actor: "test");

        queue.Recipients.Should().BeEmpty();
        result.Skipped.Should().ContainSingle(s => s.Reason == "no_recurring");
    }

    [Fact]
    public async Task AOneOffOnlyProvider_STILL_GetsAOneOffJob()
    {
        var db = TestDbContext.Create();
        // cleaningFrequency 1 = just once. This provider is a good match.
        var lead = CleaningLead(db, """{"cleaningFrequency":1}""");
        var oneOff = Sup("Kendra", "info@kendra.ee", recurring: false);
        db.Suppliers.Add(oneOff);
        await db.SaveChangesAsync();
        var queue = new CapturingEmailQueue();

        await TestServices.Outreach(db, queue).SendAsync(lead, [oneOff.Id], resend: false, actor: "test");

        queue.Recipients.Should().BeEquivalentTo(["info@kendra.ee"],
            "refusing recurring work does not make them wrong for a move-out clean");
    }

    [Fact]
    public async Task ALeadWithNoFrequencyAnswer_IsNotTreatedAsRecurring()
    {
        var db = TestDbContext.Create();
        // Every lead created before cleaningFrequency existed looks like this —
        // including the Viimsi request that prompted the whole feature.
        var lead = CleaningLead(db, """{"cleaningType":4,"cleaningSize":3}""");
        var oneOff = Sup("Kendra", "info@kendra.ee", recurring: false);
        db.Suppliers.Add(oneOff);
        await db.SaveChangesAsync();
        var queue = new CapturingEmailQueue();

        await TestServices.Outreach(db, queue).SendAsync(lead, [oneOff.Id], resend: false, actor: "test");

        queue.Recipients.Should().ContainSingle(
            "an unreadable answer must not silently withhold the request");
    }

    // ─── The reader ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"cleaningFrequency":1}""", false)] // just once
    [InlineData("""{"cleaningFrequency":2}""", true)]  // weekly
    [InlineData("""{"cleaningFrequency":3}""", true)]  // fortnightly
    [InlineData("""{"cleaningFrequency":4}""", true)]  // monthly
    [InlineData("""{"cleaningFrequency":5}""", false)] // not sure — has not asked for a contract
    [InlineData("""{"cleaningType":4}""", false)]      // no frequency answer at all
    [InlineData(null, false)]
    [InlineData("not json", false)]
    public void WantsRecurringService_ReadsOnlyAnExplicitRecurringAnswer(string? scopeJson, bool expected)
    {
        LeadScope.WantsRecurringService(scopeJson).Should().Be(expected);
    }

    [Fact]
    public void TheRecurringPositions_MatchTheCatalogue()
    {
        // If cleaningFrequency ever gains or loses an option, the 2/3/4 in
        // WantsRecurringService stops meaning what it means here.
        var question = ScopeQuestions.All.Single(q => q.Id == ScopeQuestions.CleaningFrequency);
        question.Options.Should().Be(5,
            "positions 2-4 are the recurring ones and 5 is 'not sure' — see LeadScope");
    }

    // ─── Recording it ─────────────────────────────────────────────────────────

    [Fact]
    public void TheUpdateRequest_LeavesBothUnchanged_WhenNotSupplied()
    {
        // Null means "unchanged", not "false" — a PATCH that edits a phone
        // number must not silently retire the provider.
        var body = new UpdateSupplierRequest(Name: "New name");
        body.ServesConsumers.Should().BeNull();
        body.ServesRecurring.Should().BeNull();
    }
}
