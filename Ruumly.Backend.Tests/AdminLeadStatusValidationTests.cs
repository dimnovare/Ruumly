using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

/// <summary>
/// PATCH /api/admin/leads/{id} must reject a provided-but-invalid status instead
/// of silently returning 200 with the status unchanged — otherwise an admin typo
/// (e.g. "lost", which is NOT a DemandLeadStatus) looks like it worked.
/// </summary>
public class AdminLeadStatusValidationTests
{
    private static AdminLeadsController MakeAdmin(RuumlyDbContext db) =>
        new(db, TestServices.NoEmail())
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

    private static DemandLead SeedLead(RuumlyDbContext db, DemandLeadStatus status = DemandLeadStatus.New)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", Name = "Mari Maasikas",
            City = "Tallinn", Category = DemandLeadCategory.Warehouse,
            Language = "en", Source = "concierge",
            Status = status, CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        db.SaveChanges();
        return lead;
    }

    [Theory]
    [InlineData("lost")]      // a real DemandLeadStatus in a sibling enum, but NOT this one
    [InlineData("booked")]    // plausible-but-wrong vocabulary
    [InlineData("99")]        // numeric that TryParse accepts but is not a defined member
    public async Task UpdateLead_InvalidStatus_Returns400_AndLeavesStatusUnchanged(string badStatus)
    {
        var db    = TestDbContext.Create();
        var lead  = SeedLead(db, DemandLeadStatus.Contacted);
        var admin = MakeAdmin(db);

        var result = await admin.UpdateLead(lead.Id, new UpdateLeadRequest(Status: badStatus));

        result.Should().BeOfType<BadRequestObjectResult>(
            $"'{badStatus}' is not a DemandLeadStatus — an admin typo must not silently succeed");
        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Contacted,
            "a rejected status change must not mutate the lead");
    }

    [Fact]
    public async Task UpdateLead_ValidStatus_TransitionsAndStampsContactedAt()
    {
        var db    = TestDbContext.Create();
        var lead  = SeedLead(db);   // New
        var admin = MakeAdmin(db);

        var result = await admin.UpdateLead(lead.Id, new UpdateLeadRequest(Status: "Contacted"));

        result.Should().BeOfType<OkObjectResult>();
        var stored = db.DemandLeads.Single();
        stored.Status.Should().Be(DemandLeadStatus.Contacted);
        stored.ContactedAt.Should().NotBeNull("moving out of New stamps the first touch");
    }

    [Fact]
    public async Task UpdateLead_OmittedStatus_LeavesFunnelPositionUntouched()
    {
        var db    = TestDbContext.Create();
        var lead  = SeedLead(db, DemandLeadStatus.Quoted);
        var admin = MakeAdmin(db);

        // AdminNotes-only edit: status omitted → must remain a no-op for status.
        var result = await admin.UpdateLead(lead.Id, new UpdateLeadRequest(AdminNotes: "called them"));

        result.Should().BeOfType<OkObjectResult>();
        var stored = db.DemandLeads.Single();
        stored.Status.Should().Be(DemandLeadStatus.Quoted,
            "omitting status must leave the funnel position untouched");
        stored.AdminNotes.Should().Be("called them");
    }
}
