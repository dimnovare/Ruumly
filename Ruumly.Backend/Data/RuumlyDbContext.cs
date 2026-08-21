using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Data;

public class RuumlyDbContext(DbContextOptions<RuumlyDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
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
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ListingExtra> ListingExtras => Set<ListingExtra>();
    public DbSet<PayoutEntry> PayoutEntries => Set<PayoutEntry>();
    public DbSet<RebateInvoice> RebateInvoices => Set<RebateInvoice>();
    public DbSet<BlockedDate> BlockedDates => Set<BlockedDate>();
    public DbSet<PollingLog>        PollingLogs        => Set<PollingLog>();
    public DbSet<ContractTemplate> ContractTemplates  => Set<ContractTemplate>();
    public DbSet<SignedContract>   SignedContracts     => Set<SignedContract>();
    public DbSet<DemandLead>       DemandLeads        => Set<DemandLead>();
    public DbSet<PaidFeature>      PaidFeatures       => Set<PaidFeature>();
    public DbSet<SupplierPaidFeature> SupplierPaidFeatures => Set<SupplierPaidFeature>();
    public DbSet<PaidFeatureRequest> PaidFeatureRequests => Set<PaidFeatureRequest>();
    public DbSet<Dispute>          Disputes           => Set<Dispute>();
    public DbSet<Offer>            Offers             => Set<Offer>();
    public DbSet<OfferOption>      OfferOptions       => Set<OfferOption>();
    public DbSet<ProviderOutreach> ProviderOutreaches => Set<ProviderOutreach>();
    public DbSet<LeadMessage>     LeadMessages       => Set<LeadMessage>();
    public DbSet<ProviderInfoRequest> ProviderInfoRequests => Set<ProviderInfoRequest>();
    public DbSet<SupplierClaim>    SupplierClaims     => Set<SupplierClaim>();
    public DbSet<EmailDeliveryEvent> EmailDeliveryEvents => Set<EmailDeliveryEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        // ─── Soft-delete global query filters ───
        model.Entity<Booking>().HasQueryFilter(b => !b.IsDeleted);
        model.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);

        // Silence EF Core warnings about query filter / required relationship mismatch.
        // These filters are always true — they just tell EF the relationship is filter-aware.
        model.Entity<Message>().HasQueryFilter(m => true);
        model.Entity<BookingTimeline>().HasQueryFilter(bt => true);
        model.Entity<Invoice>().HasQueryFilter(i => true);
        model.Entity<Review>().HasQueryFilter(r => true);
        model.Entity<OrderTimeline>().HasQueryFilter(ot => true);
        model.Entity<FulfillmentEvent>().HasQueryFilter(fe => true);
        model.Entity<PayoutEntry>().HasQueryFilter(pe => true);
        model.Entity<SignedContract>().HasQueryFilter(sc => true);

        // ─── Enums as strings ───
        model.Entity<User>().Property(e => e.Role).HasConversion<string>();
        model.Entity<User>().Property(e => e.Status).HasConversion<string>();
        // TRUE, stated here and not merely as a C# initializer. EF reads the
        // CLR default for a value type when it scaffolds a migration, so
        // `public bool ServesConsumers { get; set; } = true;` produced
        // `defaultValue: false` — which on apply would have set both columns
        // false for all 1,187 existing suppliers and made every fan-out skip
        // every provider as "business_only". Saying it here is what stops the
        // next scaffold regenerating that.
        model.Entity<Supplier>().Property(e => e.ServesConsumers).HasDefaultValue(true);
        model.Entity<Supplier>().Property(e => e.ServesRecurring).HasDefaultValue(true);
        model.Entity<Supplier>().Property(e => e.IntegrationType).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.IntegrationHealth).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.Tier).HasConversion<string>();
        model.Entity<Supplier>().Property(e => e.BillingModel).HasConversion<string>();
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
        model.Entity<PaidFeature>().Property(e => e.Category).HasConversion<string>();
        model.Entity<PaidFeature>().Property(e => e.Scope).HasConversion<string>();
        model.Entity<PaidFeatureRequest>().Property(e => e.Status).HasConversion<string>();

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
            .IsUnique()
            .HasFilter("\"RegistryCode\" IS NOT NULL");

        // Public partner pages are looked up by slug; uniqueness was only enforced
        // in app code. Make the DB the source of truth (and speed the lookup).
        model.Entity<Supplier>()
            .HasIndex(e => e.Slug)
            .IsUnique()
            .HasFilter("\"Slug\" IS NOT NULL");

        // The customer's status page looks a lead up by this token on every visit,
        // and the token IS the credential -- so the database, not app code, is
        // where "no two leads share one" has to be true. Filtered because every
        // row created before the status page existed carries null.
        model.Entity<DemandLead>()
            .HasIndex(e => e.StatusToken)
            .IsUnique()
            .HasFilter("\"StatusToken\" IS NOT NULL");

        // The Montonio webhook finds the invoice by PaymentOrderId on every payment
        // callback — index it (and enforce one invoice per payment order).
        model.Entity<Invoice>()
            .HasIndex(i => i.PaymentOrderId)
            .IsUnique()
            .HasFilter("\"PaymentOrderId\" IS NOT NULL");

        // The Resend webhook is public and RETRIED — this unique index is the
        // idempotency guard, not the app-code duplicate check in front of it.
        model.Entity<EmailDeliveryEvent>()
            .HasIndex(e => e.EventId)
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

        model.Entity<Booking>()
            .HasIndex(b => b.CreatedAt);

        model.Entity<Booking>()
            .HasIndex(b => b.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        model.Entity<Booking>()
            .HasIndex(b => b.SupplierId);

        // Composite for date-scoped supplier queries (provider dashboard, stats)
        model.Entity<Booking>()
            .HasIndex(b => new { b.SupplierId, b.CreatedAt });

        // Composite for status-filtered queries (ExpireReservations, cleanup jobs)
        model.Entity<Booking>()
            .HasIndex(b => new { b.SupplierId, b.Status });

        model.Entity<RefreshToken>()
            .HasIndex(t => new { t.TokenHash, t.IsRevoked });

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

        // ─── Listing full-text search vector ───
        model.Entity<Listing>()
            .Property(l => l.SearchVector)
            .HasColumnType("tsvector")
            .ValueGeneratedOnAddOrUpdate();   // populated by DB trigger, never by EF

        model.Entity<Listing>()
            .HasIndex(l => l.SearchVector)
            .HasMethod("GIN");

        // ─── Listing search indexes ───
        model.Entity<Listing>()
            .HasIndex(l => l.IsActive);

        model.Entity<Listing>()
            .HasIndex(l => new { l.IsActive, l.Type });

        model.Entity<Listing>()
            .HasIndex(l => new { l.IsActive, l.City });

        // GIN trigram index — enables fast ILike '%...%' partial-match on City.
        // Requires: CREATE EXTENSION IF NOT EXISTS pg_trgm (handled at startup).
        model.Entity<Listing>()
            .HasIndex(l => l.City)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("IX_Listings_City_Trgm");

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

        // ─── Review: one review per booking (unique), indexed by listing/supplier ───
        model.Entity<Review>()
            .HasIndex(r => r.BookingId)
            .IsUnique();

        model.Entity<Review>()
            .HasIndex(r => r.ListingId);

        model.Entity<Review>()
            .HasIndex(r => r.SupplierId);

        model.Entity<Review>()
            .HasOne(r => r.Booking)
            .WithMany()
            .HasForeignKey(r => r.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Review>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Review>()
            .HasOne(r => r.Listing)
            .WithMany()
            .HasForeignKey(r => r.ListingId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Review>()
            .HasOne(r => r.Supplier)
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // ─── ListingExtra → Listing (cascade) ───
        model.Entity<ListingExtra>()
            .HasOne(e => e.Listing)
            .WithMany(l => l.Extras)
            .HasForeignKey(e => e.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<ListingExtra>()
            .HasIndex(e => new { e.ListingId, e.Key })
            .IsUnique();

        // ─── PayoutEntry ───
        model.Entity<PayoutEntry>()
            .HasIndex(p => new { p.SupplierId, p.Status });

        model.Entity<PayoutEntry>()
            .Property(e => e.Status).HasConversion<string>();

        // ─── RebateInvoice ───
        model.Entity<RebateInvoice>()
            .HasIndex(r => new { r.SupplierId, r.Period })
            .IsUnique();

        model.Entity<RebateInvoice>()
            .Property(e => e.Status).HasConversion<string>();

        model.Entity<RebateInvoice>()
            .HasOne(r => r.Supplier)
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<RebateInvoice>().HasQueryFilter(r => true);

        // ─── BlockedDate ───
        model.Entity<BlockedDate>()
            .HasIndex(b => new { b.LocationId, b.Date })
            .IsUnique();

        // ─── ContractTemplate → Supplier (cascade delete) ───
        model.Entity<ContractTemplate>()
            .HasOne(c => c.Supplier)
            .WithMany()
            .HasForeignKey(c => c.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<ContractTemplate>()
            .HasIndex(c => c.SupplierId);

        // ─── SignedContract → Booking (1:1, cascade delete) ───
        model.Entity<SignedContract>()
            .HasOne(c => c.Booking)
            .WithOne(b => b.SignedContract)
            .HasForeignKey<SignedContract>(c => c.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<SignedContract>()
            .HasIndex(c => c.BookingId)
            .IsUnique();

        // ─── PollingLog → Supplier (cascade delete) ───
        model.Entity<PollingLog>()
            .HasOne(p => p.Supplier)
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<PollingLog>()
            .HasIndex(p => new { p.SupplierId, p.Timestamp });

        // ─── DemandLead ───
        model.Entity<DemandLead>(e =>
        {
            e.HasIndex(d => d.Email);
            // Partner-facing leads view filters by SupplierId; generic leads have it null.
            e.HasIndex(d => d.SupplierId);
            // The admin lead queue — the founder's most-hit screen — filters on
            // Status and orders by CreatedAt DESC (AdminLeadsController.GetLeads).
            // Without this it is a seq scan + in-memory sort of the whole table on
            // every load, degrading as leads accumulate. Composite so the filter
            // and the sort are one index seek; CreatedAt descending to match the
            // query's order so Postgres reads it forward.
            e.HasIndex(d => new { d.Status, d.CreatedAt })
                .HasDatabaseName("IX_DemandLeads_Status_CreatedAt");
            // Operator correspondence hangs off the lead and is read every time
            // the workspace opens one, newest first.
            e.HasMany<LeadMessage>()
                .WithOne(m => m.DemandLead)
                .HasForeignKey(m => m.DemandLeadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(d => d.Category).HasConversion<string>();
            e.Property(d => d.Status).HasConversion<string>();
        });

        // ─── Operator correspondence ───
        model.Entity<LeadMessage>(e =>
        {
            e.HasIndex(m => new { m.DemandLeadId, m.SentAt });
        });

        // ─── Offer loop (concierge) ───
        model.Entity<Offer>(e =>
        {
            // The public page resolves offers by token — unique and indexed.
            e.HasIndex(o => o.Token).IsUnique();
            e.HasIndex(o => o.DemandLeadId);
            e.Property(o => o.Status).HasConversion<string>();
            e.HasOne(o => o.DemandLead)
                .WithMany()
                .HasForeignKey(o => o.DemandLeadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<OfferOption>(e =>
        {
            e.HasIndex(o => o.OfferId);
            // Plain column, no FK (the option outlives its outreach row) — indexed
            // because it keys the provider quote's add-or-update lookup.
            e.HasIndex(o => o.CreatedFromOutreachId);
            e.HasOne(o => o.Offer)
                .WithMany(x => x.Options)
                .HasForeignKey(o => o.OfferId)
                .OnDelete(DeleteBehavior.Cascade);
            // Supplier linkage is optional (free-form options) — keep the
            // option alive if the supplier/location disappears.
            e.HasOne(o => o.Supplier)
                .WithMany()
                .HasForeignKey(o => o.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(o => o.SupplierLocation)
                .WithMany()
                .HasForeignKey(o => o.SupplierLocationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ProviderOutreach>(e =>
        {
            e.HasIndex(o => o.DemandLeadId);
            e.HasIndex(o => o.SupplierId);
            // The public quote page resolves a row by its token. Unique; nullable
            // (legacy rows have no token) — Postgres treats NULLs as distinct, so
            // many token-less rows coexist under a unique index.
            e.HasIndex(o => o.QuoteToken).IsUnique();
            e.Property(o => o.Status).HasConversion<string>();
            e.HasOne(o => o.DemandLead)
                .WithMany()
                .HasForeignKey(o => o.DemandLeadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Supplier)
                .WithMany()
                .HasForeignKey(o => o.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ProviderInfoRequest>(e =>
        {
            // The two questions ops actually asks of this table: "what is blocked
            // on THIS lead" (the workspace) and "has this provider already told
            // us, on this link" (the quote page's own read-back).
            e.HasIndex(r => r.DemandLeadId);
            e.HasIndex(r => r.ProviderOutreachId);
            // Cascade from all three parents. The row is a fact ABOUT a specific
            // (lead, supplier, outreach) triple and means nothing once any of them
            // is gone — unlike OfferOption, which deliberately outlives its
            // outreach because it is customer-facing work product.
            e.HasOne(r => r.DemandLead)
                .WithMany()
                .HasForeignKey(r => r.DemandLeadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Supplier)
                .WithMany()
                .HasForeignKey(r => r.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.ProviderOutreach)
                .WithMany()
                .HasForeignKey(r => r.ProviderOutreachId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SupplierClaim>(e =>
        {
            e.HasIndex(c => c.SupplierId);
            // Both credentials are resolved by an indexed equality match on the
            // stored HASH — the raw token never touches the database. Unique and
            // nullable: Postgres treats NULLs as distinct, so the many no-match
            // rows (which carry no token at all) coexist under the index, exactly
            // like token-less legacy ProviderOutreach rows.
            e.HasIndex(c => c.TokenHash).IsUnique();
            e.HasIndex(c => c.SessionTokenHash).IsUnique();
            e.HasOne(c => c.Supplier)
                .WithMany()
                .HasForeignKey(c => c.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── Dispute ───
        model.Entity<Dispute>(e =>
        {
            e.HasIndex(d => d.Status);
            e.HasIndex(d => new { d.SupplierId, d.Status });
            e.Property(d => d.Type).HasConversion<string>();
            e.Property(d => d.Status).HasConversion<string>();
            e.Property(d => d.RaisedByRole).HasConversion<string>();
        });

        // ─── Paid feature catalog / activations ───
        model.Entity<PaidFeature>()
            .HasIndex(f => f.Code)
            .IsUnique();

        model.Entity<SupplierPaidFeature>()
            .HasOne(f => f.Supplier)
            .WithMany()
            .HasForeignKey(f => f.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<SupplierPaidFeature>()
            .HasOne(f => f.PaidFeature)
            .WithMany()
            .HasForeignKey(f => f.PaidFeatureId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<SupplierPaidFeature>()
            .HasOne(f => f.Listing)
            .WithMany()
            .HasForeignKey(f => f.ListingId)
            .OnDelete(DeleteBehavior.SetNull);

        model.Entity<SupplierPaidFeature>()
            .HasOne(f => f.Location)
            .WithMany()
            .HasForeignKey(f => f.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        model.Entity<SupplierPaidFeature>()
            .HasIndex(f => new { f.SupplierId, f.IsActive, f.EndsAt });

        model.Entity<SupplierPaidFeature>()
            .HasIndex(f => new { f.PaidFeatureId, f.SupplierId, f.ListingId, f.LocationId });

        model.Entity<PaidFeatureRequest>()
            .HasOne(r => r.Supplier)
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<PaidFeatureRequest>()
            .HasOne(r => r.PaidFeature)
            .WithMany()
            .HasForeignKey(r => r.PaidFeatureId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<PaidFeatureRequest>()
            .HasOne(r => r.Listing)
            .WithMany()
            .HasForeignKey(r => r.ListingId)
            .OnDelete(DeleteBehavior.SetNull);

        model.Entity<PaidFeatureRequest>()
            .HasOne(r => r.Location)
            .WithMany()
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        model.Entity<PaidFeatureRequest>()
            .HasIndex(r => new { r.SupplierId, r.Status, r.CreatedAt });

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
