using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests.Integration;

/// <summary>
/// The hard delete of a demand lead, run against the REAL Npgsql provider with
/// the full FK graph enforced. EF InMemory ignores foreign keys entirely, so it
/// cannot fail the way production would: a missed dependent (outreach row,
/// offer, offer option) is a 23503 constraint violation there and a silent pass
/// here. This is the gate that says the prod cleanup will actually work.
/// </summary>
[Collection("Postgres integration")]
public class LeadDeleteNpgsqlTests(PostgresIntegrationFixture pg)
{
    private static AdminLeadsController MakeAdmin(RuumlyDbContext db) =>
        new(db)
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

    [Fact]
    public async Task DeleteLead_RemovesTheWholeDependentGraph_UnderNpgsql()
    {
        Assert.True(pg.Available,
            "PostgreSQL integration database unavailable. Start local PostgreSQL on port 5433 " +
            "or set RUUMLY_TEST_PG before running this focused gate.");

        var canaryId   = Guid.NewGuid();
        var keeperId   = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var canaryOfferId = Guid.NewGuid();
        var keeperOfferId = Guid.NewGuid();

        await using (var seed = pg.NewContext())
        {
            DemandLead Lead(Guid id, string city, string name) => new()
            {
                Id = id, Email = "customer@x.ee", Name = name, City = city,
                Category = DemandLeadCategory.Moving, Language = "et", Source = "concierge",
                Status = DemandLeadStatus.Contacted, ContactedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };

            seed.DemandLeads.AddRange(
                Lead(canaryId, "Kunda", "CANARY TEST"),
                Lead(keeperId, "Tallinn", "Mari Maasikas"));

            seed.Suppliers.Add(new Supplier
            {
                Id = supplierId, Name = "Kolija OÜ",
                RegistryCode = Guid.NewGuid().ToString("N"),
                ContactName = "Provider",
                ContactEmail = $"provider-{Guid.NewGuid():N}@x.ee",
                ContactPhone = "1", IsActive = true,
            });

            // Both leads carry the full dependent graph: outreach (incl. a
            // tokenized quote row) plus an offer with options.
            seed.ProviderOutreaches.AddRange(
                new ProviderOutreach
                {
                    Id = Guid.NewGuid(), DemandLeadId = canaryId, SupplierId = supplierId,
                    SentTo = "provider@x.ee", SentAt = DateTime.UtcNow,
                    Status = ProviderOutreachStatus.Replied,
                    QuoteToken = Guid.NewGuid().ToString("N"),
                    QuotedAmount = 180m, QuotedUnit = "job", QuotedAt = DateTime.UtcNow,
                },
                new ProviderOutreach
                {
                    Id = Guid.NewGuid(), DemandLeadId = keeperId, SupplierId = supplierId,
                    SentTo = "provider@x.ee", SentAt = DateTime.UtcNow,
                    Status = ProviderOutreachStatus.Sent,
                    QuoteToken = Guid.NewGuid().ToString("N"),
                });

            seed.Offers.AddRange(
                new Offer { Id = canaryOfferId, DemandLeadId = canaryId, Token = Guid.NewGuid().ToString("N") },
                new Offer { Id = keeperOfferId, DemandLeadId = keeperId, Token = Guid.NewGuid().ToString("N") });
            seed.OfferOptions.AddRange(
                new OfferOption { Id = Guid.NewGuid(), OfferId = canaryOfferId, Title = "Canary option", SupplierId = supplierId },
                new OfferOption { Id = Guid.NewGuid(), OfferId = keeperOfferId, Title = "Keeper option", SupplierId = supplierId });

            await seed.SaveChangesAsync();
        }

        await using (var act = pg.NewContext())
        {
            var result = await MakeAdmin(act).DeleteLead(canaryId);
            result.Should().BeOfType<NoContentResult>(
                "the delete must survive real FK enforcement, not 500 on a 23503");
        }

        await using var verify = pg.NewContext();
        (await verify.DemandLeads.AnyAsync(d => d.Id == canaryId)).Should().BeFalse();
        (await verify.ProviderOutreaches.AnyAsync(o => o.DemandLeadId == canaryId)).Should().BeFalse();
        (await verify.Offers.AnyAsync(o => o.DemandLeadId == canaryId)).Should().BeFalse();
        (await verify.OfferOptions.AnyAsync(o => o.OfferId == canaryOfferId)).Should().BeFalse();

        // The neighbouring lead's graph is untouched.
        (await verify.DemandLeads.AnyAsync(d => d.Id == keeperId)).Should().BeTrue();
        (await verify.ProviderOutreaches.CountAsync(o => o.DemandLeadId == keeperId)).Should().Be(1);
        (await verify.OfferOptions.CountAsync(o => o.OfferId == keeperOfferId)).Should().Be(1);
        (await verify.Suppliers.AnyAsync(s => s.Id == supplierId))
            .Should().BeTrue("deleting demand must never delete the partner it was routed to");

        var audit = await verify.AuditLogs
            .Where(a => a.Action == "lead.deleted" && a.Target == canaryId.ToString())
            .SingleAsync();
        audit.Detail.Should().Contain("Kunda").And.Contain("moving");
    }

    [Fact]
    public async Task DeleteLead_UnknownId_Returns404_UnderNpgsql()
    {
        Assert.True(pg.Available,
            "PostgreSQL integration database unavailable. Start local PostgreSQL on port 5433 " +
            "or set RUUMLY_TEST_PG before running this focused gate.");

        var missingId = Guid.NewGuid();
        await using var db = pg.NewContext();
        var result = await MakeAdmin(db).DeleteLead(missingId);

        result.Should().BeOfType<NotFoundObjectResult>();
        (await db.AuditLogs.AnyAsync(a => a.Target == missingId.ToString()))
            .Should().BeFalse("a 404 writes no audit row");
    }
}
