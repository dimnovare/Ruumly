using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Coverage for the free canvas-signature path: <see cref="ContractService.SignAsync"/>
/// and its private ValidateCanvasSignature guard.
///
/// The validator decodes the data URL, checks the PNG magic, and reads the IHDR
/// width/height (big-endian 32-bit ints at byte offsets 16/20) to reject a degenerate
/// 1x1 image while still accepting a full-size blank canvas (the type-your-name a11y
/// path). On the happy path the rendered contract snapshot must EMBED the signature
/// image and carry the self-declared / not-identity-verified label, and the persisted
/// row must be SigningMethod="canvas" with no identity-verified fields set.
/// </summary>
public class ContractCanvasSignatureTests
{
    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    // ── PNG data-URL builders ───────────────────────────────────────────────────

    private const string Prefix = "data:image/png;base64,";

    /// <summary>
    /// Builds a data:image/png;base64 URL whose bytes start with the real PNG magic and
    /// carry an IHDR chunk with the given dimensions. The validator only inspects the
    /// magic (8 bytes), total length (>=24), and the width/height at offsets 16/20 — it
    /// does NOT validate the CRC — so this is sufficient to exercise every branch.
    /// </summary>
    private static string PngDataUrl(int width, int height)
    {
        var bytes = new byte[24];
        // 8-byte PNG magic.
        byte[] magic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(magic, bytes, 8);
        // IHDR length (13) + "IHDR" — not inspected, but realistic.
        bytes[8] = 0x00; bytes[9] = 0x00; bytes[10] = 0x00; bytes[11] = 0x0D;
        bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
        // Width @16, height @20 — big-endian.
        bytes[16] = (byte)(width  >> 24); bytes[17] = (byte)(width  >> 16);
        bytes[18] = (byte)(width  >> 8);  bytes[19] = (byte)width;
        bytes[20] = (byte)(height >> 24); bytes[21] = (byte)(height >> 16);
        bytes[22] = (byte)(height >> 8);  bytes[23] = (byte)height;
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>A full-size valid signature PNG (200x80) — passes every guard.</summary>
    private static string ValidPngDataUrl() => PngDataUrl(200, 80);

    // ── (a) tiny / degenerate PNG is rejected ────────────────────────────────────

    [Fact]
    public async Task SignAsync_Rejects_Tiny_1x1_Png()
    {
        await using var db = CreateDb();
        var booking = await SeedBookingAsync(db);
        var svc = MakeService(db);
        var req = MakeReq(booking.Id, PngDataUrl(1, 1));

        var act = async () => await svc.SignAsync(req, "tenant@test.ee", "1.2.3.4");

        await act.Should().ThrowAsync<ArgumentException>(
            "a 1x1 / degenerate PNG is below the minimum signature dimensions");

        // Nothing persisted on a rejected sign.
        (await db.SignedContracts.CountAsync()).Should().Be(0);
    }

    // ── (b) non-PNG / malformed base64 is rejected ───────────────────────────────

    [Fact]
    public async Task SignAsync_Rejects_NonPng_DataUrl()
    {
        await using var db = CreateDb();
        var booking = await SeedBookingAsync(db);
        var svc = MakeService(db);
        // Wrong MIME prefix entirely (a JPEG data URL).
        var req = MakeReq(booking.Id, "data:image/jpeg;base64," + Convert.ToBase64String(new byte[40]));

        var act = async () => await svc.SignAsync(req, "tenant@test.ee", "1.2.3.4");

        await act.Should().ThrowAsync<ArgumentException>();
        (await db.SignedContracts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SignAsync_Rejects_Malformed_Base64()
    {
        await using var db = CreateDb();
        var booking = await SeedBookingAsync(db);
        var svc = MakeService(db);
        // Correct PNG prefix but the payload is not valid base64.
        var req = MakeReq(booking.Id, Prefix + "@@@not-valid-base64@@@");

        var act = async () => await svc.SignAsync(req, "tenant@test.ee", "1.2.3.4");

        await act.Should().ThrowAsync<ArgumentException>();
        (await db.SignedContracts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SignAsync_Rejects_Png_Prefix_With_NonPng_Bytes()
    {
        await using var db = CreateDb();
        var booking = await SeedBookingAsync(db);
        var svc = MakeService(db);
        // Valid base64, correct prefix, but the bytes lack the PNG magic header.
        var req = MakeReq(booking.Id, Prefix + Convert.ToBase64String(new byte[40]));

        var act = async () => await svc.SignAsync(req, "tenant@test.ee", "1.2.3.4");

        await act.Should().ThrowAsync<ArgumentException>();
        (await db.SignedContracts.CountAsync()).Should().Be(0);
    }

    // ── (c) valid full-size PNG accepted + signature embedded in rendered HTML ────

    [Fact]
    public async Task SignAsync_Accepts_FullSize_Png_And_Embeds_Signature_In_Html()
    {
        await using var db = CreateDb();
        var booking = await SeedBookingAsync(db);
        var svc = MakeService(db);
        var dataUrl = ValidPngDataUrl();
        var req = MakeReq(booking.Id, dataUrl);

        var signed = await svc.SignAsync(req, "tenant@test.ee", "1.2.3.4");

        signed.Status.Should().Be("completed");
        // The rendered snapshot embeds the actual drawn signature as an <img> and carries
        // the self-declared / not-identity-verified label.
        signed.RenderedHtml.Should().Contain("<img src=\"data:image/png");
        signed.RenderedHtml.Should().Contain(dataUrl, "the exact signature PNG is embedded");
        signed.RenderedHtml.Should().Contain("self-declared, not identity-verified");
        // Tamper-evidence hash computed over the rendered HTML.
        signed.RenderedHtmlHash.Should().NotBeNullOrEmpty();
    }

    // ── (d) SigningMethod=="canvas" and not identity-verified surface in the flow ─

    [Fact]
    public async Task SignAsync_CanvasPath_Persists_Unverified_Method()
    {
        await using var db = CreateDb();
        var booking = await SeedBookingAsync(db);
        var svc = MakeService(db);
        var req = MakeReq(booking.Id, ValidPngDataUrl());

        var signed = await svc.SignAsync(req, "tenant@test.ee", "1.2.3.4");

        // The free canvas method is self-declared: method "canvas", and NO identity-verified
        // name/id-code captured (the model surfaces "not verified" via these being null).
        signed.SigningMethod.Should().Be("canvas");
        signed.VerifiedName.Should().BeNull("a canvas signature is not identity-verified");
        signed.VerifiedIdCode.Should().BeNull("a canvas signature is not identity-verified");
        signed.SignedFromIp.Should().Be("1.2.3.4");

        // Persisted row matches.
        var persisted = await db.SignedContracts.FindAsync(signed.Id);
        persisted!.SigningMethod.Should().Be("canvas");
        persisted.Status.Should().Be("completed");
    }

    [Fact]
    public async Task SignAsync_Is_Idempotent_For_Same_Booking()
    {
        await using var db = CreateDb();
        var booking = await SeedBookingAsync(db);
        var svc = MakeService(db);

        var first  = await svc.SignAsync(MakeReq(booking.Id, ValidPngDataUrl()), "tenant@test.ee", "1.2.3.4");
        var second = await svc.SignAsync(MakeReq(booking.Id, ValidPngDataUrl()), "tenant@test.ee", "5.6.7.8");

        second.Id.Should().Be(first.Id, "a second sign returns the existing contract");
        (await db.SignedContracts.CountAsync()).Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ContractService MakeService(RuumlyDbContext db) =>
        new(db,
            new NoOpEmail(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://test.ruumly.eu" })
                .Build(),
            NullLogger<ContractService>.Instance);

    private static SignContractRequest MakeReq(Guid bookingId, string dataUrl) =>
        new(
            BookingId: bookingId,
            // Route through the in-code platform-default template so RenderAsync needs no
            // ContractTemplate row (the sentinel id is rendered from PlatformDefaultContract).
            ContractTemplateId: PlatformDefaultContract.TemplateId,
            TenantName: "Mari Maasikas",
            TenantIdCode: "38001085718",
            SignatureDataUrl: dataUrl);

    private static async Task<Booking> SeedBookingAsync(RuumlyDbContext db)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Test Supplier",
            RegistryCode = "CV001",
            ContactName = "Partner",
            ContactEmail = "supplier@test.ee",
            ContactPhone = "+37255555555",
        };
        var customer = new User
        {
            Id = Guid.NewGuid(),
            Name = "Mari Maasikas",
            Email = "tenant@test.ee",
            Role = UserRole.Customer,
            Language = "et",
        };
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Type = ListingType.Warehouse,
            Title = "Storage unit A",
            Address = "Test St 1",
            City = "Tallinn",
            PriceFrom = 100m,
            PriceUnit = "month",
        };
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = customer.Id,
            ListingId = listing.Id,
            SupplierId = supplier.Id,
            StartDate = DateTime.UtcNow.Date.AddDays(1),
            Duration = "1 month",
            Total = 100m,
            BasePrice = 100m,
            Status = BookingStatus.Pending,
            Supplier = supplier,
            Listing = listing,
            User = customer,
        };

        db.Suppliers.Add(supplier);
        db.Users.Add(customer);
        db.Listings.Add(listing);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private sealed class NoOpEmail : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }
}
