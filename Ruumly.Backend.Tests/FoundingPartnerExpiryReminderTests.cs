using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Jobs;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class FoundingPartnerExpiryReminderTests
{
    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject)> Sent { get; } = [];
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
        {
            Sent.Add((to, subject));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<(Guid UserId, string Title)> Created { get; } = [];
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null)
        {
            Created.Add((userId, title));
            return Task.CompletedTask;
        }
    }

    private static IConfiguration MakeConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://ruumly.eu" })
            .Build();

    [Fact]
    public async Task Sends_Reminder_For_Supplier_Expiring_In_30_Days()
    {
        var db            = TestDbContext.Create();
        var email         = new FakeEmailSender();
        var notifications = new FakeNotificationService();

        var supplierId = Guid.NewGuid();
        var userId     = Guid.NewGuid();

        db.Suppliers.Add(new Supplier
        {
            Id = supplierId, Name = "Test OÜ", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
            FoundingPartner = true,
            FoundingPartnerUntil = DateTime.UtcNow.AddDays(30), // exactly in window
        });
        db.Users.Add(new User
        {
            Id = userId, Name = "Provider", Email = "provider@test.ee",
            PasswordHash = "x", Role = UserRole.Provider, SupplierId = supplierId,
        });
        await db.SaveChangesAsync();

        var job = new FoundingPartnerExpiryReminderJob(
            db, notifications, email, MakeConfig(),
            NullLogger<FoundingPartnerExpiryReminderJob>.Instance);

        // Act
        await job.ExecuteAsync();

        // Assert
        notifications.Created.Should().ContainSingle()
            .Which.UserId.Should().Be(userId);
        email.Sent.Should().ContainSingle()
            .Which.To.Should().Be("provider@test.ee");

        var supplier = await db.Suppliers.FindAsync(supplierId);
        supplier!.FoundingPartnerReminderSentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Skips_Supplier_Already_Reminded_Recently()
    {
        var db            = TestDbContext.Create();
        var email         = new FakeEmailSender();
        var notifications = new FakeNotificationService();

        var supplierId = Guid.NewGuid();

        db.Suppliers.Add(new Supplier
        {
            Id = supplierId, Name = "Test OÜ", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
            FoundingPartner = true,
            FoundingPartnerUntil = DateTime.UtcNow.AddDays(30),
            FoundingPartnerReminderSentAt = DateTime.UtcNow.AddDays(-5), // reminded 5 days ago
        });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(), Name = "Provider", Email = "p@test.ee",
            PasswordHash = "x", Role = UserRole.Provider, SupplierId = supplierId,
        });
        await db.SaveChangesAsync();

        var job = new FoundingPartnerExpiryReminderJob(
            db, notifications, email, MakeConfig(),
            NullLogger<FoundingPartnerExpiryReminderJob>.Instance);

        // Act
        await job.ExecuteAsync();

        // Assert — no notifications sent
        notifications.Created.Should().BeEmpty();
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_Supplier_Expiring_Too_Far_Out()
    {
        var db            = TestDbContext.Create();
        var email         = new FakeEmailSender();
        var notifications = new FakeNotificationService();

        db.Suppliers.Add(new Supplier
        {
            Id = Guid.NewGuid(), Name = "Test OÜ", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
            FoundingPartner = true,
            FoundingPartnerUntil = DateTime.UtcNow.AddDays(90), // too far out
        });
        await db.SaveChangesAsync();

        var job = new FoundingPartnerExpiryReminderJob(
            db, notifications, email, MakeConfig(),
            NullLogger<FoundingPartnerExpiryReminderJob>.Instance);

        // Act
        await job.ExecuteAsync();

        // Assert
        notifications.Created.Should().BeEmpty();
        email.Sent.Should().BeEmpty();
    }
}
