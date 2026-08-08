using System.Data.Common;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests.Integration;

[Collection("Postgres integration")]
public class ProviderOutreachNpgsqlTests(PostgresIntegrationFixture pg)
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<string> Recipients { get; } = [];

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Recipients.Add(to);

        public void EnqueueEmail(
            string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Recipients.Add(to);

        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private sealed class DuplicateReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"ProviderOutreaches\"", StringComparison.Ordinal)
                && command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _release.TrySetResult();

                await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }

    [Fact]
    public async Task ConcurrentOutreach_SerializationLoserReturnsRetryable409_AndNeverQueues()
    {
        Assert.True(pg.Available,
            "PostgreSQL integration database unavailable. Start local PostgreSQL on port 5433 " +
            "or set RUUMLY_TEST_PG before running this focused gate.");

        var leadId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        await using (var seed = pg.NewContext())
        {
            seed.DemandLeads.Add(new DemandLead
            {
                Id = leadId,
                Email = "customer@x.ee",
                City = "Tallinn",
                ToCity = "Tartu",
                Category = DemandLeadCategory.Moving,
                Language = "en",
                Source = "concierge",
                Status = DemandLeadStatus.Contacted,
                ContactedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            seed.Suppliers.Add(new Supplier
            {
                Id = supplierId,
                Name = "Concurrent Provider",
                RegistryCode = Guid.NewGuid().ToString("N"),
                ContactName = "Provider",
                ContactEmail = $"provider-{Guid.NewGuid():N}@x.ee",
                ContactPhone = "1",
                IsActive = true,
            });
            await seed.SaveChangesAsync();
        }

        var barrier = new DuplicateReadBarrier();
        await using var db1 = NewContext(barrier);
        await using var db2 = NewContext(barrier);
        var queue1 = new CapturingEmailQueue();
        var queue2 = new CapturingEmailQueue();

        var results = await Task.WhenAll(
            MakeAdmin(db1, queue1).SendOutreach(
                leadId, new OutreachRequest([supplierId])),
            MakeAdmin(db2, queue2).SendOutreach(
                leadId, new OutreachRequest([supplierId])));

        results.Should().ContainSingle(r => r is OkObjectResult);
        var conflict = results.Should().ContainSingle(r => r is ConflictObjectResult)
            .Subject.Should().BeOfType<ConflictObjectResult>().Subject;
        Read<bool>(conflict.Value!, "retryable").Should().BeTrue();
        queue1.Recipients.Concat(queue2.Recipients).Should().ContainSingle(
            "the serialization loser must return before queueing");

        await using var verify = pg.NewContext();
        (await verify.ProviderOutreaches.CountAsync(o =>
                o.DemandLeadId == leadId && o.SupplierId == supplierId))
            .Should().Be(1);

        var retryQueue = new CapturingEmailQueue();
        var retry = await MakeAdmin(verify, retryQueue).SendOutreach(
            leadId, new OutreachRequest([supplierId]));
        var retryBody = retry.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var skipped = ((System.Collections.IEnumerable)Read<object>(retryBody, "skipped"))
            .Cast<object>().Should().ContainSingle().Subject;
        Read<string>(skipped, "reason").Should().Be("already_contacted");
        retryQueue.Recipients.Should().BeEmpty();
    }

    private RuumlyDbContext NewContext(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseNpgsql(pg.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new RuumlyDbContext(options);
    }

    private static AdminOffersController MakeAdmin(
        RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue, new ConfigurationBuilder().Build(), TestServices.Outreach(db, queue))
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

    private static T Read<T>(object value, string propertyName) =>
        (T)value.GetType().GetProperty(propertyName)!.GetValue(value)!;
}
