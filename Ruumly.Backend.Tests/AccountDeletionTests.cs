using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Tests the GDPR account deletion and data-export business logic.
/// The logic lives in AuthController; these tests exercise it directly
/// against the InMemory database to keep tests fast and self-contained.
/// </summary>
public class AccountDeletionTests
{
    // ─── Test infrastructure ──────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    // ─── Helpers that mirror the controller's deletion/export logic ───────────

    /// <summary>
    /// Replicates AuthController.DeleteAccount — runs the deletion logic against
    /// the provided db context and returns true on success, false when the
    /// user has active bookings.
    /// </summary>
    private static async Task<bool> DeleteAccountAsync(RuumlyDbContext db, Guid userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return false;

        var hasActiveBookings = await db.Bookings.AnyAsync(b =>
            b.UserId == userId &&
            b.Status != BookingStatus.Cancelled &&
            b.Status != BookingStatus.Completed);

        if (hasActiveBookings) return false;

        user.Name         = "Deleted User";
        user.Email        = $"deleted-{user.Id}@ruumly.eu";
        user.Phone        = null;
        user.Company      = null;
        user.Avatar       = null;
        user.GoogleId     = null;
        user.PasswordHash = "";
        user.Status       = UserStatus.Deleted;
        user.DeletedAt    = DateTime.UtcNow;

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();
        foreach (var token in tokens) token.IsRevoked = true;

        db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = "account.deleted",
            Actor     = userId.ToString(),
            Target    = userId.ToString(),
            Detail    = "User requested account deletion — PII anonymized",
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return true;
    }

    // ─── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<User> SeedUserAsync(RuumlyDbContext db)
    {
        var user = new User
        {
            Id            = Guid.NewGuid(),
            Email         = "delete-me@test.ee",
            Name          = "Delete Me",
            Phone         = "+372 5000 0001",
            Company       = "Test Corp",
            Avatar        = "https://cdn.test/avatar.jpg",
            PasswordHash  = "hash",
            Role          = UserRole.Customer,
            Status        = UserStatus.Active,
            EmailVerified = true,
            RegisteredAt  = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static Supplier MakeSupplier() => new()
    {
        Id           = Guid.NewGuid(),
        Name         = "Supplier",
        RegistryCode = "SUP001",
        ContactName  = "Contact",
        ContactEmail = "sup@test.ee",
        ContactPhone = "+372 5000 0000",
    };

    private static Listing MakeListing(Guid supplierId) => new()
    {
        Id           = Guid.NewGuid(),
        SupplierId   = supplierId,
        Type         = ListingType.Warehouse,
        Title        = "Test Warehouse",
        Address      = "Test St 1",
        City         = "Tallinn",
        PriceFrom    = 100m,
        PriceUnit    = "kuu",
        IsActive     = true,
        AvailableNow = true,
        Description  = "Test",
    };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAccount_NoBookings_Succeeds_And_AnonymisesPii()
    {
        var db   = CreateDb();
        var user = await SeedUserAsync(db);

        var result = await DeleteAccountAsync(db, user.Id);

        result.Should().BeTrue();

        var deleted = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        deleted.Status.Should().Be(UserStatus.Deleted);
        deleted.DeletedAt.Should().NotBeNull();
        deleted.Name.Should().Be("Deleted User");
        deleted.Email.Should().Be($"deleted-{user.Id}@ruumly.eu");
        deleted.Phone.Should().BeNull();
        deleted.Company.Should().BeNull();
        deleted.Avatar.Should().BeNull();
        deleted.PasswordHash.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAccount_WithActivePendingBooking_Fails()
    {
        var db       = CreateDb();
        var user     = await SeedUserAsync(db);
        var supplier = MakeSupplier();
        var listing  = MakeListing(supplier.Id);
        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Bookings.Add(new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            ListingId  = listing.Id,
            SupplierId = supplier.Id,
            StartDate  = DateTime.UtcNow.AddDays(10),
            Duration   = "1 kuu",
            Status     = BookingStatus.Pending,   // still active — blocks deletion
        });
        await db.SaveChangesAsync();

        var result = await DeleteAccountAsync(db, user.Id);

        result.Should().BeFalse("active bookings must be cancelled before account deletion");
        var unchanged = await db.Users.FindAsync(user.Id);
        unchanged!.Status.Should().Be(UserStatus.Active, "user should not have been deleted");
    }

    [Fact]
    public async Task DeleteAccount_WithCancelledBookingOnly_Succeeds()
    {
        // Completed/Cancelled bookings should not block deletion
        var db       = CreateDb();
        var user     = await SeedUserAsync(db);
        var supplier = MakeSupplier();
        var listing  = MakeListing(supplier.Id);
        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Bookings.Add(new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            ListingId  = listing.Id,
            SupplierId = supplier.Id,
            StartDate  = DateTime.UtcNow.AddDays(-30),
            Duration   = "1 kuu",
            Status     = BookingStatus.Cancelled,
            IsDeleted  = true,
        });
        await db.SaveChangesAsync();

        var result = await DeleteAccountAsync(db, user.Id);

        result.Should().BeTrue("only cancelled bookings should not block deletion");
    }

    [Fact]
    public async Task DeleteAccount_RevokesAllRefreshTokens()
    {
        var db   = CreateDb();
        var user = await SeedUserAsync(db);

        // Seed two active refresh tokens
        db.RefreshTokens.AddRange(
            new RefreshToken
            {
                Id         = Guid.NewGuid(),
                UserId     = user.Id,
                TokenHash  = "hash1",
                IsRevoked  = false,
                ExpiresAt  = DateTime.UtcNow.AddDays(7),
                CreatedAt  = DateTime.UtcNow,
            },
            new RefreshToken
            {
                Id         = Guid.NewGuid(),
                UserId     = user.Id,
                TokenHash  = "hash2",
                IsRevoked  = false,
                ExpiresAt  = DateTime.UtcNow.AddDays(7),
                CreatedAt  = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        await DeleteAccountAsync(db, user.Id);

        var tokens = await db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        tokens.Should().AllSatisfy(t => t.IsRevoked.Should().BeTrue());
    }

    [Fact]
    public async Task ExportData_ContainsAllUserData()
    {
        // Mirrors AuthController.ExportData — verifies the shape of data available for export.
        var db       = CreateDb();
        var user     = await SeedUserAsync(db);
        var supplier = MakeSupplier();
        var listing  = MakeListing(supplier.Id);
        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);

        var booking = new Booking
        {
            Id           = Guid.NewGuid(),
            UserId       = user.Id,
            ListingId    = listing.Id,
            SupplierId   = supplier.Id,
            StartDate    = DateTime.UtcNow.AddDays(5),
            Duration     = "1 kuu",
            Total        = 95m,
            Status       = BookingStatus.Confirmed,
            ContactEmail = user.Email,
            ContactName  = user.Name,
        };
        db.Bookings.Add(booking);

        db.Invoices.Add(new Invoice
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount    = 95m,
            Status    = InvoiceStatus.Pending,
        });

        db.Messages.Add(new Message
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            UserId    = user.Id,
            From      = MessageSender.Customer,
            Text      = "Hello!",
        });

        db.Reviews.Add(new Review
        {
            Id        = Guid.NewGuid(),
            UserId    = user.Id,
            ListingId = listing.Id,
            BookingId = booking.Id,
            SupplierId= supplier.Id,
            Rating    = 5,
            Comment   = "Great!",
        });

        await db.SaveChangesAsync();

        // ── Run the same queries as AuthController.ExportData ────────────────
        var exportUser = await db.Users.FindAsync(user.Id);
        var bookings   = await db.Bookings
            .Include(b => b.Timeline)
            .Where(b => b.UserId == user.Id)
            .ToListAsync();
        var bookingIds = bookings.Select(b => b.Id).ToList();
        var invoices   = await db.Invoices
            .Where(i => bookingIds.Contains(i.BookingId))
            .ToListAsync();
        var messages   = await db.Messages
            .Where(m => m.UserId == user.Id)
            .ToListAsync();
        var reviews    = await db.Reviews
            .Where(r => r.UserId == user.Id)
            .ToListAsync();

        // ── Assertions ───────────────────────────────────────────────────────
        exportUser!.Email.Should().Be(user.Email);
        bookings.Should().HaveCount(1);
        invoices.Should().HaveCount(1);
        messages.Should().HaveCount(1);
        reviews.Should().HaveCount(1);
        reviews.Single().Rating.Should().Be(5);
    }
}
