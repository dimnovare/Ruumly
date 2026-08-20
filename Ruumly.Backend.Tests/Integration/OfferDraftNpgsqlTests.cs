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
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ruumly.Backend.Tests.Integration;

[Collection("Postgres integration")]
public class OfferDraftNpgsqlTests(PostgresIntegrationFixture pg)
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

    private sealed class UnlockedLeadReadBarrier : DbCommandInterceptor
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
            if (command.CommandText.Contains("FROM \"DemandLeads\"", StringComparison.Ordinal)
                && command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && !command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _release.TrySetResult();

                await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }

    private sealed class DeleteAfterSendInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _sent =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM \"Offers\"", StringComparison.Ordinal))
                await _sent.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"Offers\"", StringComparison.Ordinal))
                _sent.TrySetResult();

            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task ConcurrentCreateOffer_ReusesSingleDraft()
    {
        RequirePostgres();
        var leadId = await SeedLead();
        var barrier = new UnlockedLeadReadBarrier();
        await using var db1 = NewContext(barrier);
        await using var db2 = NewContext(barrier);

        var results = await Task.WhenAll(
            MakeAdmin(db1, new CapturingEmailQueue()).CreateOffer(
                leadId, new CreateOfferRequest(Options: [new OfferOptionInput("First")])),
            MakeAdmin(db2, new CapturingEmailQueue()).CreateOffer(
                leadId, new CreateOfferRequest(Options: [new OfferOptionInput("Second")])));

        results.Should().OnlyContain(result => result is OkObjectResult);
        var ids = results
            .Cast<OkObjectResult>()
            .Select(result => Read<Guid>(result.Value!, "id"))
            .ToList();
        ids.Distinct().Should().ContainSingle();

        await using var verify = pg.NewContext();
        (await verify.Offers.CountAsync(o => o.DemandLeadId == leadId))
            .Should().Be(1);
        (await verify.OfferOptions.CountAsync(o => o.OfferId == ids[0]))
            .Should().Be(1, "the losing request body must not be merged into the winning draft");
    }

    [Fact]
    public async Task DeleteOffer_WhenConcurrentSendCommits_ReturnsConflictAndKeepsSentOffer()
    {
        RequirePostgres();
        var leadId = await SeedLead();
        Guid offerId;
        await using (var seed = pg.NewContext())
        {
            var offer = new Offer
            {
                Id = Guid.NewGuid(), DemandLeadId = leadId, Token = OfferToken.Generate(),
                Status = OfferStatus.Draft, Language = "en", CreatedAt = DateTime.UtcNow,
                Options =
                [
                    new OfferOption
                    {
                        Id = Guid.NewGuid(), Title = "Concurrent option", SortOrder = 0,
                    },
                ],
            };
            offerId = offer.Id;
            seed.Offers.Add(offer);
            await seed.SaveChangesAsync();
        }

        var coordination = new DeleteAfterSendInterceptor();
        await using var deleteDb = NewContext(coordination);
        await using var sendDb = NewContext(coordination);
        var sendQueue = new CapturingEmailQueue();

        var deleteTask = MakeAdmin(deleteDb, new CapturingEmailQueue()).DeleteOffer(offerId);
        var sendTask = MakeAdmin(sendDb, sendQueue).SendOffer(offerId);
        var results = await Task.WhenAll(deleteTask, sendTask);

        results[0].Should().BeOfType<ConflictObjectResult>();
        results[1].Should().BeOfType<OkObjectResult>();
        sendQueue.Recipients.Should().ContainSingle();

        await using var verify = pg.NewContext();
        var stored = await verify.Offers.SingleAsync(o => o.Id == offerId);
        stored.Status.Should().Be(OfferStatus.Sent);
        (await verify.OfferOptions.CountAsync(o => o.OfferId == offerId)).Should().Be(1);
    }

    private void RequirePostgres() => Assert.True(pg.Available,
        "PostgreSQL integration database unavailable. Start local PostgreSQL on port 5433 " +
        "or set RUUMLY_TEST_PG before running this focused gate.");

    private async Task<Guid> SeedLead()
    {
        var id = Guid.NewGuid();
        await using var db = pg.NewContext();
        db.DemandLeads.Add(new DemandLead
        {
            Id = id,
            Email = $"customer-{id:N}@x.ee",
            City = "Tallinn",
            Category = DemandLeadCategory.Moving,
            Language = "en",
            Source = "concierge",
            Status = DemandLeadStatus.New,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
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
        new(db, queue, TestServices.Config(), TestServices.Outreach(db, queue, TestServices.Config()),
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

    private static T Read<T>(object value, string propertyName) =>
        (T)value.GetType().GetProperty(propertyName)!.GetValue(value)!;
}
