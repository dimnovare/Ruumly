using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests;

public class ContractDokobitCompletionTests
{
    [Fact]
    public async Task Completed_gateway_status_without_signed_pdf_stays_retryable()
    {
        await using var db = CreateDb();
        var (booking, contract) = SeedContract(db, "pending");
        var dokobit = new FakeDokobitService
        {
            StatusResult = CompletedResult(),
            SignedDocument = null,
        };
        var storage = new FakeStorageService();
        var controller = CreateController(db, dokobit, storage);

        await controller.DokobitStatus(contract.DokobitSigningToken!, CancellationToken.None);

        contract.Status.Should().Be("pending");
        contract.SignedDocumentUrl.Should().BeNull();
        contract.VerifiedIdCode.Should().Be("60001019906");
        dokobit.DownloadCalls.Should().Be(1);
    }

    [Fact]
    public async Task Incomplete_cached_completion_is_reprocessed_and_repaired()
    {
        await using var db = CreateDb();
        var (_, contract) = SeedContract(db, "completed");
        var dokobit = new FakeDokobitService
        {
            StatusResult = CompletedResult(),
            SignedDocument = "%PDF-signed"u8.ToArray(),
        };
        var storage = new FakeStorageService();
        var controller = CreateController(db, dokobit, storage);

        var action = await controller.DokobitStatus(
            contract.DokobitSigningToken!, CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>();
        contract.Status.Should().Be("completed");
        contract.VerifiedIdCode.Should().Be("60001019906");
        contract.VerifiedName.Should().Be("MARY ÄNN TESTNUMBER");
        contract.SignedDocumentUrl.Should().Be("https://files.test/signed.pdf");
        dokobit.StatusCalls.Should().Be(1);
        dokobit.DownloadCalls.Should().Be(1);
        storage.UploadCalls.Should().Be(1);
    }

    [Fact]
    public async Task Pending_contract_with_all_completed_artifacts_becomes_completed()
    {
        await using var db = CreateDb();
        var (_, contract) = SeedContract(db, "pending");
        var dokobit = new FakeDokobitService
        {
            StatusResult = CompletedResult(),
            SignedDocument = "%PDF-signed"u8.ToArray(),
        };
        var controller = CreateController(db, dokobit, new FakeStorageService());

        await controller.DokobitStatus(contract.DokobitSigningToken!, CancellationToken.None);

        contract.Status.Should().Be("completed");
        contract.VerifiedIdCode.Should().Be("60001019906");
        contract.VerifiedName.Should().Be("MARY ÄNN TESTNUMBER");
        contract.SignedDocumentUrl.Should().Be("https://files.test/signed.pdf");
    }

    private static DokobitStatusResult CompletedResult() =>
        new(DokobitSigningStatus.Completed, "60001019906", "MARY ÄNN TESTNUMBER", "mobile");

    private static RuumlyDbContext CreateDb()
        => TestDbContext.Create();

    private static (Booking Booking, SignedContract Contract) SeedContract(
        RuumlyDbContext db, string status)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            Duration = "1 month",
            Status = BookingStatus.Pending,
        };
        var contract = new SignedContract
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            ContractTemplateId = Guid.NewGuid(),
            TenantName = "Customer",
            TenantEmail = "customer@example.com",
            DokobitSigningToken = "session-token",
            SigningMethod = "dokobit",
            Status = status,
        };
        db.Bookings.Add(booking);
        db.SignedContracts.Add(contract);
        db.SaveChanges();
        return (booking, contract);
    }

    private static ContractController CreateController(
        RuumlyDbContext db, IDokobitService dokobit, IStorageService storage)
    {
        var controller = new ContractController(
            db,
            new UnusedContractService(),
            dokobit,
            storage,
            new UnusedDocumentService(),
            new UnusedGotenbergClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<ContractController>.Instance);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };
        return controller;
    }

    private sealed class FakeDokobitService : IDokobitService
    {
        public bool IsEnabled => true;
        public DokobitStatusResult StatusResult { get; init; } = CompletedResult();
        public byte[]? SignedDocument { get; init; }
        public int StatusCalls { get; private set; }
        public int DownloadCalls { get; private set; }

        public Task<DokobitStatusResult> GetStatusAsync(
            string signingToken, CancellationToken ct = default)
        {
            StatusCalls++;
            return Task.FromResult(StatusResult);
        }

        public Task<byte[]?> DownloadSignedDocumentAsync(
            string signingToken, CancellationToken ct = default)
        {
            DownloadCalls++;
            return Task.FromResult(SignedDocument);
        }

        public Task<DokobitUploadResult> UploadDocumentAsync(
            string fileName, byte[] pdfBytes, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DokobitSigningResult> CreateSigningRequestAsync(
            string fileToken, string documentName, DokobitSigner signer,
            string postbackUrl, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStorageService : IStorageService
    {
        public int UploadCalls { get; private set; }

        public Task<string> UploadAsync(Stream stream, string fileName, string contentType)
        {
            UploadCalls++;
            return Task.FromResult("https://files.test/signed.pdf");
        }

        public Task<StoredObject> UploadWithKeyAsync(
            Stream stream, string fileName, string contentType) =>
            throw new NotSupportedException();

        public Task<byte[]?> DownloadAsync(string key) => throw new NotSupportedException();
        public Task DeleteAsync(string publicUrl) => throw new NotSupportedException();
    }

    private sealed class UnusedContractService : IContractService
    {
        public Task<string> RenderAsync(
            Guid templateId, Guid bookingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SignedContract> SignAsync(
            SignContractRequest req, string tenantEmail, string? ip,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedDocumentService : IContractDocumentService
    {
        public IReadOnlyList<string> DiscoverTokens(byte[] docxBytes) => throw new NotSupportedException();
        public byte[] Fill(byte[] docxBytes, IReadOnlyDictionary<string, string> values) => throw new NotSupportedException();
        public byte[] BuildDocx(IEnumerable<string> paragraphs) => throw new NotSupportedException();
        public byte[] AppendClause(byte[] docxBytes, string clauseText) => throw new NotSupportedException();
    }

    private sealed class UnusedGotenbergClient : IGotenbergClient
    {
        public bool IsEnabled => false;
        public Task<byte[]> ConvertDocxToPdfAsync(
            byte[] docxBytes, string fileName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
