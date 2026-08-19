using System.Collections;
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
/// The provider-side half of the lead queue: what GET /api/admin/leads now says
/// about who was asked and who answered, and who the never-answering providers
/// are (GET /api/admin/leads/provider-silence).
///
/// All of this exists because of one morning's reading: five Viljandi storage
/// requests reached 18 providers and produced no reply of any kind, and the
/// workspace could not show that without expanding every row one at a time. The
/// tests below pin the two things that make the numbers usable — that silence,
/// delivery failure and answers are counted as three separate facts, and that a
/// missing delivery receipt is never reported as a failed delivery.
/// </summary>
public class AdminLeadQueueTests
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
                    ], "test")),
                },
            },
        };

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    private static object Body(IActionResult result) =>
        result.Should().BeOfType<OkObjectResult>().Subject.Value!;

    /// <summary>The per-lead outreach summary for one lead id.</summary>
    private static object OutreachFor(object body, Guid leadId)
    {
        var map = (IDictionary<string, object>)Prop(body, "outreach")!;
        map.Should().ContainKey(leadId.ToString(),
            "every lead on the page gets a summary, including the ones nobody was asked about");
        return map[leadId.ToString()];
    }

    private static List<object> Items(object body) =>
        ((IEnumerable)Prop(body, "items")!).Cast<object>().ToList();

    private static DemandLead Lead(
        RuumlyDbContext db, DateTime createdAt,
        DemandLeadStatus status = DemandLeadStatus.Contacted, string city = "Viljandi")
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", City = city,
            Category = DemandLeadCategory.Warehouse, Language = "et",
            Source = "concierge", Status = status, CreatedAt = createdAt,
        };
        db.DemandLeads.Add(lead);
        return lead;
    }

    private static ProviderOutreach Ask(
        RuumlyDbContext db, DemandLead lead, Guid supplierId, DateTime sentAt,
        ProviderOutreachStatus status = ProviderOutreachStatus.Sent,
        DateTime? deliveredAt = null, DateTime? quotedAt = null)
    {
        var row = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = supplierId,
            SentTo = $"{supplierId:N}@provider.ee", SentAt = sentAt,
            Status = status, DeliveredAt = deliveredAt, QuotedAt = quotedAt,
        };
        db.ProviderOutreaches.Add(row);
        return row;
    }

    // ─── Per-lead outreach summary ────────────────────────────────────────────

    [Fact]
    public async Task GetLeads_SummarisesEachLeadsOutreach_WithoutExpandingTheRow()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;
        var lead = Lead(db, now.AddDays(-6));

        // The Viljandi shape in miniature: several asks, one answer, one dead
        // address, and one row whose delivery we simply never recorded.
        Ask(db, lead, Guid.NewGuid(), now.AddDays(-6), deliveredAt: now.AddDays(-6));
        Ask(db, lead, Guid.NewGuid(), now.AddDays(-5), ProviderOutreachStatus.Replied, deliveredAt: now.AddDays(-5));
        Ask(db, lead, Guid.NewGuid(), now.AddDays(-4), ProviderOutreachStatus.Bounced);
        Ask(db, lead, Guid.NewGuid(), now.AddDays(-3));   // no receipt at all
        await db.SaveChangesAsync();

        var summary = OutreachFor(Body(await MakeAdmin(db).GetLeads()), lead.Id);

        Prop(summary, "asked").Should().Be(4);
        Prop(summary, "answered").Should().Be(1, "only the Replied row is a provider speaking");
        Prop(summary, "failed").Should().Be(1, "the bounce is the only proven non-delivery");
        Prop(summary, "silent").Should().Be(2, "asked - answered - failed, so the three never overlap");
        Prop(summary, "delivered").Should().Be(2);
        // The heart of it: two rows have no receipt (the bounce and the one we
        // never heard about), but only ONE of them is a delivery failure.
        Prop(summary, "deliveryUnknown").Should().Be(1,
            "a row with no receipt and no bounce verdict is unknown, never undelivered");
        Prop(summary, "lastSentAt").Should().Be(now.AddDays(-3));
    }

    [Fact]
    public async Task GetLeads_LeadNobodyWasAskedAbout_ReportsZeroAndNoAge()
    {
        var db   = TestDbContext.Create();
        var lead = Lead(db, DateTime.UtcNow.AddDays(-2), DemandLeadStatus.New);
        await db.SaveChangesAsync();

        var summary = OutreachFor(Body(await MakeAdmin(db).GetLeads()), lead.Id);

        Prop(summary, "asked").Should().Be(0);
        Prop(summary, "silent").Should().Be(0);
        // Null, not the lead's own age: "nobody has been asked" and "asked a long
        // time ago" are opposite situations and must not render the same way.
        Prop(summary, "lastSentAt").Should().BeNull();
    }

    [Fact]
    public async Task GetLeads_NoAnswerStatus_CountsAsSilence_NotAsAnAnswer()
    {
        var db   = TestDbContext.Create();
        var now  = DateTime.UtcNow;
        var lead = Lead(db, now.AddDays(-9));
        // NoAnswer is an ADMIN recording that nobody came back. Counting it as an
        // answer would erase precisely the fact this whole view exists to show.
        Ask(db, lead, Guid.NewGuid(), now.AddDays(-9), ProviderOutreachStatus.NoAnswer);
        await db.SaveChangesAsync();

        var summary = OutreachFor(Body(await MakeAdmin(db).GetLeads()), lead.Id);

        Prop(summary, "answered").Should().Be(0);
        Prop(summary, "silent").Should().Be(1);
    }

    [Fact]
    public async Task GetLeads_CountsOpenAsksOnly_AsBlocked()
    {
        var db   = TestDbContext.Create();
        var now  = DateTime.UtcNow;
        var lead = Lead(db, now.AddDays(-2));
        var open   = Ask(db, lead, Guid.NewGuid(), now.AddDays(-2), ProviderOutreachStatus.NeedsInfo);
        var closed = Ask(db, lead, Guid.NewGuid(), now.AddDays(-2), ProviderOutreachStatus.Sent);
        db.ProviderInfoRequests.AddRange(
            new ProviderInfoRequest
            {
                Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = open.SupplierId,
                ProviderOutreachId = open.Id, CreatedAt = now.AddDays(-1),
            },
            // Already answered: it must stop counting as a block the moment ops
            // resolves it, or the badge means "was blocked once".
            new ProviderInfoRequest
            {
                Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = closed.SupplierId,
                ProviderOutreachId = closed.Id, CreatedAt = now.AddDays(-2),
                ResolvedAt = now.AddHours(-3),
            });
        await db.SaveChangesAsync();

        var body = Body(await MakeAdmin(db).GetLeads());

        Prop(OutreachFor(body, lead.Id), "blocked").Should().Be(1);
        Prop(Prop(body, "queues")!, "blocked").Should().Be(1);
    }

    // ─── Queue counts and the filters they label ──────────────────────────────

    [Fact]
    public async Task GetLeads_StalledIsSilence_NotAge()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        // Old and never asked about — the case a date-sorted list buries.
        var neverAsked = Lead(db, now.AddDays(-8), DemandLeadStatus.New);
        // Old, asked long ago, nobody said a word.
        var goneQuiet = Lead(db, now.AddDays(-9));
        Ask(db, goneQuiet, Guid.NewGuid(), now.AddDays(-7));
        // Old, but we chased them yesterday: the ball is legitimately in their court.
        var chasedYesterday = Lead(db, now.AddDays(-9));
        Ask(db, chasedYesterday, Guid.NewGuid(), now.AddDays(-1));
        // Old and quiet from most, but one provider DID answer.
        var oneAnswer = Lead(db, now.AddDays(-9));
        Ask(db, oneAnswer, Guid.NewGuid(), now.AddDays(-7));
        Ask(db, oneAnswer, Guid.NewGuid(), now.AddDays(-7), ProviderOutreachStatus.Replied);
        // Fresh: not stalled however few people we have asked.
        Lead(db, now.AddHours(-2), DemandLeadStatus.New);
        // Closed: a lost request is not waiting for anything.
        var closed = Lead(db, now.AddDays(-20), DemandLeadStatus.Dismissed);
        Ask(db, closed, Guid.NewGuid(), now.AddDays(-20));
        await db.SaveChangesAsync();

        var body = Body(await MakeAdmin(db).GetLeads(queue: "stalled"));
        var ids  = Items(body).Select(i => (Guid)Prop(i, "Id")!).ToList();

        ids.Should().BeEquivalentTo([neverAsked.Id, goneQuiet.Id]);
        // Longest-waiting first — in a queue the oldest row is the point.
        ids[0].Should().Be(goneQuiet.Id, "the older of the two stalled requests leads the queue");
        Prop(body, "total").Should().Be(2);
    }

    [Fact]
    public async Task GetLeads_QueueCounts_MatchWhatTheQueueFilterReturns()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        Lead(db, now.AddHours(-1), DemandLeadStatus.New);          // needs response, too fresh to stall
        var oldNew = Lead(db, now.AddDays(-10), DemandLeadStatus.New); // needs response AND stalled
        var blocked = Lead(db, now.AddDays(-1));
        var row = Ask(db, blocked, Guid.NewGuid(), now.AddDays(-1), ProviderOutreachStatus.NeedsInfo);
        db.ProviderInfoRequests.Add(new ProviderInfoRequest
        {
            Id = Guid.NewGuid(), DemandLeadId = blocked.Id, SupplierId = row.SupplierId,
            ProviderOutreachId = row.Id, CreatedAt = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var admin  = MakeAdmin(db);
        var queues = Prop(Body(await admin.GetLeads()), "queues")!;

        // A chip may only carry a number if the number is true of what clicking
        // it shows. These four assertions are that contract.
        Prop(queues, "needsResponse").Should().Be(
            Prop(Body(await admin.GetLeads(queue: "needsresponse")), "total"));
        Prop(queues, "blocked").Should().Be(
            Prop(Body(await admin.GetLeads(queue: "blocked")), "total"));
        Prop(queues, "stalled").Should().Be(
            Prop(Body(await admin.GetLeads(queue: "stalled")), "total"));

        Prop(queues, "needsResponse").Should().Be(2);
        Prop(queues, "blocked").Should().Be(1);
        Prop(queues, "stalled").Should().Be(1);
        Items(Body(await admin.GetLeads(queue: "stalled")))
            .Select(i => Prop(i, "Id")).Should().Equal(oldNew.Id);
        // The threshold travels with the count so the row badges cannot disagree
        // with the chip about what "stalled" means.
        Prop(queues, "stalledAfterDays").Should().Be(3);
    }

    [Fact]
    public async Task GetLeads_StalledFlagOnEachRow_AgreesWithTheQueueCount()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        // The same six shapes as the queue test, so any drift between the SQL
        // predicate and the in-memory per-row rule shows up as a mismatch rather
        // than as a badge nobody trusts.
        Lead(db, now.AddDays(-8), DemandLeadStatus.New);                       // stalled: never asked
        var quiet = Lead(db, now.AddDays(-9));
        Ask(db, quiet, Guid.NewGuid(), now.AddDays(-7));                       // stalled: gone quiet
        var chased = Lead(db, now.AddDays(-9));
        Ask(db, chased, Guid.NewGuid(), now.AddDays(-1));                      // chased yesterday
        var answered = Lead(db, now.AddDays(-9));
        Ask(db, answered, Guid.NewGuid(), now.AddDays(-7), ProviderOutreachStatus.Replied);
        Lead(db, now.AddHours(-2), DemandLeadStatus.New);                      // too fresh
        Lead(db, now.AddDays(-20), DemandLeadStatus.Dismissed);                // closed
        await db.SaveChangesAsync();

        var body    = Body(await MakeAdmin(db).GetLeads());
        var map     = (IDictionary<string, object>)Prop(body, "outreach")!;
        var flagged = map.Values.Count(v => (bool)Prop(v, "stalled")!);

        flagged.Should().Be((int)Prop(Prop(body, "queues")!, "stalled")!,
            "the row badge and the chip must be the same definition, not two");
        flagged.Should().Be(2);
    }

    [Fact]
    public async Task GetLeads_QueueCounts_DescribeTheFilteredWorld_NotTheSelectedQueue()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;
        Lead(db, now.AddHours(-1), DemandLeadStatus.New);
        Lead(db, now.AddDays(-10), DemandLeadStatus.New);
        await db.SaveChangesAsync();

        // Selecting one queue must not shrink the others' counts to match it —
        // otherwise every chip reads as whatever is currently switched on.
        var queues = Prop(Body(await MakeAdmin(db).GetLeads(queue: "stalled")), "queues")!;

        Prop(queues, "needsResponse").Should().Be(2);
        Prop(queues, "stalled").Should().Be(1);
    }

    [Fact]
    public async Task GetLeads_NeedsResponseBoolean_StillSelectsTheSameQueue()
    {
        var db = TestDbContext.Create();
        Lead(db, DateTime.UtcNow.AddDays(-2), DemandLeadStatus.New);
        Lead(db, DateTime.UtcNow.AddDays(-2), DemandLeadStatus.Quoted);
        await db.SaveChangesAsync();

        // The cockpit chips and the alert emails link with ?needsResponse=1.
        var body = Body(await MakeAdmin(db).GetLeads(needsResponse: true));
        Prop(body, "total").Should().Be(1);
    }

    [Fact]
    public async Task GetLeads_UnknownQueueName_IsIgnoredRatherThanRejected()
    {
        var db = TestDbContext.Create();
        Lead(db, DateTime.UtcNow.AddDays(-2), DemandLeadStatus.New);
        await db.SaveChangesAsync();

        // A stale bookmark must show the list, not a 400 to decode mid-loop.
        var body = Body(await MakeAdmin(db).GetLeads(queue: "whatever"));
        Prop(body, "total").Should().Be(1);
    }

    // ─── Provider silence roll-call ───────────────────────────────────────────

    [Fact]
    public async Task GetProviderSilence_ListsOnlyProvidersWhoNeverAnsweredAnything()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;
        var lead = Lead(db, now.AddDays(-10));

        var neverSpoke   = Guid.NewGuid();
        var repliedOnce  = Guid.NewGuid();
        var quotedOnce   = Guid.NewGuid();
        var declinedOnce = Guid.NewGuid();

        Ask(db, lead, neverSpoke, now.AddDays(-10));
        Ask(db, lead, neverSpoke, now.AddDays(-4), ProviderOutreachStatus.NoAnswer);
        // One reply, ever, is enough to be off this list.
        Ask(db, lead, repliedOnce, now.AddDays(-9));
        Ask(db, lead, repliedOnce, now.AddDays(-8), ProviderOutreachStatus.Replied);
        // A submitted price is an answer even if nobody moved the status.
        Ask(db, lead, quotedOnce, now.AddDays(-7), quotedAt: now.AddDays(-6));
        // "No" is an answer too.
        Ask(db, lead, declinedOnce, now.AddDays(-6), ProviderOutreachStatus.Declined);
        await db.SaveChangesAsync();

        var body  = Body(await MakeAdmin(db).GetProviderSilence());
        var items = ((IEnumerable)Prop(body, "items")!).Cast<object>().ToList();

        items.Select(i => Prop(i, "supplierId")).Should().Equal(neverSpoke);
        Prop(body, "providersContacted").Should().Be(4);
        Prop(body, "providersSilent").Should().Be(1);
        Prop(body, "asksUnanswered").Should().Be(2);
    }

    [Fact]
    public async Task GetProviderSilence_SeparatesDeliveredFromUnknownFromBounced()
    {
        var db   = TestDbContext.Create();
        var now  = DateTime.UtcNow;
        var lead = Lead(db, now.AddDays(-10));
        var quiet = Guid.NewGuid();
        db.Suppliers.Add(new Supplier
        {
            Id = quiet, Name = "Viljandi Ladu OÜ", ContactEmail = "info@ladu.ee",
            IsActive = true,
        });

        Ask(db, lead, quiet, now.AddDays(-10), deliveredAt: now.AddDays(-10));
        Ask(db, lead, quiet, now.AddDays(-9));                                    // no receipt
        Ask(db, lead, quiet, now.AddDays(-8), ProviderOutreachStatus.Bounced);    // proven failure
        await db.SaveChangesAsync();

        var item = ((IEnumerable)Prop(Body(await MakeAdmin(db).GetProviderSilence()), "items")!)
            .Cast<object>().Single();

        Prop(item, "supplierName").Should().Be("Viljandi Ladu OÜ");
        Prop(item, "asked").Should().Be(3);
        Prop(item, "delivered").Should().Be(1);
        Prop(item, "deliveryFailed").Should().Be(1);
        // Three different verdicts needing three different actions — a silence
        // we know landed, a dead address, and a row we know nothing about. The
        // unknown one must never be folded into either of the others: the counts
        // add back up to `asked` exactly once.
        Prop(item, "deliveryUnknown").Should().Be(1,
            "only the receipt-less non-bounce is unknown — a bounce is a KNOWN non-delivery");
    }

    [Fact]
    public async Task GetProviderSilence_OrdersByWastedAsks_ThenColdest()
    {
        var db   = TestDbContext.Create();
        var now  = DateTime.UtcNow;
        var lead = Lead(db, now.AddDays(-30));

        var many   = Guid.NewGuid();
        var oldOne = Guid.NewGuid();
        var newOne = Guid.NewGuid();
        Ask(db, lead, many, now.AddDays(-5));
        Ask(db, lead, many, now.AddDays(-4));
        Ask(db, lead, oldOne, now.AddDays(-20));
        Ask(db, lead, newOne, now.AddDays(-2));
        await db.SaveChangesAsync();

        var ids = ((IEnumerable)Prop(Body(await MakeAdmin(db).GetProviderSilence()), "items")!)
            .Cast<object>().Select(i => Prop(i, "supplierId")).ToList();

        // Most asks wasted first, then the coldest — the order in which a founder
        // decides who to drop and who to phone.
        ids.Should().Equal(many, oldOne, newOne);
    }

    [Fact]
    public async Task GetProviderSilence_NothingSentYet_ReportsEmptyRatherThanBlaming()
    {
        var db = TestDbContext.Create();
        Lead(db, DateTime.UtcNow.AddDays(-1), DemandLeadStatus.New);
        await db.SaveChangesAsync();

        var body = Body(await MakeAdmin(db).GetProviderSilence());

        Prop(body, "providersContacted").Should().Be(0);
        Prop(body, "providersSilent").Should().Be(0);
        ((IEnumerable)Prop(body, "items")!).Cast<object>().Should().BeEmpty();
    }
}
