using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class OrderLeadStatusTests
{
    private static ClaimsPrincipal MakeUser(Guid userId, UserRole role, string email = "test@test.ee")
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(ClaimTypes.Email, email),
        ], "test"));

    private sealed class StubOrderService : IOrderService
    {
        public Task<DTOs.PaginatedResult<DTOs.Responses.OrderDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50, Guid? supplierId = null, string? status = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Dictionary<string, int>> GetStatusCountsAsync(Guid userId, UserRole role, Guid? supplierId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<DTOs.Responses.OrderDto?> GetByIdAsync(Guid id, Guid callerId, Models.Enums.UserRole callerRole, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DTOs.Responses.OrderDto?> GetByBookingIdAsync(Guid bookingId, Guid callerId, UserRole callerRole, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DTOs.Responses.OrderDto> ApproveAsync(Guid id, Guid approvedByUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DTOs.Responses.OrderDto> RejectAsync(Guid id, string reason, Guid rejectedByUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DTOs.Responses.OrderDto> ConfirmAsync(Guid id, Guid confirmedByUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DTOs.Responses.OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken ct = default) => throw new NotImplementedException();

        public DTOs.Responses.OrderDto MapToDto(Order o) => new(
            Id: o.Id, BookingId: o.BookingId, ListingId: o.ListingId,
            SupplierId: o.SupplierId, SupplierName: o.Supplier?.Name ?? "",
            ListingTitle: o.ListingTitle, ListingType: o.ListingType.ToString().ToLower(),
            IntegrationType: o.IntegrationType.ToString().ToLower(),
            CustomerName: o.CustomerName, CustomerEmail: o.CustomerEmail,
            CustomerPhone: o.CustomerPhone, City: o.City,
            StartDate: o.StartDate.ToString("yyyy-MM-dd"),
            EndDate: o.EndDate?.ToString("yyyy-MM-dd"),
            Duration: o.Duration, Extras: [],
            BasePrice: o.BasePrice, PlatformPrice: o.PlatformPrice,
            SupplierPrice: o.SupplierPrice, ExtrasTotal: o.ExtrasTotal,
            Total: o.Total, Margin: o.Margin,
            Status: o.Status.ToString().ToLower(),
            ApprovedBy: o.ApprovedBy,
            ApprovedAt: o.ApprovedAt?.ToString("yyyy-MM-dd HH:mm"),
            PostingChannel: o.PostingChannel?.ToString().ToLower(),
            SentAt: o.SentAt?.ToString("yyyy-MM-dd HH:mm"),
            ConfirmedAt: o.ConfirmedAt?.ToString("yyyy-MM-dd HH:mm"),
            Notes: o.Notes, CreatedAt: o.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            LeadStatus: o.LeadStatus.ToString().ToLower(),
            LastContactAt: o.LastContactAt?.ToString("yyyy-MM-dd HH:mm"),
            ProviderNotes: o.ProviderNotes,
            Timeline: [], FulfillmentEvents: []);
    }

    [Fact]
    public async Task Provider_Can_Patch_Own_Order_Lead_And_Other_Provider_Gets_403()
    {
        var db = TestDbContext.Create();

        var supplier1Id = Guid.NewGuid();
        var supplier2Id = Guid.NewGuid();
        var provider1Id = Guid.NewGuid();
        var provider2Id = Guid.NewGuid();
        var bookingId   = Guid.NewGuid();
        var customerId  = Guid.NewGuid();
        var listingId   = Guid.NewGuid();
        var orderId     = Guid.NewGuid();

        db.Suppliers.AddRange(
            new Supplier { Id = supplier1Id, Name = "Sup1", ContactName = "C", ContactEmail = "c@t.ee", ContactPhone = "1" },
            new Supplier { Id = supplier2Id, Name = "Sup2", ContactName = "D", ContactEmail = "d@t.ee", ContactPhone = "2" });
        db.Users.AddRange(
            new User { Id = provider1Id, Name = "P1", Email = "p1@t.ee", PasswordHash = "x", Role = UserRole.Provider, SupplierId = supplier1Id },
            new User { Id = provider2Id, Name = "P2", Email = "p2@t.ee", PasswordHash = "x", Role = UserRole.Provider, SupplierId = supplier2Id },
            new User { Id = customerId,  Name = "C1", Email = "c1@t.ee", PasswordHash = "x", Role = UserRole.Customer });
        db.Bookings.Add(new Booking
        {
            Id = bookingId, ListingId = listingId, SupplierId = supplier1Id,
            UserId = customerId, ContactName = "C1", ContactEmail = "c1@t.ee",
            ContactPhone = "1", StartDate = DateTime.UtcNow, Status = BookingStatus.Confirmed,
        });
        db.Orders.Add(new Order
        {
            Id = orderId, BookingId = bookingId, SupplierId = supplier1Id,
            ListingId = listingId, ListingTitle = "Test", ListingType = ListingType.Warehouse,
            City = "Tallinn", StartDate = DateTime.UtcNow, Duration = "1 kuu",
            CustomerName = "C1", CustomerEmail = "c1@t.ee", CustomerPhone = "1",
            IntegrationType = IntegrationType.Email, Status = OrderStatus.Created,
        });
        await db.SaveChangesAsync();

        var orderService = new StubOrderService();

        // ── Act 1: Provider1 patches lead status to Contacted ────────────
        var controller1 = new OrdersController(orderService, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = MakeUser(provider1Id, UserRole.Provider, "p1@t.ee") }
            }
        };

        var patchResult = await controller1.UpdateLead(orderId, new UpdateOrderLeadRequest(
            LeadStatus: "Contacted",
            LastContactAt: DateTime.UtcNow,
            ProviderNotes: "Called and left voicemail"));

        patchResult.Should().BeOfType<OkObjectResult>();
        var dto = ((OkObjectResult)patchResult).Value as DTOs.Responses.OrderDto;
        dto.Should().NotBeNull();
        dto!.LeadStatus.Should().Be("contacted");
        dto.LastContactAt.Should().NotBeNull();
        dto.ProviderNotes.Should().Be("Called and left voicemail");

        // Verify persisted
        var persisted = await db.Orders.FindAsync(orderId);
        persisted!.LeadStatus.Should().Be(LeadStatus.Contacted);
        persisted.LastContactAt.Should().NotBeNull();

        // ── Act 2: Provider2 (different supplier) tries the same → 403 ──
        var controller2 = new OrdersController(orderService, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = MakeUser(provider2Id, UserRole.Provider, "p2@t.ee") }
            }
        };

        var forbiddenResult = await controller2.UpdateLead(orderId, new UpdateOrderLeadRequest(
            LeadStatus: "Won"));

        forbiddenResult.Should().BeOfType<ObjectResult>();
        ((ObjectResult)forbiddenResult).StatusCode.Should().Be(403);
    }
}
