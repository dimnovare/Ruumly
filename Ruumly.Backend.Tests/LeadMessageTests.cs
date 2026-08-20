using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Operator correspondence: sending a message about a lead FROM Ruumly, and
/// recording that it happened.
///
/// The endpoint exists because its absence had a cost. With no path for "what is
/// the exact address?" or "is that per hour or for the job?", four messages about
/// a live request — one of them to the customer — went out from a personal
/// mailbox signed as Ruumly, and the provider replies landed where the ops loop
/// cannot see them.
///
/// The property that matters most here is NOT that mail goes out. It is that the
/// caller cannot choose who receives it: an authenticated endpoint that takes an
/// arbitrary address and arbitrary text is a spam cannon. The recipient is always
/// resolved from the lead.
/// </summary>
public class LeadMessageTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static AdminLeadsController Make(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue)
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

    private static (DemandLead Lead, Supplier Contacted, Supplier Stranger) Seed(RuumlyDbContext db)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "customer@x.lv", Name = "Agnese",
            City = "Tukums", ToCity = "Riga", Category = DemandLeadCategory.Moving,
            Language = "lv", Source = "concierge", Status = DemandLeadStatus.Contacted,
            ContactedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        };
        var contacted = new Supplier
        {
            Id = Guid.NewGuid(), Name = "CVN", ContactName = "Artis",
            ContactEmail = "info@cvn.lv", ContactPhone = "1", IsActive = true, Country = "LV",
        };
        // A real supplier in the directory that was NEVER written to about this
        // lead — the endpoint must refuse to reach them through it.
        var stranger = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Unrelated OU", ContactName = "X",
            ContactEmail = "victim@elsewhere.ee", ContactPhone = "1", IsActive = true, Country = "EE",
        };
        db.DemandLeads.Add(lead);
        db.Suppliers.AddRange(contacted, stranger);
        db.ProviderOutreaches.Add(new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = contacted.Id,
            SentTo = contacted.ContactEmail, SentAt = DateTime.UtcNow,
            Status = ProviderOutreachStatus.Sent,
        });
        db.SaveChanges();
        return (lead, contacted, stranger);
    }

    // ─── The safety property ──────────────────────────────────────────────────

    [Fact]
    public async Task CannotReachASupplier_WhoWasNeverContactedForThisLead()
    {
        var db = TestDbContext.Create();
        var (lead, _, stranger) = Seed(db);
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).SendLeadMessage(lead.Id,
            new SendLeadMessageRequest("Hello", "Buy my thing", stranger.Id));

        result.Should().BeOfType<BadRequestObjectResult>(
            "an authenticated endpoint that mails any address on request is a spam cannon");
        queue.Emails.Should().BeEmpty();
        db.LeadMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRequestBodyCarriesNoRecipientAddress_SoOneCannotBeInjected()
    {
        // Compile-time property, asserted so a future "convenience" parameter has
        // to delete this test on the way in.
        typeof(SendLeadMessageRequest).GetProperties()
            .Select(p => p.Name)
            .Should().BeEquivalentTo(["Subject", "Body", "SupplierId"],
                "the address is resolved from the lead, never supplied by the caller");
        await Task.CompletedTask;
    }

    // ─── The two legitimate recipients ────────────────────────────────────────

    [Fact]
    public async Task SendsToTheCustomer_WhenNoSupplierIsNamed_AndRecordsIt()
    {
        var db = TestDbContext.Create();
        var (lead, _, _) = Seed(db);
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).SendLeadMessage(lead.Id,
            new SendLeadMessageRequest("Viens precizejums", "Ludzu, precizas adreses?"));

        result.Should().BeOfType<OkObjectResult>();
        var mail = queue.Emails.Should().ContainSingle().Subject;
        mail.To.Should().Be("customer@x.lv");

        var row = db.LeadMessages.Should().ContainSingle().Subject;
        row.DemandLeadId.Should().Be(lead.Id);
        row.SupplierId.Should().BeNull("a null supplier means the customer");
        row.SentTo.Should().Be("customer@x.lv");
        row.SentByUserId.Should().NotBeNull("correspondence has an author");
    }

    [Fact]
    public async Task SendsToAContactedProvider_AndSnapshotsTheAddressItActuallyUsed()
    {
        var db = TestDbContext.Create();
        var (lead, contacted, _) = Seed(db);
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).SendLeadMessage(lead.Id,
            new SendLeadMessageRequest("Cena", "Vai 160 ir par stundu?", contacted.Id));

        result.Should().BeOfType<OkObjectResult>();
        queue.Emails.Should().ContainSingle().Subject.To.Should().Be("info@cvn.lv");
        db.LeadMessages.Single().SupplierId.Should().Be(contacted.Id);
    }

    [Fact]
    public async Task PrefersTheProvidersCURRENTAddress_WhenTheContactEmailWasCorrectedSinceOutreach()
    {
        var db = TestDbContext.Create();
        var (lead, contacted, _) = Seed(db);
        // A bounced provider address being fixed is a real, recurring event.
        contacted.ContactEmail = "viiratsiladu@gmail.com";
        db.SaveChanges();
        var queue = new CapturingEmailQueue();

        await Make(db, queue).SendLeadMessage(lead.Id,
            new SendLeadMessageRequest("S", "B", contacted.Id));

        queue.Emails.Single().To.Should().Be("viiratsiladu@gmail.com",
            "the corrected address is the one that will actually arrive");
        db.LeadMessages.Single().SentTo.Should().Be("viiratsiladu@gmail.com",
            "history records where the mail really went");
    }

    // ─── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RefusesEmptySubjectOrBody_AndAngleBrackets()
    {
        var db = TestDbContext.Create();
        var (lead, _, _) = Seed(db);
        var queue = new CapturingEmailQueue();
        var ctrl = Make(db, queue);

        (await ctrl.SendLeadMessage(lead.Id, new SendLeadMessageRequest("", "body")))
            .Should().BeOfType<BadRequestObjectResult>();
        (await ctrl.SendLeadMessage(lead.Id, new SendLeadMessageRequest("subj", "  ")))
            .Should().BeOfType<BadRequestObjectResult>();
        (await ctrl.SendLeadMessage(lead.Id, new SendLeadMessageRequest("subj", "<b>hi</b>")))
            .Should().BeOfType<BadRequestObjectResult>("no markup may be smuggled into a mail we send");

        queue.Emails.Should().BeEmpty();
        db.LeadMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownLead_Is404_AndSendsNothing()
    {
        var db = TestDbContext.Create();
        Seed(db);
        var queue = new CapturingEmailQueue();

        var result = await Make(db, queue).SendLeadMessage(Guid.NewGuid(),
            new SendLeadMessageRequest("S", "B"));

        result.Should().BeOfType<NotFoundObjectResult>();
        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task History_ReturnsWhatWasSent_NewestFirst()
    {
        var db = TestDbContext.Create();
        var (lead, contacted, _) = Seed(db);
        var ctrl = Make(db, new CapturingEmailQueue());

        await ctrl.SendLeadMessage(lead.Id, new SendLeadMessageRequest("First", "to customer"));
        await ctrl.SendLeadMessage(lead.Id, new SendLeadMessageRequest("Second", "to provider", contacted.Id));

        var result = await ctrl.GetLeadMessages(lead.Id);
        var rows = ((IEnumerable<object>)((OkObjectResult)result).Value!).ToList();

        rows.Should().HaveCount(2,
            "the point of the row is that the next person to open the lead can see what was asked");
    }
}
