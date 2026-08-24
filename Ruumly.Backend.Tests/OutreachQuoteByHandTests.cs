using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Recording a price a provider sent by EMAIL rather than through the quote page.
///
/// The tokenized page is the happy path, but a real share of providers just
/// reply — and they are the ones least likely to click a link. On one Latvian
/// moving request two of seven repliers answered by mail and one carried a real
/// price (170 EUR + VAT); neither could be recorded as a quote, so both counted
/// as silence in the metric built to measure silence, and the price statistics
/// were biased against email repliers specifically.
/// </summary>
public class OutreachQuoteByHandTests
{
    private sealed class NoEmail : IBackgroundEmailQueue
    {
        public void EnqueueEmail(string to, string s, string b, string? h = null) { }
        public void EnqueueEmail(string to, string s, string b, string? h, string? r) { }
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static AdminOffersController Make(RuumlyDbContext db)
    {
        var queue = new NoEmail();
        return new(db, queue, new ConfigurationBuilder().Build(),
            TestServices.Outreach(db, queue),
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
    }

    private static ProviderOutreach Seed(RuumlyDbContext db, DemandLeadStatus leadStatus = DemandLeadStatus.Contacted)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.lv", City = "Tukums", ToCity = "Riga",
            Category = DemandLeadCategory.Moving, Language = "lv", Source = "concierge",
            Status = leadStatus, CreatedAt = DateTime.UtcNow,
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "movers.lv", ContactName = "Kaspars",
            ContactEmail = "info@movers.lv", ContactPhone = "1", IsActive = true, Country = "LV",
        };
        var row = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = supplier.Id,
            SentTo = supplier.ContactEmail, SentAt = DateTime.UtcNow.AddDays(-4),
            Status = ProviderOutreachStatus.Sent,
        };
        db.DemandLeads.Add(lead);
        db.Suppliers.Add(supplier);
        db.ProviderOutreaches.Add(row);
        db.SaveChanges();
        return row;
    }

    [Fact]
    public async Task AnEmailedPrice_IsRecorded_AndTheRowStopsReadingAsSilence()
    {
        var db = TestDbContext.Create();
        var row = Seed(db);

        var result = await Make(db).UpdateOutreach(row.Id, new UpdateOutreachRequest(
            QuotedAmount: 170m, QuotedUnit: "vienreizējs",
            QuotedNote: "170 EUR + PVN. Atbilde e-pastā."));

        result.Should().BeOfType<OkObjectResult>();
        var saved = db.ProviderOutreaches.Single();
        saved.QuotedAmount.Should().Be(170m);
        saved.QuotedUnit.Should().Be("vienreizējs");
        saved.Status.Should().Be(ProviderOutreachStatus.Replied,
            "recording a price IS the answer — the row must stop counting as silence");
        saved.QuotedAt.Should().NotBeNull();
    }

    /// <summary>
    /// A reply that sat unread in a spam folder for four days must not be
    /// stamped "now": median first response is measured off this column.
    /// </summary>
    [Fact]
    public async Task TheAnswerTime_IsWhenTheProviderReplied_NotWhenAnOperatorTypedIt()
    {
        var db = TestDbContext.Create();
        var row = Seed(db);
        var actuallyAnswered = DateTime.UtcNow.AddDays(-4).AddMinutes(30);

        await Make(db).UpdateOutreach(row.Id, new UpdateOutreachRequest(
            QuotedAmount: 170m, QuotedAt: actuallyAnswered));

        db.ProviderOutreaches.Single().QuotedAt.Should()
            .BeCloseTo(actuallyAnswered, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CorrectingTheAmount_DoesNotMoveTheMomentTheyAnswered()
    {
        var db = TestDbContext.Create();
        var row = Seed(db);
        var answered = DateTime.UtcNow.AddDays(-4);

        var ctrl = Make(db);
        await ctrl.UpdateOutreach(row.Id, new UpdateOutreachRequest(QuotedAmount: 170m, QuotedAt: answered));
        await ctrl.UpdateOutreach(row.Id, new UpdateOutreachRequest(QuotedAmount: 205m));

        var saved = db.ProviderOutreaches.Single();
        saved.QuotedAmount.Should().Be(205m, "a typo can be fixed");
        saved.QuotedAt.Should().BeCloseTo(answered, TimeSpan.FromSeconds(1),
            "a correction is not a second answer");
    }

    [Fact]
    public async Task ARecordedPrice_CannotBeErasedByAPartialPayload()
    {
        var db = TestDbContext.Create();
        var row = Seed(db);
        var ctrl = Make(db);
        await ctrl.UpdateOutreach(row.Id, new UpdateOutreachRequest(QuotedAmount: 170m));

        // An admin later edits only the note — the price is evidence and stays.
        await ctrl.UpdateOutreach(row.Id, new UpdateOutreachRequest(Note: "Called them, still valid."));

        db.ProviderOutreaches.Single().QuotedAmount.Should().Be(170m);
    }

    [Fact]
    public async Task AnExplicitStatus_Wins_OverTheImpliedReplied()
    {
        var db = TestDbContext.Create();
        var row = Seed(db);

        // They quoted, then withdrew — both facts are recordable in one call.
        await Make(db).UpdateOutreach(row.Id, new UpdateOutreachRequest(
            Status: "declined", QuotedAmount: 170m));

        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Declined);
        db.ProviderOutreaches.Single().QuotedAmount.Should().Be(170m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2_000_000)]
    public async Task ANonsensePrice_IsRefused_AndNothingIsWritten(decimal amount)
    {
        var db = TestDbContext.Create();
        var row = Seed(db);

        var result = await Make(db).UpdateOutreach(row.Id, new UpdateOutreachRequest(QuotedAmount: amount));

        result.Should().BeOfType<BadRequestObjectResult>();
        db.ProviderOutreaches.Single().QuotedAmount.Should().BeNull();
        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Sent);
    }

    [Fact]
    public async Task AFutureAnswerTime_IsRefused()
    {
        var db = TestDbContext.Create();
        var row = Seed(db);

        (await Make(db).UpdateOutreach(row.Id, new UpdateOutreachRequest(
            QuotedAmount: 170m, QuotedAt: DateTime.UtcNow.AddDays(2))))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Unlike the quote page, this records history without resurrecting work:
    /// most emailed prices get written down AFTER the request has closed.
    /// </summary>
    [Fact]
    public async Task AClosedLead_StillAcceptsTheRecord_AndNoDraftOfferAppears()
    {
        var db = TestDbContext.Create();
        var row = Seed(db, DemandLeadStatus.Dismissed);

        var result = await Make(db).UpdateOutreach(row.Id, new UpdateOutreachRequest(QuotedAmount: 170m));

        result.Should().BeOfType<OkObjectResult>(
            "a closed request is exactly when a four-day-old email gets written down");
        db.ProviderOutreaches.Single().QuotedAmount.Should().Be(170m);
        db.Offers.Should().BeEmpty("recording history must not put dead work back in the queue");
    }
}
