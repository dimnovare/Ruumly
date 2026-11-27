using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// A quote-form answer whose availability says only "not possible" is a NO.
/// Reproduces AKM Rent, Keila, 2026-11-27: 15 €, "Ei ole võimalik", "our light
/// trailers are only in Tallinn" — which became a 15 € option on the draft.
/// </summary>
public class NotPossibleAnswerTests
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

    [Theory]
    [InlineData("Ei ole võimalik")]
    [InlineData("ei ole võimalik.")]
    [InlineData("„ei ole võimalik“")]
    [InlineData("ei  ole   võimalik")]
    [InlineData("Kahjuks ei saa")]
    [InlineData("Not possible")]
    [InlineData("не получится")]
    [InlineData("Nav iespējams")]
    [InlineData("Negalime!")]
    [InlineData("ei")]
    public void ABareNo_IsRecognised(string availability) =>
        NotPossibleAnswer.Matches(availability).Should().BeTrue();

    [Theory]
    [InlineData("ei ole võimalik 1.12, 2.12 sobib")] // a quote with another date in it
    [InlineData("27.11.26")]
    [InlineData("Sobib")]
    [InlineData("kohe")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingMore_IsStillAQuote(string? availability) =>
        NotPossibleAnswer.Matches(availability).Should().BeFalse();

    [Fact]
    public async Task NotPossible_IsRecordedAsADecline_AndNoOfferIsSeeded()
    {
        var (db, queue, lead, supplier) = await SetupAsync(claimed: false);
        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");
        var token = (await db.ProviderOutreaches.SingleAsync()).QuoteToken!;
        queue.Emails.Clear();

        var result = await MakeQuote(db, queue).SubmitQuote(token,
            new SubmitQuoteRequest(15m, "päev", "Ei ole võimalik",
                "Meie kerghaagised asuvad ainult Tallinnas, Kristiine linnaosas."));

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<QuoteSubmittedDto>().Subject;
        dto.Declined.Should().BeTrue();

        var outreach = await db.ProviderOutreaches.SingleAsync();
        outreach.Status.Should().Be(ProviderOutreachStatus.Declined);
        outreach.QuotedAt.Should().BeNull("a no is not a quote in the quote-rate metric");
        outreach.QuotedAmount.Should().BeNull();
        outreach.DeclineNote.Should().Be("Meie kerghaagised asuvad ainult Tallinnas, Kristiine linnaosas.");
        (await db.Offers.CountAsync()).Should().Be(0);
        queue.Emails.Should().ContainSingle().Which.Subject.Should().Contain("provider declined");
    }

    [Fact]
    public async Task NotPossible_FromAStandingPriceProvider_TakesTheirSeededOptionBackOut()
    {
        var (db, queue, lead, supplier) = await SetupAsync(claimed: true);
        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");
        (await db.OfferOptions.CountAsync()).Should().Be(1);
        var token = (await db.ProviderOutreaches.SingleAsync()).QuoteToken!;

        await MakeQuote(db, queue).SubmitQuote(token, new SubmitQuoteRequest(80m, "päev", "ei ole võimalik"));

        (await db.OfferOptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AfterARealPrice_NotPossibleStaysAQuoteUpdate()
    {
        var (db, queue, lead, supplier) = await SetupAsync(claimed: false);
        await TestServices.Outreach(db, queue, TestServices.Config())
            .SendAsync(lead, [supplier.Id], resend: false, actor: "test");
        var token = (await db.ProviderOutreaches.SingleAsync()).QuoteToken!;
        var quote = MakeQuote(db, queue);
        await quote.SubmitQuote(token, new SubmitQuoteRequest(20m, "päev", "Sobib"));

        await quote.SubmitQuote(token, new SubmitQuoteRequest(20m, "päev", "Ei ole võimalik"));

        (await db.ProviderOutreaches.SingleAsync()).Status.Should().Be(ProviderOutreachStatus.Replied,
            "withdrawing a live price is a conversation, not a form field");
        (await db.OfferOptions.CountAsync()).Should().Be(1);
    }

    private static async Task<(RuumlyDbContext, CapturingEmailQueue, DemandLead, Supplier)> SetupAsync(bool claimed)
    {
        var db = TestDbContext.Create();
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "AKM Rent", ContactName = "AKM", ContactEmail = "info@akm.example",
            ContactPhone = "+372 500 0000", IsActive = true, Country = "EE", ServiceTypesJson = "[\"trailer\"]",
            ClaimedByEmail = claimed ? "info@akm.example" : null,
            PriceFrom = claimed ? 80m : null, PriceUnit = claimed ? "päev" : null,
        };
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "klient@example.ee", City = "Keila",
            Category = DemandLeadCategory.Trailer, Language = "ru",
            Source = "concierge", Status = DemandLeadStatus.New,
        };
        db.Suppliers.Add(supplier);
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();
        return (db, new CapturingEmailQueue(), lead, supplier);
    }

    private static QuoteController MakeQuote(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
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
}
