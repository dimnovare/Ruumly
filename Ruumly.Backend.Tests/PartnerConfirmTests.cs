using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Jobs;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Partner confirmation: one letter with three buttons, a 14-day reminder, a
/// 7-day expiry. The properties that matter most: nothing is sent without an
/// explicit non-dry-run call, an opted-out business is never mailed or changed,
/// a link scanner opening the button URL records nothing, and the follow-up job
/// is inert until the founder switches it on.
/// </summary>
public class PartnerConfirmTests
{
    private sealed class Queue : IBackgroundEmailQueue
    {
        public List<(TimeSpan Delay, string To, string Subject, string Text, string? Html)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((TimeSpan.Zero, to, subject, textBody, htmlBody));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((TimeSpan.Zero, to, subject, textBody, htmlBody));
        public void EnqueueEmailAfter(TimeSpan delay, string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((delay, to, subject, textBody, htmlBody));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private sealed class AllDomainsOk : IMailDomainVerifier
    {
        public Task<IReadOnlySet<string>> FindUndeliverableAsync(IEnumerable<string> domains, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://ruumly.eu" }).Build();

    private static Supplier Provider(string name, string email, string country = "EE") => new()
    {
        Id = Guid.NewGuid(), Name = name, ContactEmail = email, Country = country, IsActive = true,
        IsDirectoryListing = true,
    };

    private static AdminPartnerConfirmController Admin(TestDbContext db, Queue q) =>
        new(db, q, new AllDomainsOk(), Config, NullLogger<AdminPartnerConfirmController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Email, "admin@ruumly.eu"), new Claim(ClaimTypes.Role, "Admin")], "test")),
                },
            },
        };

    private static PartnerConfirmController Public(TestDbContext db, Queue q) =>
        new(db, q, NullLogger<PartnerConfirmController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static PartnerConfirmFollowUpJob Job(TestDbContext db, Queue q) =>
        new(db, q, Config, NullLogger<PartnerConfirmFollowUpJob>.Instance);

    private static async Task TurnOn(TestDbContext db)
    {
        db.PlatformSettings.Add(new PlatformSetting { Key = PartnerConfirm.AutomationSetting, Value = "on" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Campaign_requires_explicit_ids_and_dry_run_sends_nothing()
    {
        var db = TestDbContext.Create();
        var q = new Queue();
        var a = Provider("Kolimisabi OÜ", "info@kolimisabi.ee");
        db.Suppliers.Add(a);
        await db.SaveChangesAsync();

        (await Admin(db, q).Campaign(new PartnerConfirmCampaignRequest(null), default))
            .Should().BeOfType<BadRequestObjectResult>();

        var res = await Admin(db, q).Campaign(new PartnerConfirmCampaignRequest([a.Id]), default);
        res.Should().BeOfType<OkObjectResult>();
        q.Emails.Should().BeEmpty();
        (await db.Suppliers.SingleAsync()).PartnerConfirmSentAt.Should().BeNull();
    }

    [Fact]
    public async Task Live_campaign_skips_opt_outs_and_inactive_and_sends_one_letter_per_inbox()
    {
        var db = TestDbContext.Create();
        var q = new Queue();
        var a = Provider("VIADA Baltija AS — DUS Rīga", "info@viada.lv", "LV");
        var b = Provider("VIADA Baltija AS — DUS Daugavpils", "INFO@viada.lv ", "LV");
        var opted = Provider("Sacred OÜ", "stop@sacred.ee");
        opted.MarketingOptOutAt = DateTime.UtcNow.AddDays(-30);
        var inactive = Provider("Gone OÜ", "gone@gone.ee");
        inactive.IsActive = false;
        db.Suppliers.AddRange(a, b, opted, inactive);
        await db.SaveChangesAsync();

        await Admin(db, q).Campaign(new PartnerConfirmCampaignRequest([a.Id, b.Id, opted.Id, inactive.Id], DryRun: false), default);

        q.Emails.Should().ContainSingle();
        var mail = q.Emails[0];
        mail.To.Should().Be("info@viada.lv");
        mail.Subject.Should().Contain("VIADA Baltija");
        mail.Html.Should().Contain("/api/partner-confirm/").And.Contain("?a=yes").And.Contain("?a=business").And.Contain("?a=no");
        mail.Html.Should().Contain("17527757");
        (await db.Suppliers.FindAsync(a.Id))!.PartnerConfirmSentAt.Should().NotBeNull();
        (await db.Suppliers.FindAsync(b.Id))!.PartnerConfirmSentAt.Should().NotBeNull();
        var o = (await db.Suppliers.FindAsync(opted.Id))!;
        o.PartnerConfirmSentAt.Should().BeNull();
        o.PartnerConfirmToken.Should().BeNull();
        (await db.Suppliers.FindAsync(inactive.Id))!.PartnerConfirmSentAt.Should().BeNull();

        // A second run never re-mails.
        q.Emails.Clear();
        await Admin(db, q).Campaign(new PartnerConfirmCampaignRequest([a.Id, b.Id], DryRun: false), default);
        q.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task Opening_the_button_link_records_nothing_but_the_post_does_and_answers_for_the_whole_inbox()
    {
        var db = TestDbContext.Create();
        var q = new Queue();
        var a = Provider("Branch A", "one@inbox.ee");
        var b = Provider("Branch B", "one@inbox.ee");
        foreach (var s in new[] { a, b }) { s.PartnerConfirmSentAt = DateTime.UtcNow; s.PartnerConfirmToken = PartnerConfirm.NewToken(); }
        db.Suppliers.AddRange(a, b);
        await db.SaveChangesAsync();

        var page = await Public(db, q).Show(a.PartnerConfirmToken!, "business", default);
        page.Should().BeOfType<ContentResult>().Which.Content.Should().Contain("method=\"post\"");
        (await db.Suppliers.FindAsync(a.Id))!.PartnerConfirmAnswer.Should().BeNull();

        await Public(db, q).Confirm(a.PartnerConfirmToken!, "business", default);
        foreach (var id in new[] { a.Id, b.Id })
        {
            var s = (await db.Suppliers.FindAsync(id))!;
            s.PartnerConfirmAnswer.Should().Be("business");
            s.ServesConsumers.Should().BeFalse();
            s.IsActive.Should().BeTrue();
        }
        q.Emails.Should().ContainSingle(e => e.Subject.Contains("BUSINESS"));
    }

    [Fact]
    public async Task No_opts_out_and_a_corrected_yes_undoes_only_that_opt_out()
    {
        var db = TestDbContext.Create();
        var q = new Queue();
        var a = Provider("Kolija OÜ", "a@kolija.ee");
        a.PartnerConfirmSentAt = DateTime.UtcNow; a.PartnerConfirmToken = PartnerConfirm.NewToken();
        db.Suppliers.Add(a);
        await db.SaveChangesAsync();

        await Public(db, q).Confirm(a.PartnerConfirmToken!, "no", default);
        var s = (await db.Suppliers.FindAsync(a.Id))!;
        s.MarketingOptOutAt.Should().NotBeNull();
        s.IsActive.Should().BeFalse();

        await Public(db, q).Confirm(a.PartnerConfirmToken!, "yes", default);
        s = (await db.Suppliers.FindAsync(a.Id))!;
        s.MarketingOptOutAt.Should().BeNull();
        s.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_token_is_a_404_page()
    {
        var db = TestDbContext.Create();
        var res = await Public(db, new Queue()).Show("nope", "yes", default);
        res.Should().BeOfType<ContentResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Job_is_inert_until_switched_on()
    {
        var db = TestDbContext.Create();
        var q = new Queue();
        var a = Provider("Old OÜ", "old@old.ee");
        a.PartnerConfirmSentAt = DateTime.UtcNow.AddDays(-40); a.PartnerConfirmToken = PartnerConfirm.NewToken();
        db.Suppliers.Add(a);
        await db.SaveChangesAsync();

        var r = await Job(db, q).RunAsync(DateTime.UtcNow);
        r.Enabled.Should().BeFalse();
        q.Emails.Should().BeEmpty();
        (await db.Suppliers.FindAsync(a.Id))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Job_reminds_after_14_days_expires_7_days_later_and_a_late_yes_restores()
    {
        var db = TestDbContext.Create();
        var q = new Queue();
        await TurnOn(db);
        var start = new DateTime(2026, 11, 14, 7, 0, 0, DateTimeKind.Utc);
        var a = Provider("Silent OÜ", "silent@silent.ee");
        a.PartnerConfirmSentAt = start; a.PartnerConfirmToken = PartnerConfirm.NewToken();
        var opted = Provider("Opted OÜ", "x@opted.ee");
        opted.PartnerConfirmSentAt = start; opted.MarketingOptOutAt = start.AddDays(1);
        db.Suppliers.AddRange(a, opted);
        await db.SaveChangesAsync();

        (await Job(db, q).RunAsync(start.AddDays(13))).Reminded.Should().Be(0);

        var r = await Job(db, q).RunAsync(start.AddDays(14));
        r.Reminded.Should().Be(1);
        q.Emails.Should().Contain(e => e.To == "silent@silent.ee" && e.Subject.StartsWith("Meeldetuletus"));
        q.Emails.Should().NotContain(e => e.To == "x@opted.ee");

        q.Emails.Clear();
        (await Job(db, q).RunAsync(start.AddDays(15))).Reminded.Should().Be(0);   // only once
        (await Job(db, q).RunAsync(start.AddDays(20))).Expired.Should().Be(0);
        (await Job(db, q).RunAsync(start.AddDays(21))).Expired.Should().Be(1);

        var s = (await db.Suppliers.FindAsync(a.Id))!;
        s.IsActive.Should().BeFalse();
        s.MarketingOptOutAt.Should().BeNull();   // silence is not an opt-out
        (await db.Suppliers.FindAsync(opted.Id))!.PartnerConfirmExpiredAt.Should().BeNull();

        await Public(db, q).Confirm(a.PartnerConfirmToken!, "yes", default);
        s = (await db.Suppliers.FindAsync(a.Id))!;
        s.IsActive.Should().BeTrue();
        s.PartnerConfirmExpiredAt.Should().BeNull();
    }

    [Fact]
    public async Task Quoting_on_a_request_counts_as_yes()
    {
        var db = TestDbContext.Create();
        var q = new Queue();
        await TurnOn(db);
        var start = DateTime.UtcNow.AddDays(-2);
        var a = Provider("Busy OÜ", "busy@busy.ee");
        a.PartnerConfirmSentAt = start;
        db.Suppliers.Add(a);
        db.ProviderOutreaches.Add(new ProviderOutreach
        {
            Id = Guid.NewGuid(), SupplierId = a.Id, DemandLeadId = Guid.NewGuid(), SentTo = a.ContactEmail,
            SentAt = start.AddDays(-5), QuotedAt = start.AddDays(1), QuotedAmount = 80,
        });
        await db.SaveChangesAsync();

        (await Job(db, q).RunAsync(DateTime.UtcNow)).AutoYes.Should().Be(1);
        (await db.Suppliers.FindAsync(a.Id))!.PartnerConfirmAnswer.Should().Be("yes");
    }

    [Theory]
    [InlineData("EE", "Tasuta kliendipäringud")]
    [InlineData("LV", "Bezmaksas klientu pieprasījumi")]
    [InlineData("LT", "Nemokamos klientų užklausos")]
    [InlineData("FI", "Free customer requests")]
    public void Copy_renders_in_the_suppliers_language_with_no_placeholders_left(string country, string subject)
    {
        var s = Provider("Näide OÜ", "a@b.ee", country);
        foreach (var n in new[] { 0, 1, 4, 21 })
        foreach (var first in new DateTime?[] { null, DateTime.UtcNow })
        {
            var m = PartnerConfirm.Compose(s, "tok", "https://api.ruumly.eu", n, first);
            m.Subject.Should().Contain(subject);
            (m.TextBody + m.HtmlBody + m.Subject).Should().NotContain("{0}").And.NotContain("{1}").And.NotContain("{2}");
            m.HtmlBody.Should().Contain("https://api.ruumly.eu/api/partner-confirm/tok?a=yes");
        }
    }

    [Theory]
    [InlineData("SIA \"Kravu Mednieki\"", "Kravu Mednieki")]
    [InlineData("VIADA Baltija AS — DUS Daugavpils 1", "VIADA Baltija")]
    [InlineData("Hekkari Veod OÜ", "Hekkari Veod")]
    [InlineData("Ants Viljandi OÜ (Ansvil)", "Ants Viljandi")]
    public void Brand_name_drops_legal_form_branch_and_parenthetical(string name, string brand) =>
        PartnerConfirm.BrandName(name).Should().Be(brand);
}
