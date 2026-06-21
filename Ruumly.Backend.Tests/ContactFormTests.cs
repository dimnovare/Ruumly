using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
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

    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public string? To;
        public string? Subject;
        public string? TextBody;

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
        {
            To = to;
            Subject = subject;
            TextBody = textBody;
        }

        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static SupportController MakeController(RuumlyDbContext db, IBackgroundEmailQueue emailQueue)
        => new SupportController(db, emailQueue, new NoOpNotifications())
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
}
