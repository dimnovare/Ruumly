using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

/// <summary>
/// DELETE /api/admin/leads/{id} — the HARD delete that exists so test/canary
/// rows can be removed from the north-star metrics. Dismissing a lead leaves it
/// in every denominator of /leads/metrics, which is correct for a real request
/// that went nowhere and wrong for a smoke-test row: five canaries in a
/// fifteen-lead dataset is a third of the funnel. These tests pin both halves —
/// the delete cleans up its dependents, and the counts actually drop.
/// </summary>
public class AdminLeadDeleteTests
{
    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Email, $"{role.ToLowerInvariant()}@ruumly.eu"),
        ], "test"));

    private static AdminLeadsController MakeAdmin(RuumlyDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal("Admin") },
            },
        };

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    private static DemandLead Lead(
        string city,
        DemandLeadStatus status = DemandLeadStatus.New,
        DateTime? createdAt = null,
        DateTime? contactedAt = null,
        string? name = null) => new()
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", Name = name,
            City = city, Category = DemandLeadCategory.Moving,
            Language = "et", Source = "concierge",
            Status = status, CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-1),
            ContactedAt = contactedAt,
        };

    private static ProviderOutreach Outreach(Guid leadId) => new()
    {
        Id = Guid.NewGuid(), DemandLeadId = leadId, SupplierId = Guid.NewGuid(),
        SentTo = "partner@x.ee", SentAt = DateTime.UtcNow, Status = ProviderOutreachStatus.Sent,
    };

    // ─── Cleanup of dependent rows ────────────────────────────────────────────

    [Fact]
    public async Task DeleteLead_RemovesTheLeadAndItsProviderOutreachRows()
    {
        var db     = TestDbContext.Create();
        var canary = Lead("Kunda", name: "CANARY TEST");
        var real   = Lead("Tallinn", name: "Mari Maasikas");
        db.DemandLeads.AddRange(canary, real);
        db.ProviderOutreaches.AddRange(Outreach(canary.Id), Outreach(canary.Id), Outreach(real.Id));
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).DeleteLead(canary.Id);

        result.Should().BeOfType<NoContentResult>("a successful hard delete returns 204");
        db.DemandLeads.Should().ContainSingle().Which.Id.Should().Be(real.Id);
        db.ProviderOutreaches.Should().ContainSingle(
            "the deleted lead's outreach rows must go with it — an orphan would violate the FK")
            .Which.DemandLeadId.Should().Be(real.Id);
    }

    [Fact]
    public async Task DeleteLead_RemovesItsOffersAndOfferOptions()
    {
        var db     = TestDbContext.Create();
        var canary = Lead("Tartu", name: "SMOKE TEST — lead-ops canary");
        var real   = Lead("Tallinn");
        db.DemandLeads.AddRange(canary, real);

        var canaryOffer = new Offer { Id = Guid.NewGuid(), DemandLeadId = canary.Id, Token = "canary-token" };
        var realOffer   = new Offer { Id = Guid.NewGuid(), DemandLeadId = real.Id,   Token = "real-token" };
        db.Offers.AddRange(canaryOffer, realOffer);
        db.OfferOptions.AddRange(
            new OfferOption { Id = Guid.NewGuid(), OfferId = canaryOffer.Id, Title = "Option A" },
            new OfferOption { Id = Guid.NewGuid(), OfferId = canaryOffer.Id, Title = "Option B" },
            new OfferOption { Id = Guid.NewGuid(), OfferId = realOffer.Id,   Title = "Keep me" });
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).DeleteLead(canary.Id);

        result.Should().BeOfType<NoContentResult>();
        db.Offers.Should().ContainSingle("the deleted lead's offer must go with it")
            .Which.Id.Should().Be(realOffer.Id);
        db.OfferOptions.Should().ContainSingle("options hang off the deleted offer")
            .Which.Title.Should().Be("Keep me");
    }

    [Fact]
    public async Task DeleteLead_WritesAnAuditTrailCarryingCityAndCategory()
    {
        var db     = TestDbContext.Create();
        var canary = Lead("Kunda", name: "CANARY TEST");
        db.DemandLeads.Add(canary);
        await db.SaveChangesAsync();

        await MakeAdmin(db).DeleteLead(canary.Id);

        var entry = db.AuditLogs.Should().ContainSingle().Subject;
        entry.Action.Should().Be("lead.deleted");
        entry.Target.Should().Be(canary.Id.ToString());
        entry.Detail.Should().Contain("Kunda").And.Contain("moving",
            "the audit trail is all that survives a hard delete — it must say what was destroyed");
    }

    // ─── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteLead_UnknownId_Returns404()
    {
        var db = TestDbContext.Create();
        db.DemandLeads.Add(Lead("Tallinn"));
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).DeleteLead(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>("deleting a lead that isn't there is a 404");
        db.DemandLeads.Should().ContainSingle("a 404 must not touch anything");
        db.AuditLogs.Should().BeEmpty("nothing was deleted, so nothing is audited");
    }

    [Fact]
    public async Task DeleteLead_NonAdminCaller_IsRejected()
    {
        var method = typeof(AdminLeadsController).GetMethod(nameof(AdminLeadsController.DeleteLead))!;
        var authData = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(typeof(AdminLeadsController).GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .ToList();

        authData.Should().NotBeEmpty("an unauthenticated caller must never reach a hard delete");
        authData.Should().Contain(a => a.Roles == "Admin",
            "the hard delete is Admin-only, mirroring suppliers/{id}/purge");

        // Evaluate the real policy the attributes produce rather than trusting the string.
        await using var services = new ServiceCollection()
            .AddLogging().AddAuthorization().BuildServiceProvider();
        var policy = (await AuthorizationPolicy.CombineAsync(
            services.GetRequiredService<IAuthorizationPolicyProvider>(), authData))!;
        var authorization = services.GetRequiredService<IAuthorizationService>();

        (await authorization.AuthorizeAsync(Principal("Provider"), null, policy))
            .Succeeded.Should().BeFalse("a partner must not be able to erase demand data");
        (await authorization.AuthorizeAsync(Principal("Customer"), null, policy))
            .Succeeded.Should().BeFalse("a customer must not be able to erase demand data");
        (await authorization.AuthorizeAsync(Principal("Admin"), null, policy))
            .Succeeded.Should().BeTrue("ops needs this to clear canary rows");
    }

    // ─── The point of the feature: honest denominators ────────────────────────

    [Fact]
    public async Task DeleteLead_DropsTheLeadOutOfTheOpsMetrics()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        // Two genuine, contacted requests plus one canary that never was one.
        db.DemandLeads.AddRange(
            Lead("Tallinn", DemandLeadStatus.Contacted, now.AddDays(-2), now.AddDays(-2).AddMinutes(20)),
            Lead("Tallinn", DemandLeadStatus.Contacted, now.AddDays(-3), now.AddDays(-3).AddMinutes(40)),
            Lead("Kunda",   DemandLeadStatus.New,       now.AddDays(-1), name: "CANARY TEST"));
        await db.SaveChangesAsync();
        var canaryId = db.DemandLeads.Single(d => d.Name == "CANARY TEST").Id;

        var before = (await MakeAdmin(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;
        Prop(before, "requestsThisWeek").Should().Be(3);
        Prop(before, "requests30d").Should().Be(3);
        ((double)Prop(before, "contactRate30d")!).Should().BeApproximately(2.0 / 3.0, 1e-9,
            "the canary is inflating the denominator");

        (await MakeAdmin(db).DeleteLead(canaryId)).Should().BeOfType<NoContentResult>();

        var after = (await MakeAdmin(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;
        Prop(after, "requestsThisWeek").Should().Be(2,
            "a deleted canary must stop counting as a qualified request this week");
        Prop(after, "requests30d").Should().Be(2,
            "…and must leave the 30-day base too");
        ((double)Prop(after, "contactRate30d")!).Should().Be(1.0,
            "with the fake row gone, both real requests were contacted — the rate is honest");
    }

    [Fact]
    public async Task DeleteLead_DoesNotDisturbTheMetricsOfSurroundingLeads()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        var keep   = Lead("Tallinn", DemandLeadStatus.Quoted, now.AddDays(-2), now.AddDays(-2).AddMinutes(10));
        keep.QuotedPrice = 250m;
        var canary = Lead("Kunda", DemandLeadStatus.New, now.AddDays(-1), name: "Test Klient");
        db.DemandLeads.AddRange(keep, canary);
        db.ProviderOutreaches.AddRange(Outreach(keep.Id), Outreach(canary.Id));
        await db.SaveChangesAsync();

        await MakeAdmin(db).DeleteLead(canary.Id);

        var after = (await MakeAdmin(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;
        Prop(after, "requests30d").Should().Be(1);
        ((double)Prop(after, "quoteRate30d")!).Should().Be(1.0,
            "the surviving quoted lead still counts, in both numerator and denominator");
        Prop(after, "medianFirstResponseMinutes").Should().Be(10,
            "deleting an uncontacted canary must not change the surviving response time");
        db.ProviderOutreaches.Should().ContainSingle().Which.DemandLeadId.Should().Be(keep.Id);
    }
}
