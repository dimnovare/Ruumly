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

namespace Ruumly.Backend.Tests.Integration;

[Collection("Postgres integration")]
public class OfferSelectionNpgsqlTests(PostgresIntegrationFixture pg)
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

    private sealed class UnlockedOfferReadBarrier : DbCommandInterceptor
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
            if (command.CommandText.Contains("FROM \"Offers\"", StringComparison.Ordinal)
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

    [Fact]
    public async Task ConcurrentSameOptionChoose_IsIdempotentAndQueuesOneNotification()
    {
        RequirePostgres();
        var seeded = await SeedOffer(OfferStatus.Sent);
        var barrier = new UnlockedOfferReadBarrier();
        await using var db1 = NewContext(barrier);
        await using var db2 = NewContext(barrier);
        var queue1 = new CapturingEmailQueue();
        var queue2 = new CapturingEmailQueue();

        var results = await Task.WhenAll(
            MakePublic(db1, queue1).ChooseOption(
                seeded.Token, new ChooseOptionRequest(seeded.OptionIds[0])),
            MakePublic(db2, queue2).ChooseOption(
                seeded.Token, new ChooseOptionRequest(seeded.OptionIds[0])));

        results.Should().OnlyContain(result => result is OkObjectResult);
        queue1.Recipients.Concat(queue2.Recipients).Should().ContainSingle();

        await using var verify = pg.NewContext();
        var stored = await verify.Offers.SingleAsync(o => o.Id == seeded.OfferId);
        stored.Status.Should().Be(OfferStatus.Chosen);
        stored.ChosenOptionId.Should().Be(seeded.OptionIds[0]);
        (await verify.DemandLeads.SingleAsync(l => l.Id == seeded.LeadId))
            .Status.Should().Be(DemandLeadStatus.Quoted);
    }

    [Fact]
    public async Task ConcurrentDifferentOptionChoose_AllowsOneWinnerAndQueuesOneNotification()
    {
        RequirePostgres();
        var seeded = await SeedOffer(OfferStatus.Sent);
        var barrier = new UnlockedOfferReadBarrier();
        await using var db1 = NewContext(barrier);
        await using var db2 = NewContext(barrier);
        var queue1 = new CapturingEmailQueue();
        var queue2 = new CapturingEmailQueue();

        var results = await Task.WhenAll(
            MakePublic(db1, queue1).ChooseOption(
                seeded.Token, new ChooseOptionRequest(seeded.OptionIds[0])),
            MakePublic(db2, queue2).ChooseOption(
                seeded.Token, new ChooseOptionRequest(seeded.OptionIds[1])));

        results.Should().ContainSingle(result => result is OkObjectResult);
        results.Should().ContainSingle(result => result is ConflictObjectResult);
        queue1.Recipients.Concat(queue2.Recipients).Should().ContainSingle();

        await using var verify = pg.NewContext();
        var chosen = await verify.Offers.SingleAsync(o => o.Id == seeded.OfferId);
        chosen.ChosenOptionId.Should().NotBeNull();
        seeded.OptionIds.Should().Contain(chosen.ChosenOptionId!.Value);
    }

    [Fact]
    public async Task ConcurrentConfirmation_ConvertsOnceAndWritesOneAudit()
    {
        RequirePostgres();
        var seeded = await SeedOffer(OfferStatus.Chosen);
        var barrier = new UnlockedOfferReadBarrier();
        await using var db1 = NewContext(barrier);
        await using var db2 = NewContext(barrier);

        var results = await Task.WhenAll(
            MakeAdmin(db1).ConfirmBooking(seeded.OfferId),
            MakeAdmin(db2).ConfirmBooking(seeded.OfferId));

        results.Should().OnlyContain(result => result is OkObjectResult);

        await using var verify = pg.NewContext();
        (await verify.DemandLeads.SingleAsync(l => l.Id == seeded.LeadId))
            .Status.Should().Be(DemandLeadStatus.Converted);
        (await verify.AuditLogs.CountAsync(a =>
                a.Action == "offer.booking_confirmed" && a.Target == seeded.OfferId.ToString()))
            .Should().Be(1);
        (await verify.Bookings.CountAsync()).Should().Be(0);
        (await verify.Orders.CountAsync()).Should().Be(0);
    }

    private void RequirePostgres() => Assert.True(pg.Available,
        "PostgreSQL integration database unavailable. Start local PostgreSQL on port 5433 " +
        "or set RUUMLY_TEST_PG before running this focused gate.");

    private async Task<(Guid LeadId, Guid OfferId, string Token, Guid[] OptionIds)> SeedOffer(
        OfferStatus status)
    {
        var leadId = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        var optionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var token = OfferToken.Generate();
        await using var db = pg.NewContext();
        db.DemandLeads.Add(new DemandLead
        {
            Id = leadId,
            Email = $"customer-{leadId:N}@x.ee",
            City = "Tallinn",
            Category = DemandLeadCategory.Moving,
            Language = "en",
            Source = "concierge",
            Status = DemandLeadStatus.Quoted,
            ContactedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        db.Offers.Add(new Offer
        {
            Id = offerId,
            DemandLeadId = leadId,
            Token = token,
            Status = status,
            Language = "en",
            SentAt = DateTime.UtcNow,
            ChosenAt = status == OfferStatus.Chosen ? DateTime.UtcNow : null,
            ChosenOptionId = status == OfferStatus.Chosen ? optionIds[0] : null,
            Options =
            [
                new OfferOption { Id = optionIds[0], Title = "First", SortOrder = 0 },
                new OfferOption { Id = optionIds[1], Title = "Second", SortOrder = 1 },
            ],
        });
        await db.SaveChangesAsync();
        return (leadId, offerId, token, optionIds);
    }

    private RuumlyDbContext NewContext(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseNpgsql(pg.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new RuumlyDbContext(options);
    }

    private static OffersController MakePublic(
        RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static AdminOffersController MakeAdmin(RuumlyDbContext db) =>
        new(db, new CapturingEmailQueue(), new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, "Admin"),
                    ], "test")),
                },
            },
        };
}
