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

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        // ─── Enums as strings ───
        model.Entity<User>().Property(e => e.Role).HasConversion<string>();
        model.Entity<User>().Property(e => e.Status).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.IntegrationType).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.IntegrationHealth).HasConversion<string>();
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

        // ─── Decimal precision ───
        foreach (var prop in model.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            prop.SetColumnType("numeric(18,4)");
        }
    }
}
