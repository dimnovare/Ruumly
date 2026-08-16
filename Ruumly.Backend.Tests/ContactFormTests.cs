using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Ruumly.Backend.Validators;

namespace Ruumly.Backend.Tests;

public class ContactFormTests
{
    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private sealed class NoOpNotifications : INotificationService
    {
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null) => Task.CompletedTask;
    }

    private sealed record SentEmail(
        string To, string Subject, string TextBody, string? HtmlBody, string? ReplyTo);

    /// <summary>
    /// Records every send, not just the last one: the contact form mails the team
    /// AND the sender, and the whole point of the second one is that it exists.
    /// </summary>
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public readonly List<SentEmail> Sent = [];

        public string? To => Sent.FirstOrDefault()?.To;
        public string? Subject => Sent.FirstOrDefault()?.Subject;
        public string? TextBody => Sent.FirstOrDefault()?.TextBody;

        public SentEmail? ToTeam => Sent.FirstOrDefault(e => e.Subject.StartsWith("[Ruumly contact]"));
        public SentEmail? ToSender(string address) => Sent.FirstOrDefault(e => e.To == address);

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Sent.Add(new SentEmail(to, subject, textBody, htmlBody, null));

        public void EnqueueEmail(
            string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Sent.Add(new SentEmail(to, subject, textBody, htmlBody, replyTo));

        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static SupportController MakeController(RuumlyDbContext db, IBackgroundEmailQueue emailQueue)
        => new SupportController(db, emailQueue, new NoOpNotifications(),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                TestServices.Outreach(db, emailQueue),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SupportController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    // ─── Validator ─────────────────────────────────────────────────────────

    [Fact]
    public void Validator_ValidRequest_Passes()
    {
        var result = new ContactRequestValidator().Validate(
            new ContactRequest("Jane", "jane@test.ee", "Question", "I have a storage question.", "et"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_EmptyAndShort_Fails()
    {
        var result = new ContactRequestValidator().Validate(
            new ContactRequest("", "not-an-email", "", "short"));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName)
            .Should().Contain(new[] { "Name", "Email", "Subject", "Message" });
    }

    // ─── Endpoint ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Contact_ValidRequest_ReturnsSuccess_AndEmailsTeam()
    {
        var db = CreateDb();
        var email = new CapturingEmailQueue();
        var controller = MakeController(db, email);

        var result = await controller.Contact(
            new ContactRequest("Jane", "jane@test.ee", "Need storage", "Looking for a unit in Tallinn.", "et"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        ((bool)value.GetType().GetProperty("success")!.GetValue(value)!).Should().BeTrue();

        // Falls back to info@ruumly.eu when siteEmail is not set.
        email.To.Should().Be("info@ruumly.eu");
        email.Subject.Should().Be("[Ruumly contact] Need storage");
        email.TextBody.Should().Contain("jane@test.ee");
        email.TextBody.Should().Contain("Looking for a unit in Tallinn.");
    }

    [Fact]
    public async Task Contact_UsesSiteEmailSetting_WhenPresent()
    {
        var db = CreateDb();
        db.PlatformSettings.Add(new PlatformSetting { Key = "siteEmail", Value = "team@ruumly.eu" });
        await db.SaveChangesAsync();

        var email = new CapturingEmailQueue();
        var controller = MakeController(db, email);

        await controller.Contact(
            new ContactRequest("Jane", "jane@test.ee", "Hi", "This is a valid message.", "en"));

        email.To.Should().Be("team@ruumly.eu");
    }

    // ─── The sender's receipt ──────────────────────────────────────────────
    //
    // Before 2026-08-16 the form produced ONE email, to the team, and nothing to
    // the person who wrote it — on a form with no account behind it, so there was
    // no way to tell a silent success from a silent failure. That is what a
    // partner is checking when the message they send is the single word "test".

    [Fact]
    public async Task Contact_AlsoSendsTheSenderAReceipt()
    {
        var db = CreateDb();
        var email = new CapturingEmailQueue();
        var controller = MakeController(db, email);

        await controller.Contact(new ContactRequest(
            "Ann", "anniviis@gmail.com", "Küsimus",
            "test\n\nPartner: Peetri Miniladu (peetri-miniladu)", "et"));

        email.Sent.Should().HaveCount(2, "the team is notified and the sender gets a receipt");

        var receipt = email.ToSender("anniviis@gmail.com");
        receipt.Should().NotBeNull();
        receipt!.Subject.Should().Be(EmailTranslations.For("et").ContactAckSubject);
        // Their own words read back — the cheapest proof that a human-shaped
        // thing arrived rather than a form post silently 200-ing.
        receipt.TextBody.Should().Contain("Küsimus").And.Contain("test");
        receipt.HtmlBody.Should().NotBeNullOrWhiteSpace(
            "the receipt must not look less legitimate than the cold mail we send strangers");
    }

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public async Task Contact_ReceiptIsWrittenInTheSendersLanguage(string language)
    {
        var db = CreateDb();
        var email = new CapturingEmailQueue();
        var controller = MakeController(db, email);

        await controller.Contact(new ContactRequest(
            "Ann", "ann@test.ee", "Hello", "A question about my profile.", language));

        var t = EmailTranslations.For(language);
        var receipt = email.ToSender("ann@test.ee")!;
        receipt.Subject.Should().Be(t.ContactAckSubject).And.NotBeNullOrWhiteSpace();
        receipt.TextBody.Should().Contain(t.ContactAckReceived).And.Contain(t.ContactAckReply);
    }

    [Fact]
    public async Task Contact_ReceiptPromisesNoResponseTime()
    {
        // A deadline invented by a queue and kept by a human is a deadline that
        // gets broken — the same rule the concierge receipt follows.
        foreach (var language in new[] { "et", "en", "ru", "lv", "lt" })
        {
            var email = new CapturingEmailQueue();
            await MakeController(CreateDb(), email).Contact(new ContactRequest(
                "Ann", "ann@test.ee", "Hello", "A question about my profile.", language));

            email.ToSender("ann@test.ee")!.TextBody.Should()
                .NotContain("24")
                .And.NotContainEquivalentOf("hour")
                .And.NotContainEquivalentOf("tund")
                .And.NotContainEquivalentOf("час")
                .And.NotContainEquivalentOf("stund")
                .And.NotContainEquivalentOf("valand");
        }
    }

    [Fact]
    public async Task Contact_TeamMailRepliesToTheSender_NotToNoreply()
    {
        var db = CreateDb();
        var email = new CapturingEmailQueue();
        var controller = MakeController(db, email);

        await controller.Contact(new ContactRequest(
            "Ann", "anniviis@gmail.com", "Küsimus", "A question about my profile.", "et"));

        // Every Ruumly mail is FROM noreply@, so without this header pressing
        // Reply in the team inbox composes to a mailbox nobody reads.
        email.ToTeam!.ReplyTo.Should().Be("anniviis@gmail.com");
    }

    [Fact]
    public async Task Contact_ReceiptRepliesToTheTeamInbox()
    {
        var db = CreateDb();
        db.PlatformSettings.Add(new PlatformSetting { Key = "siteEmail", Value = "team@ruumly.eu" });
        await db.SaveChangesAsync();

        var email = new CapturingEmailQueue();
        await MakeController(db, email).Contact(new ContactRequest(
            "Ann", "ann@test.ee", "Hello", "A question about my profile.", "et"));

        // The receipt invites a reply; it has to land where a human reads it.
        email.ToSender("ann@test.ee")!.ReplyTo.Should().Be("team@ruumly.eu");
    }

    [Fact]
    public async Task Contact_ReceiptQuotesOnlyTheOpeningOfALongMessage()
    {
        // The form takes 5,000 characters and this mail goes to an address nobody
        // verified. Quoting all of it would make Ruumly a delivery service for
        // 5 KB of someone else's text.
        var db = CreateDb();
        var email = new CapturingEmailQueue();
        var message = new string('x', 5000);

        await MakeController(db, email).Contact(
            new ContactRequest("Ann", "ann@test.ee", "Hello", message, "et"));

        var receipt = email.ToSender("ann@test.ee")!;
        receipt.TextBody.Should().Contain("…").And.NotContain(message);
        receipt.TextBody!.Length.Should().BeLessThan(2000);
        // The team still gets every word of it.
        email.ToTeam!.TextBody.Should().Contain(message);
    }

    [Fact]
    public async Task Contact_ReceiptEscapesTheSendersOwnMarkup()
    {
        var db = CreateDb();
        var email = new CapturingEmailQueue();

        await MakeController(db, email).Contact(new ContactRequest(
            "Ann", "ann@test.ee", "Hello", "<script>alert(1)</script> is my question.", "et"));

        email.ToSender("ann@test.ee")!.HtmlBody.Should()
            .NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public async Task Contact_InvalidEmail_SendsNothingAtAll()
    {
        var db = CreateDb();
        var email = new CapturingEmailQueue();

        var result = await MakeController(db, email).Contact(
            new ContactRequest("Ann", "not-an-email", "Hello", "A question about my profile."));

        result.Should().BeOfType<BadRequestObjectResult>();
        email.Sent.Should().BeEmpty();
    }
}
