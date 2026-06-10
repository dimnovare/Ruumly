using FluentAssertions;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class BackgroundEmailServiceTests
{
    [Fact]
    public void EnqueueEmail_StoresDurableHangfireJob()
    {
        JobStorage.Current = new InMemoryStorage();
        var queue = new HangfireBackgroundEmailQueue(new BackgroundJobClient());

        queue.EnqueueEmail("partner@example.com", "Welcome", "Approved");

        var jobs = JobStorage.Current.GetMonitoringApi().EnqueuedJobs("default", 0, 10);
        jobs.Should().ContainSingle();
        jobs.Single().Value.Job.Type.Should().Be(typeof(BackgroundEmailService));
        jobs.Single().Value.Job.Method.Name.Should().Be(nameof(BackgroundEmailService.SendAsync));
    }

    [Fact]
    public async Task SendVerificationEmailAsync_PersistsTokenAndSendsLocalizedEmail()
    {
        await using var db = TestDbContext.Create();
        var user = new User
        {
            Id            = Guid.NewGuid(),
            Name          = "Test Partner",
            Email         = "partner@example.com",
            PasswordHash  = string.Empty,
            Role          = UserRole.Customer,
            Status        = UserStatus.Active,
            Language      = "en",
            RegisteredAt  = DateTime.UtcNow,
            EmailVerified = false,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sender = new CapturingEmailSender();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppUrl"] = "https://ruumly.eu",
            })
            .Build();
        var service = new BackgroundEmailService(
            db,
            sender,
            config,
            new NullLogger<BackgroundEmailService>());

        await service.SendVerificationEmailAsync(user.Id);

        user.EmailVerificationToken.Should().NotBeNullOrWhiteSpace();
        user.EmailVerificationExpiry.Should().BeAfter(DateTime.UtcNow.AddHours(23));
        sender.To.Should().Be(user.Email);
        sender.Subject.Should().NotBeNullOrWhiteSpace();
        sender.TextBody.Should().Contain("https://ruumly.eu/en/verify?token=");
    }

    [Fact]
    public async Task SendAsync_WhenProviderFails_PropagatesForHangfireRetry()
    {
        await using var db = TestDbContext.Create();
        var service = new BackgroundEmailService(
            db,
            new FailingEmailSender(),
            new ConfigurationBuilder().Build(),
            new NullLogger<BackgroundEmailService>());

        Func<Task> act = () => service.SendAsync("partner@example.com", "Welcome", "Approved");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? To { get; private set; }
        public string? Subject { get; private set; }
        public string? TextBody { get; private set; }

        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
        {
            To = to;
            Subject = subject;
            TextBody = textBody;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null) =>
            throw new HttpRequestException("Resend unavailable");
    }
}
