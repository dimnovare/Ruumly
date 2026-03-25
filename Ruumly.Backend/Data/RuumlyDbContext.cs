using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Data;

public class RuumlyDbContext(DbContextOptions<RuumlyDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<IntegrationSettings> IntegrationSettings => Set<IntegrationSettings>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingTimeline> BookingTimelines => Set<BookingTimeline>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderTimeline> OrderTimelines => Set<OrderTimeline>();
    public DbSet<FulfillmentEvent> FulfillmentEvents => Set<FulfillmentEvent>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OrderRoutingRule> OrderRoutingRules => Set<OrderRoutingRule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<SupplierLocation> SupplierLocations => Set<SupplierLocation>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        // ─── Enums as strings ───
        model.Entity<User>().Property(e => e.Role).HasConversion<string>();
        model.Entity<User>().Property(e => e.Status).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.IntegrationType).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.IntegrationHealth).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.Tier).HasConversion<string>();
        model.Entity<IntegrationSettings>().Property(e => e.ApprovalMode).HasConversion<string>();
        model.Entity<IntegrationSettings>().Property(e => e.PostingMode).HasConversion<string>();
        model.Entity<IntegrationSettings>().Property(e => e.FallbackPostingMode).HasConversion<string>();
        model.Entity<Listing>().Property(e => e.Type).HasConversion<string>();
        model.Entity<Listing>().Property(e => e.Badge).HasConversion<string>();
        model.Entity<Booking>().Property(e => e.Status).HasConversion<string>();
        model.Entity<BookingTimeline>().Property(e => e.Status).HasConversion<string>();
        model.Entity<Order>().Property(e => e.Status).HasConversion<string>();
        model.Entity<Order>().Property(e => e.ListingType).HasConversion<string>();
        model.Entity<Order>().Property(e => e.IntegrationType).HasConversion<string>();
        model.Entity<Order>().Property(e => e.ApprovalMode).HasConversion<string>();
        model.Entity<Order>().Property(e => e.PostingChannel).HasConversion<string>();
        model.Entity<OrderTimeline>().Property(e => e.Status).HasConversion<string>();
        model.Entity<FulfillmentEvent>().Property(e => e.Status).HasConversion<string>();
        model.Entity<FulfillmentEvent>().Property(e => e.ActorRole).HasConversion<string>();
        model.Entity<FulfillmentEvent>().Property(e => e.Channel).HasConversion<string>();
        model.Entity<Invoice>().Property(e => e.Status).HasConversion<string>();
        model.Entity<Message>().Property(e => e.From).HasConversion<string>();
        model.Entity<Notification>().Property(e => e.Type).HasConversion<string>();
        model.Entity<Notification>().Property(e => e.Channel).HasConversion<string>();
        model.Entity<OrderRoutingRule>().Property(e => e.ServiceType).HasConversion<string>();
        model.Entity<OrderRoutingRule>().Property(e => e.PostingChannel).HasConversion<string>();

        // ─── Unique indexes ───
        model.Entity<User>()
            .HasIndex(e => e.Email)
            .IsUnique();

        model.Entity<User>()
            .HasIndex(u => u.GoogleId)
            .IsUnique()
            .HasFilter("\"GoogleId\" IS NOT NULL");

        model.Entity<Supplier>()
            .HasIndex(e => e.RegistryCode)
            .IsUnique();

        model.Entity<RefreshToken>()
            .HasIndex(e => e.TokenHash)
            .IsUnique();

        model.Entity<IntegrationSettings>()
            .HasIndex(e => e.SupplierId)
            .IsUnique();

        // ─── Regular indexes ───
        model.Entity<Booking>()
            .HasIndex(e => e.UserId);

        model.Entity<Order>()
            .HasIndex(e => e.BookingId)
            .IsUnique();

        model.Entity<Invoice>()
            .HasIndex(e => e.BookingId)
            .IsUnique();

        model.Entity<Notification>()
            .HasIndex(e => e.UserId);

        // ─── Cascade deletes for child-only entities ───
        model.Entity<BookingTimeline>()
            .HasOne(e => e.Booking)
            .WithMany(e => e.Timeline)
            .HasForeignKey(e => e.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<FulfillmentEvent>()
            .HasOne(e => e.Order)
            .WithMany(e => e.FulfillmentEvents)
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<OrderTimeline>()
            .HasOne(e => e.Order)
            .WithMany(e => e.Timeline)
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // ─── Restrict delete: don't cascade from User → Booking ───
        model.Entity<Booking>()
            .HasOne(e => e.User)
            .WithMany(e => e.Bookings)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ─── Restrict delete: don't cascade from Booking → Message/Invoice/Order ───
        model.Entity<Message>()
            .HasOne(e => e.Booking)
            .WithMany(e => e.Messages)
            .HasForeignKey(e => e.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Invoice>()
            .HasOne(e => e.Booking)
            .WithOne(e => e.Invoice)
            .HasForeignKey<Invoice>(e => e.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Order>()
            .HasOne(e => e.Booking)
            .WithOne(e => e.Order)
            .HasForeignKey<Order>(e => e.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        // ─── Message → User (optional FK, no cascade) ───
        model.Entity<Message>()
            .HasOne(e => e.User)
            .WithMany(e => e.Messages)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // ─── Supplier → IntegrationSettings (1:1) ───
        model.Entity<IntegrationSettings>()
            .HasOne(e => e.Supplier)
            .WithOne(e => e.IntegrationSettings)
            .HasForeignKey<IntegrationSettings>(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        // ─── User → Supplier (optional FK for Provider users) ───
        model.Entity<User>()
            .HasOne(u => u.Supplier)
            .WithMany()
            .HasForeignKey(u => u.SupplierId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        model.Entity<User>()
            .HasIndex(u => u.SupplierId);

        // ─── Listing search indexes ───
        model.Entity<Listing>()
            .HasIndex(l => l.IsActive);

        model.Entity<Listing>()
            .HasIndex(l => new { l.IsActive, l.Type });

        model.Entity<Listing>()
            .HasIndex(l => new { l.IsActive, l.City });

        model.Entity<Listing>()
            .HasIndex(l => l.PriceFrom);

        model.Entity<Listing>()
            .HasIndex(l => l.CreatedAt);

        // ─── SupplierLocation indexes ───
        model.Entity<SupplierLocation>()
            .HasIndex(l => l.City);

        model.Entity<SupplierLocation>()
            .HasIndex(l => new { l.IsActive, l.City });

        // ─── SupplierLocation → Supplier (cascade) ───
        model.Entity<SupplierLocation>()
            .HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        // ─── Listing → SupplierLocation (optional FK, set null on delete) ───
        model.Entity<Listing>()
            .HasOne(e => e.Location)
            .WithMany(e => e.Listings)
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        model.Entity<Listing>()
            .HasIndex(l => l.LocationId);

        // ─── PlatformSetting primary key ───
        model.Entity<PlatformSetting>().HasKey(s => s.Key);

        // ─── Decimal precision ───
        foreach (var prop in model.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            prop.SetColumnType("numeric(18,4)");
        }
    }
}
