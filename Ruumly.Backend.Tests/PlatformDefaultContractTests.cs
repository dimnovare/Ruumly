using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Covers the platform-default contract fallback (H1) and the always-bound conditional-payment
/// clause (L4): the in-code agreement renders to clean HTML, the in-code docx builder round-trips
/// through Fill, and ContractService.RenderAsync resolves both the sentinel default and real
/// templates with the clause attached.
/// </summary>
public class PlatformDefaultContractTests
{
    private static string ReadDocxText(byte[] docx)
    {
        using var ms  = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")!;
        using var s = entry.Open();
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static ContractService MakeService(RuumlyDbContext db)
    {
        var config = new ConfigurationBuilder().Build();
        return new ContractService(db, new NoopEmailSender(), config, NullLogger<ContractService>.Instance);
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }

    // ─── PlatformDefaultContract.RenderHtml ──────────────────────────────────────

    [Fact]
    public void RenderHtml_fills_all_values_and_leaves_no_unresolved_placeholders()
    {
        var values = ContractTokenVocabulary.SampleValues();

        var html = System.Web.HttpUtility.HtmlDecode(PlatformDefaultContract.RenderHtml(values));

        html.Should().Contain("Näidis Ladu OÜ");      // supplier
        html.Should().Contain("Mari Maasikas");        // tenant
        html.Should().Contain("Ladu 12B");             // unit
        html.Should().Contain("Pärnu mnt 158");        // address
        // The conditional-on-payment clause text is present.
        html.Should().Contain("conditional upon payment");
        html.Should().Contain("automatically void");
        // No unresolved {{ }} placeholders leak into the rendered document.
        html.Should().NotContain("{{");
    }

    // ─── OpenXmlContractDocumentService.BuildDocx round-trip ─────────────────────

    [Fact]
    public void BuildDocx_emits_tokens_that_DiscoverTokens_finds_and_Fill_replaces()
    {
        var svc = new OpenXmlContractDocumentService();
        var paragraphs = new[]
        {
            "Provider: {{supplier_name}} (reg {{supplier_reg_code}})",
            "Tenant: {{tenant_name}}",
            "Clause: {{payment_condition_clause}}",
        };

        var docx = svc.BuildDocx(paragraphs);

        // DiscoverTokens finds the placeholders built into the docx.
        var tokens = svc.DiscoverTokens(docx);
        tokens.Should().Contain("supplier_name");
        tokens.Should().Contain("tenant_name");
        tokens.Should().Contain("payment_condition_clause");

        // Fill then replaces them with no leftover placeholders.
        var values = new Dictionary<string, string>
        {
            ["supplier_name"]            = "Näidis Ladu OÜ",
            ["supplier_reg_code"]        = "12345678",
            ["tenant_name"]              = "Mari Maasikas",
            ["payment_condition_clause"] = ContractTokenVocabulary.PaymentConditionClause(24),
        };
        var filled = svc.Fill(docx, values);
        var text   = ReadDocxText(filled);

        text.Should().Contain("Näidis Ladu OÜ");
        text.Should().Contain("Mari Maasikas");
        text.Should().NotContain("{{");
    }

    [Fact]
    public void BuildDocx_of_the_platform_default_paragraphs_round_trips()
    {
        var svc  = new OpenXmlContractDocumentService();
        var docx = svc.BuildDocx(PlatformDefaultContract.Paragraphs);

        var values = ContractTokenVocabulary.SampleValues();
        var filled = svc.Fill(docx, values);
        var text   = ReadDocxText(filled);

        text.Should().Contain("Storage Rental Agreement");
        text.Should().Contain("Näidis Ladu OÜ");
        text.Should().Contain("conditional upon payment");
        text.Should().NotContain("{{");
    }

    // ─── ContractService.RenderAsync ─────────────────────────────────────────────

    private static (RuumlyDbContext db, Guid bookingId, Guid supplierId) SeedBooking()
    {
        var db         = TestDbContext.Create();
        var supplierId = Guid.NewGuid();
        var listingId  = Guid.NewGuid();
        var userId     = Guid.NewGuid();
        var bookingId  = Guid.NewGuid();

        db.Suppliers.Add(new Supplier
        {
            Id = supplierId, Name = "BalticBox Center", RegistryCode = "16000001",
        });
        db.Users.Add(new User
        {
            Id = userId, Name = "Andres Tamm", Email = "andres@email.com",
        });
        db.Listings.Add(new Listing
        {
            Id = listingId, SupplierId = supplierId, Type = ListingType.Warehouse,
            Title = "Ladu 5A", Address = "Tehnika 12", City = "Tallinn",
            PriceUnit = "kuu", PriceFrom = 49m, SizeM2 = 8m, Description = "Test",
        });
        db.Bookings.Add(new Booking
        {
            Id = bookingId, UserId = userId, ListingId = listingId, SupplierId = supplierId,
            StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 12, 31),
            Duration = "6 kuud", Status = BookingStatus.Pending, BasePrice = 49m, Total = 294m,
        });
        db.SaveChanges();
        return (db, bookingId, supplierId);
    }

    [Fact]
    public async Task RenderAsync_with_sentinel_id_returns_default_html_with_clause()
    {
        var (db, bookingId, _) = SeedBooking();
        var svc = MakeService(db);

        var html = await svc.RenderAsync(PlatformDefaultContract.TemplateId, bookingId);

        html.Should().Contain("Storage Rental Agreement");
        html.Should().Contain("BalticBox Center");      // supplier from booking
        html.Should().Contain("Andres Tamm");           // tenant from booking
        html.Should().Contain("Ladu 5A");               // unit
        html.Should().Contain("conditional upon payment");
        html.Should().NotContain("{{");
    }

    [Fact]
    public async Task RenderAsync_for_real_html_template_appends_the_clause()
    {
        var (db, bookingId, supplierId) = SeedBooking();
        var templateId = Guid.NewGuid();
        db.ContractTemplates.Add(new ContractTemplate
        {
            Id = templateId, SupplierId = supplierId, Name = "Supplier template",
            HtmlTemplate = "<h1>Lease for {{unit_title}}</h1>", IsActive = true,
            TemplateType = ContractTemplateType.Html,
        });
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var html = await svc.RenderAsync(templateId, bookingId);

        html.Should().Contain("Lease for Ladu 5A");                 // real template body
        html.Should().Contain("Conditional upon payment:");        // L4 appended section
        html.Should().Contain("automatically void");
    }
}
