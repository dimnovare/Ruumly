using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using BC = BCrypt.Net.BCrypt;

namespace Ruumly.Backend.Data;

public static class SeedData
{
    // ─── Deterministic Guid from string key (MD5, same algo as spec) ─────────
    private static Guid G(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };
    private static string J(object obj) => JsonSerializer.Serialize(obj, _json);

    public static async Task SeedAsync(RuumlyDbContext db)
    {
        Console.WriteLine("[Seed] Starting...");
        try
        {
            await SeedSuppliersAsync(db);
            await SeedIntegrationSettingsAsync(db);
            await SeedListingsAsync(db);
            await SeedLocationsAsync(db);
            await SeedListingExtrasAsync(db);
            await SeedUsersAsync(db);
            await SeedRoutingRulesAsync(db);
            await SeedBookingsAsync(db);
            await SeedReviewsAsync(db);
            await SeedPlatformSettingsAsync(db);
            await SeedFeatureDefinitionsAsync(db);
            await SeedKookonAsync(db);   // env-gated: Development only
            await SeedBoxoAsync(db);     // env-gated: Development only
            Console.WriteLine("[Seed] Complete.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Seed] FAILED: {ex.Message}");
            Console.WriteLine(ex.ToString());
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUPPLIERS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedSuppliersAsync(RuumlyDbContext db)
    {
        if (await db.Suppliers.AnyAsync()) return;

        db.Suppliers.AddRange(new List<Supplier>
        {
            new() {
                Id                  = G("sup-1"),
                Name                = "Laobox OÜ",
                RegistryCode        = "14523678",
                ContactName         = "Mart Kivi",
                ContactEmail        = "mart@laobox.ee",
                ContactPhone        = "+372 5123 4567",
                IntegrationType     = IntegrationType.Api,
                ApiEndpoint         = "https://api.laobox.ee/v1/orders",
                ApiAuthType         = "bearer",
                IsActive            = true,
                Tier                = SupplierTier.Premium,
                IntegrationHealth   = IntegrationHealth.Healthy,
                PartnerDiscountRate = 10m,
                ClientDiscountRate  = 5m,
                CreatedAt           = Utc(2025, 8, 15),
                UpdatedAt           = Utc(2025, 8, 15),
            },
            new() {
                Id                  = G("sup-2"),
                Name                = "MiniLadu AS",
                RegistryCode        = "11234567",
                ContactName         = "Tiina Rebane",
                ContactEmail        = "tiina@miniladu.ee",
                ContactPhone        = "+372 5234 5678",
                IntegrationType     = IntegrationType.Email,
                RecipientEmail      = "tiina@miniladu.ee",
                IsActive            = true,
                IntegrationHealth   = IntegrationHealth.Healthy,
                PartnerDiscountRate = 8m,
                ClientDiscountRate  = 3m,
                CreatedAt           = Utc(2025, 9, 1),
                UpdatedAt           = Utc(2025, 9, 1),
            },
            new() {
                Id                  = G("sup-3"),
                Name                = "SecureStore OÜ",
                RegistryCode        = "16789012",
                ContactName         = "Jaan Tamm",
                ContactEmail        = "jaan@securestore.ee",
                ContactPhone        = "+372 5345 6789",
                IntegrationType     = IntegrationType.Api,
                ApiEndpoint         = "https://api.securestore.ee/bookings",
                ApiAuthType         = "apikey",
                IsActive            = true,
                IntegrationHealth   = IntegrationHealth.Healthy,
                PartnerDiscountRate = 12m,
                ClientDiscountRate  = 0m,
                CreatedAt           = Utc(2025, 9, 20),
                UpdatedAt           = Utc(2025, 9, 20),
            },
            new() {
                Id                  = G("sup-4"),
                Name                = "KoliExpress OÜ",
                RegistryCode        = "12345678",
                ContactName         = "Andres Pärn",
                ContactEmail        = "andres@koliexpress.ee",
                ContactPhone        = "+372 5456 7890",
                IntegrationType     = IntegrationType.Email,
                RecipientEmail      = "andres@koliexpress.ee",
                IsActive            = true,
                IntegrationHealth   = IntegrationHealth.Healthy,
                PartnerDiscountRate = 15m,
                ClientDiscountRate  = 5m,
                CreatedAt           = Utc(2025, 10, 5),
                UpdatedAt           = Utc(2025, 10, 5),
            },
            new() {
                Id                  = G("sup-5"),
                Name                = "HaagisRent OÜ",
                RegistryCode        = "13456789",
                ContactName         = "Kristjan Mägi",
                ContactEmail        = "kristjan@haagisrent.ee",
                ContactPhone        = "+372 5567 8901",
                IntegrationType     = IntegrationType.Manual,
                RecipientEmail      = "kristjan@haagisrent.ee",
                IsActive            = true,
                IntegrationHealth   = IntegrationHealth.Degraded,
                PartnerDiscountRate = 5m,
                ClientDiscountRate  = 0m,
                Notes               = "Manuaalne protsess, vajalik operaatori sekkumine",
                CreatedAt           = Utc(2025, 10, 15),
                UpdatedAt           = Utc(2025, 10, 15),
            },
            new() {
                Id                  = G("sup-6"),
                Name                = "Pärnu Ladu OÜ",
                RegistryCode        = "15678901",
                ContactName         = "Liis Sepp",
                ContactEmail        = "liis@parnuladu.ee",
                ContactPhone        = "+372 5678 9012",
                IntegrationType     = IntegrationType.Manual,
                RecipientEmail      = "liis@parnuladu.ee",
                IsActive            = false,
                IntegrationHealth   = IntegrationHealth.Offline,
                PartnerDiscountRate = 0m,
                ClientDiscountRate  = 0m,
                Notes               = "Mitteaktiivne partner, lepingu uuendamine ootel",
                CreatedAt           = Utc(2025, 11, 1),
                UpdatedAt           = Utc(2025, 11, 1),
            },

            // ─── Estonia (sup-7..sup-32) ─────────────────────────────────────
            new() { Id = G("sup-7"),  Name = "Tallinna Hoidla OÜ",      RegistryCode = "16100007", Country = "EE",
                    ContactName = "Mart Saar",        ContactEmail = "info@tallinnahoidla.ee",      ContactPhone = "+372 5512 3456",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@tallinnahoidla.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 13m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 11, 12), UpdatedAt = Utc(2025, 11, 12) },
            new() { Id = G("sup-8"),  Name = "Eesti Logistika OÜ",      RegistryCode = "16100008", Country = "EE",
                    ContactName = "Tiina Kask",       ContactEmail = "kontor@eestilogistika.ee",    ContactPhone = "+372 5523 4567",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "kontor@eestilogistika.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 15m, ClientDiscountRate = 6m,
                    CreatedAt = Utc(2025, 12, 3),  UpdatedAt = Utc(2025, 12, 3)  },
            new() { Id = G("sup-9"),  Name = "Lao24 OÜ",                RegistryCode = "16100009", Country = "EE",
                    ContactName = "Jaan Toom",        ContactEmail = "tartu@lao24.ee",              ContactPhone = "+372 5234 5678",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "tartu@lao24.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2025, 9, 22),  UpdatedAt = Utc(2025, 9, 22)  },
            new() { Id = G("sup-19"), Name = "Mustamäe Hoidla OÜ",      RegistryCode = "16100019", Country = "EE",
                    ContactName = "Liis Mägi",        ContactEmail = "info@mustamaehoidla.ee",      ContactPhone = "+372 5111 2233",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@mustamaehoidla.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 10m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2025, 12, 15), UpdatedAt = Utc(2025, 12, 15) },
            new() { Id = G("sup-20"), Name = "Lasnamäe Storage OÜ",     RegistryCode = "16100020", Country = "EE",
                    ContactName = "Andres Tamm",      ContactEmail = "hello@lasnamaestorage.ee",    ContactPhone = "+372 5122 3344",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "hello@lasnamaestorage.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2026, 1, 8),   UpdatedAt = Utc(2026, 1, 8)   },
            new() { Id = G("sup-21"), Name = "Põhja Lao OÜ",            RegistryCode = "16100021", Country = "EE",
                    ContactName = "Kati Lill",        ContactEmail = "info@pohjalao.ee",            ContactPhone = "+372 5133 4455",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@pohjalao.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 2, 2),   UpdatedAt = Utc(2026, 2, 2)   },
            new() { Id = G("sup-22"), Name = "KesklinnBox OÜ",          RegistryCode = "16100022", Country = "EE",
                    ContactName = "Kristjan Vahter",  ContactEmail = "kontor@kesklinnbox.ee",       ContactPhone = "+372 5144 5566",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "kontor@kesklinnbox.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 12m, ClientDiscountRate = 6m,
                    CreatedAt = Utc(2026, 1, 25),  UpdatedAt = Utc(2026, 1, 25)  },
            new() { Id = G("sup-23"), Name = "Kristiine Hoidla OÜ",     RegistryCode = "16100023", Country = "EE",
                    ContactName = "Anu Pärn",         ContactEmail = "info@kristiinehoidla.ee",     ContactPhone = "+372 5155 6677",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@kristiinehoidla.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 10m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2025, 10, 4),  UpdatedAt = Utc(2025, 10, 4)  },
            new() { Id = G("sup-24"), Name = "Viimsi Storage OÜ",       RegistryCode = "16100024", Country = "EE",
                    ContactName = "Toomas Sepp",      ContactEmail = "hello@viimsistorage.ee",      ContactPhone = "+372 5166 7788",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "hello@viimsistorage.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 3, 12),  UpdatedAt = Utc(2026, 3, 12)  },
            new() { Id = G("sup-25"), Name = "Kolimine Pluss OÜ",       RegistryCode = "16100025", Country = "EE",
                    ContactName = "Triin Lepik",      ContactEmail = "info@kolipluss.ee",           ContactPhone = "+372 5177 8899",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@kolipluss.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 14m, ClientDiscountRate = 7m,
                    CreatedAt = Utc(2025, 11, 28), UpdatedAt = Utc(2025, 11, 28) },
            new() { Id = G("sup-26"), Name = "Tartu Hoiuruum OÜ",       RegistryCode = "16100026", Country = "EE",
                    ContactName = "Erki Roos",        ContactEmail = "info@hoiuruum.ee",            ContactPhone = "+372 5288 9900",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@hoiuruum.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 10, 18), UpdatedAt = Utc(2025, 10, 18) },
            new() { Id = G("sup-27"), Name = "Annelinna Lao OÜ",        RegistryCode = "16100027", Country = "EE",
                    ContactName = "Helle Rebane",     ContactEmail = "annelinn@lao.ee",             ContactPhone = "+372 5299 0011",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "annelinn@lao.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 2, 20),  UpdatedAt = Utc(2026, 2, 20)  },
            new() { Id = G("sup-28"), Name = "Riia Mini-Lao OÜ",        RegistryCode = "16100028", Country = "EE",
                    ContactName = "Jaak Mets",        ContactEmail = "riia@minilao.ee",             ContactPhone = "+372 5310 1122",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "riia@minilao.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 9m,  ClientDiscountRate = 4m,
                    CreatedAt = Utc(2026, 3, 4),   UpdatedAt = Utc(2026, 3, 4)   },
            new() { Id = G("sup-29"), Name = "Pärnu Beach Storage OÜ",  RegistryCode = "16100029", Country = "EE",
                    ContactName = "Riin Kuusk",       ContactEmail = "info@beachstorage.ee",        ContactPhone = "+372 5321 2233",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@beachstorage.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 12m, ClientDiscountRate = 6m,
                    CreatedAt = Utc(2025, 12, 19), UpdatedAt = Utc(2025, 12, 19) },
            new() { Id = G("sup-30"), Name = "Mai Hoidla OÜ",           RegistryCode = "16100030", Country = "EE",
                    ContactName = "Maarja Põld",      ContactEmail = "hello@maihoidla.ee",          ContactPhone = "+372 5332 3344",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "hello@maihoidla.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 1, 30),  UpdatedAt = Utc(2026, 1, 30)  },
            new() { Id = G("sup-31"), Name = "Narva Lao Keskus OÜ",     RegistryCode = "16100031", Country = "EE",
                    ContactName = "Peeter Aru",       ContactEmail = "info@narvalao.ee",            ContactPhone = "+372 5343 4455",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@narvalao.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 9, 30),  UpdatedAt = Utc(2025, 9, 30)  },
            new() { Id = G("sup-32"), Name = "Sillamäe Storage OÜ",     RegistryCode = "16100032", Country = "EE",
                    ContactName = "Kalev Vesi",       ContactEmail = "sillamae@storage.ee",         ContactPhone = "+372 5354 5566",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "sillamae@storage.ee",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 9m,  ClientDiscountRate = 4m,
                    CreatedAt = Utc(2026, 4, 5),   UpdatedAt = Utc(2026, 4, 5)   },

            // ─── Latvia (sup-33..sup-48) ─────────────────────────────────────
            new() { Id = G("sup-33"), Name = "Rīgas Noliktavas SIA",    RegistryCode = "40103000033", Country = "LV",
                    ContactName = "Jānis Bērziņš",    ContactEmail = "info@rigasnoliktavas.lv",     ContactPhone = "+371 2812 3456",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@rigasnoliktavas.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 15m, ClientDiscountRate = 7m,
                    CreatedAt = Utc(2025, 12, 8),  UpdatedAt = Utc(2025, 12, 8)  },
            new() { Id = G("sup-34"), Name = "BalticBox SIA",           RegistryCode = "40103000034", Country = "LV",
                    ContactName = "Anna Ozoliņa",     ContactEmail = "hello@balticbox.lv",          ContactPhone = "+371 2823 4567",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "hello@balticbox.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 14m, ClientDiscountRate = 6m,
                    CreatedAt = Utc(2026, 1, 15),  UpdatedAt = Utc(2026, 1, 15)  },
            new() { Id = G("sup-35"), Name = "StoragePro Latvija SIA",  RegistryCode = "40103000035", Country = "LV",
                    ContactName = "Pēteris Liepa",    ContactEmail = "sales@storagepro.lv",         ContactPhone = "+371 2834 5678",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "sales@storagepro.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2026, 2, 12),  UpdatedAt = Utc(2026, 2, 12)  },
            new() { Id = G("sup-36"), Name = "Centra Noliktava SIA",    RegistryCode = "40103000036", Country = "LV",
                    ContactName = "Inga Kalniņa",     ContactEmail = "info@centranoliktava.lv",     ContactPhone = "+371 2845 6789",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@centranoliktava.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 10m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2025, 10, 22), UpdatedAt = Utc(2025, 10, 22) },
            new() { Id = G("sup-37"), Name = "Pārdaugavas Glabātavas SIA", RegistryCode = "40103000037", Country = "LV",
                    ContactName = "Andris Krūmiņš",   ContactEmail = "info@pardaugavas.lv",         ContactPhone = "+371 2856 7890",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@pardaugavas.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 12m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 11, 18), UpdatedAt = Utc(2025, 11, 18) },
            new() { Id = G("sup-38"), Name = "Mežaparka Storage SIA",   RegistryCode = "40103000038", Country = "LV",
                    ContactName = "Līga Eglīte",      ContactEmail = "mezaparks@storage.lv",        ContactPhone = "+371 2867 8901",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "mezaparks@storage.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 2, 25),  UpdatedAt = Utc(2026, 2, 25)  },
            new() { Id = G("sup-39"), Name = "Imanta Lao SIA",          RegistryCode = "40103000039", Country = "LV",
                    ContactName = "Mārtiņš Vītols",   ContactEmail = "imanta@lao.lv",               ContactPhone = "+371 2878 9012",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "imanta@lao.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 9m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 3, 9),   UpdatedAt = Utc(2026, 3, 9)   },
            new() { Id = G("sup-40"), Name = "Purvciema Noliktava SIA", RegistryCode = "40103000040", Country = "LV",
                    ContactName = "Anita Lapiņa",     ContactEmail = "info@purvciems.lv",           ContactPhone = "+371 2889 0123",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@purvciems.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 10m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2025, 12, 27), UpdatedAt = Utc(2025, 12, 27) },
            new() { Id = G("sup-41"), Name = "Pārvešana Rīga SIA",      RegistryCode = "40103000041", Country = "LV",
                    ContactName = "Kārlis Skujiņš",   ContactEmail = "sales@parvesana.lv",          ContactPhone = "+371 2890 1234",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "sales@parvesana.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 13m, ClientDiscountRate = 6m,
                    CreatedAt = Utc(2026, 1, 22),  UpdatedAt = Utc(2026, 1, 22)  },
            new() { Id = G("sup-42"), Name = "Daugavpils Storage SIA",  RegistryCode = "40103000042", Country = "LV",
                    ContactName = "Sandra Zariņa",    ContactEmail = "info@dgstorage.lv",           ContactPhone = "+371 2901 2345",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@dgstorage.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 10, 30), UpdatedAt = Utc(2025, 10, 30) },
            new() { Id = G("sup-43"), Name = "DGV Glabātavas SIA",      RegistryCode = "40103000043", Country = "LV",
                    ContactName = "Edgars Kalns",     ContactEmail = "dgv@glabatavas.lv",           ContactPhone = "+371 2912 3456",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "dgv@glabatavas.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 3, 18),  UpdatedAt = Utc(2026, 3, 18)  },
            new() { Id = G("sup-44"), Name = "Cietoksnis Lao SIA",      RegistryCode = "40103000044", Country = "LV",
                    ContactName = "Ilze Pētersone",   ContactEmail = "cietoksnis@lao.lv",           ContactPhone = "+371 2923 4567",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "cietoksnis@lao.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 9m,  ClientDiscountRate = 4m,
                    CreatedAt = Utc(2026, 4, 1),   UpdatedAt = Utc(2026, 4, 1)   },
            new() { Id = G("sup-45"), Name = "Liepājas Hoidla SIA",     RegistryCode = "40103000045", Country = "LV",
                    ContactName = "Raivis Strautiņš", ContactEmail = "birojs@liepajashoidla.lv",    ContactPhone = "+371 2934 5678",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "birojs@liepajashoidla.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 12m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 11, 5),  UpdatedAt = Utc(2025, 11, 5)  },
            new() { Id = G("sup-46"), Name = "Karostas Storage SIA",    RegistryCode = "40103000046", Country = "LV",
                    ContactName = "Iveta Kļaviņa",    ContactEmail = "karosta@storage.lv",          ContactPhone = "+371 2945 6789",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "karosta@storage.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 4, 8),   UpdatedAt = Utc(2026, 4, 8)   },
            new() { Id = G("sup-47"), Name = "Jelgavas Noliktava SIA",  RegistryCode = "40103000047", Country = "LV",
                    ContactName = "Aigars Apinis",    ContactEmail = "info@jelgnolikta.lv",         ContactPhone = "+371 2956 7890",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@jelgnolikta.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2025, 12, 12), UpdatedAt = Utc(2025, 12, 12) },
            new() { Id = G("sup-48"), Name = "Jūrmalas Glabātavas SIA", RegistryCode = "40103000048", Country = "LV",
                    ContactName = "Dace Ābele",       ContactEmail = "info@jurmalas.lv",            ContactPhone = "+371 2967 8901",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@jurmalas.lv",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 9m,  ClientDiscountRate = 4m,
                    CreatedAt = Utc(2026, 3, 22),  UpdatedAt = Utc(2026, 3, 22)  },

            // ─── Lithuania (sup-49..sup-60) ──────────────────────────────────
            new() { Id = G("sup-49"), Name = "Vilniaus Sandėliai UAB",  RegistryCode = "304000049", Country = "LT",
                    ContactName = "Tomas Petrauskas", ContactEmail = "info@vilnsand.lt",            ContactPhone = "+370 6112 3456",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@vilnsand.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 15m, ClientDiscountRate = 7m,
                    CreatedAt = Utc(2025, 12, 22), UpdatedAt = Utc(2025, 12, 22) },
            new() { Id = G("sup-50"), Name = "LietuvosBox UAB",          RegistryCode = "304000050", Country = "LT",
                    ContactName = "Rasa Kazlauskienė", ContactEmail = "sales@lietuvosbox.lt",       ContactPhone = "+370 6123 4567",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "sales@lietuvosbox.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 14m, ClientDiscountRate = 6m,
                    CreatedAt = Utc(2026, 1, 19),  UpdatedAt = Utc(2026, 1, 19)  },
            new() { Id = G("sup-51"), Name = "Saugykla LT UAB",          RegistryCode = "304000051", Country = "LT",
                    ContactName = "Andrius Jankauskas", ContactEmail = "info@saugykla.lt",          ContactPhone = "+370 6134 5678",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@saugykla.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2026, 2, 8),   UpdatedAt = Utc(2026, 2, 8)   },
            new() { Id = G("sup-52"), Name = "Antakalnio Sandėliai UAB", RegistryCode = "304000052", Country = "LT",
                    ContactName = "Gintarė Stankevičienė", ContactEmail = "antakalnis@sandeliai.lt", ContactPhone = "+370 6145 6789",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "antakalnis@sandeliai.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 10m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2025, 10, 12), UpdatedAt = Utc(2025, 10, 12) },
            new() { Id = G("sup-53"), Name = "Naujamiesčio Saugykla UAB", RegistryCode = "304000053", Country = "LT",
                    ContactName = "Mantas Žukauskas", ContactEmail = "naujamiestis@saugykla.lt",    ContactPhone = "+370 6156 7890",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "naujamiestis@saugykla.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 12m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 11, 22), UpdatedAt = Utc(2025, 11, 22) },
            new() { Id = G("sup-54"), Name = "Šnipiškių Lao UAB",        RegistryCode = "304000054", Country = "LT",
                    ContactName = "Aušra Paulauskienė", ContactEmail = "snipiskes@lao.lt",          ContactPhone = "+370 6167 8901",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "snipiskes@lao.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 3, 15),  UpdatedAt = Utc(2026, 3, 15)  },
            new() { Id = G("sup-55"), Name = "Žirmūnų Storage UAB",      RegistryCode = "304000055", Country = "LT",
                    ContactName = "Darius Vasiliauskas", ContactEmail = "zirmunai@storage.lt",      ContactPhone = "+370 6178 9012",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "zirmunai@storage.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 9m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 2, 28),  UpdatedAt = Utc(2026, 2, 28)  },
            new() { Id = G("sup-56"), Name = "Kauno Sandėliavimas UAB",  RegistryCode = "304000056", Country = "LT",
                    ContactName = "Lina Butkevičienė", ContactEmail = "kontaktai@kaunsand.lt",      ContactPhone = "+370 6189 0123",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "kontaktai@kaunsand.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Premium,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 13m, ClientDiscountRate = 6m,
                    CreatedAt = Utc(2025, 12, 1),  UpdatedAt = Utc(2025, 12, 1)  },
            new() { Id = G("sup-57"), Name = "Centro Saugykla Kaunas UAB", RegistryCode = "304000057", Country = "LT",
                    ContactName = "Žydrūnas Kavaliauskas", ContactEmail = "info@centrosaugykla.lt", ContactPhone = "+370 6190 1234",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@centrosaugykla.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 4m,
                    CreatedAt = Utc(2026, 1, 4),   UpdatedAt = Utc(2026, 1, 4)   },
            new() { Id = G("sup-58"), Name = "Aleksoto Lao UAB",         RegistryCode = "304000058", Country = "LT",
                    ContactName = "Justė Šimkutė",    ContactEmail = "aleksotas@lao.lt",            ContactPhone = "+370 6201 2345",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "aleksotas@lao.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 8m,  ClientDiscountRate = 3m,
                    CreatedAt = Utc(2026, 4, 12),  UpdatedAt = Utc(2026, 4, 12)  },
            new() { Id = G("sup-59"), Name = "Klaipėdos Sandėliai UAB",  RegistryCode = "304000059", Country = "LT",
                    ContactName = "Vytautas Adomaitis", ContactEmail = "info@klaipsand.lt",         ContactPhone = "+370 6212 3456",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "info@klaipsand.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Standard, BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 11m, ClientDiscountRate = 5m,
                    CreatedAt = Utc(2025, 9, 15),  UpdatedAt = Utc(2025, 9, 15)  },
            new() { Id = G("sup-60"), Name = "Smiltynės Storage UAB",    RegistryCode = "304000060", Country = "LT",
                    ContactName = "Ieva Marcinkevičienė", ContactEmail = "smiltyne@storage.lt",     ContactPhone = "+370 6223 4567",
                    IntegrationType = IntegrationType.Email, RecipientEmail = "smiltyne@storage.lt",
                    IsActive = true, IsVerified = true, IntegrationHealth = IntegrationHealth.Healthy,
                    Tier = SupplierTier.Starter,  BillingModel = BillingModel.Marketplace,
                    PartnerDiscountRate = 9m,  ClientDiscountRate = 4m,
                    CreatedAt = Utc(2026, 3, 30),  UpdatedAt = Utc(2026, 3, 30)  },
        });

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Suppliers done.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INTEGRATION SETTINGS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedIntegrationSettingsAsync(RuumlyDbContext db)
    {
        if (await db.IntegrationSettings.AnyAsync()) return;

        db.IntegrationSettings.AddRange(new List<IntegrationSettings>
        {
            new() {
                Id                  = G("int-1"),
                SupplierId          = G("sup-1"),
                ApprovalMode        = ApprovalMode.Auto,
                PostingMode         = PostingMode.Api,
                FallbackPostingMode = PostingMode.Email,
                MappingProfile      = "laobox_v2",
                LastTestedAt        = Utc(2026, 3, 20, 14, 30),
                LastTestResult      = "success",
                IsActive            = true,
                UpdatedAt           = Utc(2026, 3, 20, 14, 30),
            },
            new() {
                Id                  = G("int-2"),
                SupplierId          = G("sup-2"),
                ApprovalMode        = ApprovalMode.Admin,
                PostingMode         = PostingMode.Email,
                FallbackPostingMode = PostingMode.Manual,
                MappingProfile      = "default",
                IsActive            = true,
                UpdatedAt           = Utc(2025, 9, 1),
            },
            new() {
                Id                  = G("int-3"),
                SupplierId          = G("sup-3"),
                ApprovalMode        = ApprovalMode.Auto,
                PostingMode         = PostingMode.Api,
                FallbackPostingMode = PostingMode.Email,
                MappingProfile      = "securestore_v1",
                LastTestedAt        = Utc(2026, 3, 19, 9, 15),
                LastTestResult      = "success",
                IsActive            = true,
                UpdatedAt           = Utc(2026, 3, 19, 9, 15),
            },
            new() {
                Id                  = G("int-4"),
                SupplierId          = G("sup-4"),
                ApprovalMode        = ApprovalMode.Provider,
                PostingMode         = PostingMode.Email,
                FallbackPostingMode = PostingMode.Manual,
                MappingProfile      = "default",
                IsActive            = true,
                UpdatedAt           = Utc(2025, 10, 5),
            },
            new() {
                Id                  = G("int-5"),
                SupplierId          = G("sup-5"),
                ApprovalMode        = ApprovalMode.Admin,
                PostingMode         = PostingMode.Manual,
                FallbackPostingMode = PostingMode.Email,
                IsActive            = false,
                UpdatedAt           = Utc(2025, 10, 15),
            },
        });

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] IntegrationSettings done.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LISTINGS — 14 total (w1–w6, m1–m4, t1–t4)
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedListingsAsync(RuumlyDbContext db)
    {
        if (await db.Listings.AnyAsync()) return;

        // ImagesJson helper: primary image first, then gallery
        static string Imgs(string primary, params string[] gallery)
        {
            var all = new List<string> { primary };
            all.AddRange(gallery);
            return JsonSerializer.Serialize(all);
        }

        // FeaturesJson helpers per type
        static string WarehouseFeatures(int size, string sizeUnit,
            bool heated, bool indoor, bool access24_7, bool security,
            bool loadingDock, bool forklift, bool shortTerm, bool longTerm,
            string[] features) => J(new
            {
                size, sizeUnit, heated, indoor, access24_7, security,
                loadingDock, forklift, shortTerm, longTerm, features
            });

        static string MovingFeatures(string[] serviceArea,
            bool withVan, bool packingHelp, bool loadingHelp,
            string pricingModel, string[] services) => J(new
            {
                serviceArea, withVan, packingHelp, loadingHelp, pricingModel, services
            });

        static string TrailerFeatures(string trailerType, string weightClass,
            string[] requirements) => J(new { trailerType, weightClass, requirements });

        db.Listings.AddRange(new List<Listing>
        {
            // ── WAREHOUSES ──────────────────────────────────────────────────
            new() {
                Id           = G("w1"),
                Type         = ListingType.Warehouse,
                SupplierId   = G("sup-1"),
                Title        = "Laobox Tallinn Kesklinn",
                Address      = "Pärnu mnt 139",
                City         = "Tallinn",
                Lat          = 59.4127,
                Lng          = 24.7277,
                PriceFrom    = 49m,
                PriceUnit    = "€/kuu",
                SizeM2       = 5m,
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.8m,
                ReviewCount  = 124,
                Badge        = ListingBadge.Promoted,
                Description  = "Kaasaegne iseteeninduslik laoruum Tallinna kesklinnas. Ideaalne nii eraklientidele kui ettevõtetele.",
                ImagesJson   = Imgs(
                    "https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop",
                    "https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=800&h=600&fit=crop",
                    "https://images.unsplash.com/photo-1553413077-190dd305871c?w=800&h=600&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²",
                    heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Kliimakontroll", "VideoValve 24/7", "Iseteenindus", "Lihtne juurdepääs"]),
                CreatedAt    = Utc(2025, 8, 15),
                UpdatedAt    = Utc(2025, 8, 15),
            },
            new() {
                Id           = G("w2"),
                Type         = ListingType.Warehouse,
                SupplierId   = G("sup-2"),
                Title        = "MiniLadu Tartu",
                Address      = "Ringtee 75",
                City         = "Tartu",
                Lat          = 58.3726,
                Lng          = 26.7158,
                PriceFrom    = 29m,
                PriceUnit    = "€/kuu",
                SizeM2       = 2.5m,
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.5m,
                ReviewCount  = 67,
                Badge        = ListingBadge.Cheapest,
                Description  = "Soodne laoruum Tartus. Sobiv mööbli, hooajaasjade või ärikauba hoiustamiseks.",
                ImagesJson   = Imgs(
                    "https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop",
                    "https://images.unsplash.com/photo-1553413077-190dd305871c?w=800&h=600&fit=crop"),
                FeaturesJson = WarehouseFeatures(3, "m²",
                    heated: false, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Laadimisplatvorm", "Valve", "Paindlikud lepingud"]),
                CreatedAt    = Utc(2025, 9, 1),
                UpdatedAt    = Utc(2025, 9, 1),
            },
            new() {
                Id           = G("w3"),
                Type         = ListingType.Warehouse,
                SupplierId   = G("sup-3"),
                Title        = "SecureStore Ülemiste",
                Address      = "Suur-Sõjamäe 10a",
                City         = "Tallinn",
                Lat          = 59.4219,
                Lng          = 24.7955,
                PriceFrom    = 79m,
                PriceUnit    = "€/kuu",
                SizeM2       = 10m,
                AvailableNow = false,
                IsActive     = true,
                Rating       = 4.9m,
                ReviewCount  = 203,
                Badge        = ListingBadge.BestValue,
                Description  = "Kõrgeima turvatasemega laoruum Ülemiste piirkonnas. Ideaalne väärtuslikuma kauba hoiustamiseks.",
                ImagesJson   = Imgs(
                    "https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop",
                    "https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=800&h=600&fit=crop"),
                FeaturesJson = WarehouseFeatures(10, "m²",
                    heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: false, longTerm: true,
                    features: ["Kaheastmeline turvakontroll", "Kindlustus", "Tõstuk", "Laadimisplatvorm", "Kliimakontroll"]),
                CreatedAt    = Utc(2025, 9, 20),
                UpdatedAt    = Utc(2025, 9, 20),
            },
            new() {
                Id           = G("w4"),
                Type         = ListingType.Warehouse,
                SupplierId   = G("sup-6"),
                Title        = "Pärnu Laokeskus",
                Address      = "Savi 25",
                City         = "Pärnu",
                Lat          = 58.3859,
                Lng          = 24.4971,
                PriceFrom    = 35m,
                PriceUnit    = "€/kuu",
                SizeM2       = 1m,
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.2m,
                ReviewCount  = 31,
                Badge        = null,
                Description  = "Taskukohane laopind Pärnus. Sobib hooajaasjade ja väikeettevõtte vajadusteks.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²",
                    heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Valve", "Hea asukoht", "Paindlik leping"]),
                CreatedAt    = Utc(2025, 11, 1),
                UpdatedAt    = Utc(2025, 11, 1),
            },
            new() {
                Id           = G("w5"),
                Type         = ListingType.Warehouse,
                SupplierId   = G("sup-1"),
                Title        = "NordicStorage Tallinn",
                Address      = "Kadaka tee 56",
                City         = "Tallinn",
                Lat          = 59.3956,
                Lng          = 24.6651,
                PriceFrom    = 59m,
                PriceUnit    = "€/kuu",
                SizeM2       = 25m,
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.6m,
                ReviewCount  = 89,
                Badge        = ListingBadge.Closest,
                Description  = "Professionaalne laohoone Mustamäel. Ideaalne ettevõtetele, kes vajavad regulaarset juurdepääsu kaubale.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²",
                    heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: false, longTerm: true,
                    features: ["Tõstuk", "Laadimisplatvorm", "24/7 juurdepääs", "Kindlustus", "Kliimakontroll"]),
                CreatedAt    = Utc(2025, 10, 1),
                UpdatedAt    = Utc(2025, 10, 1),
            },
            new() {
                Id           = G("w6"),
                Type         = ListingType.Warehouse,
                SupplierId   = G("sup-2"),
                Title        = "Viljandi MiniLadu",
                Address      = "Vaksali 12",
                City         = "Viljandi",
                Lat          = 58.3639,
                Lng          = 25.5900,
                PriceFrom    = 22m,
                PriceUnit    = "€/kuu",
                SizeM2       = 4m,
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.0m,
                ReviewCount  = 15,
                Badge        = null,
                Description  = "Odav ladu Viljandis. Sobib hooajaasjade ja väiksema kauba hoiustamiseks.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(4, "m²",
                    heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Valve", "Paindlik leping"]),
                CreatedAt    = Utc(2025, 11, 10),
                UpdatedAt    = Utc(2025, 11, 10),
            },

            // ── MOVING SERVICES ─────────────────────────────────────────────
            new() {
                Id           = G("m1"),
                Type         = ListingType.Moving,
                SupplierId   = G("sup-4"),
                Title        = "KoliExpress",
                Address      = "Peterburi tee 81",
                City         = "Tallinn",
                Lat          = 59.4369,
                Lng          = 24.7926,
                PriceFrom    = 45m,
                PriceUnit    = "€/h",
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.7m,
                ReviewCount  = 189,
                Badge        = ListingBadge.Promoted,
                Description  = "Kiire ja usaldusväärne kolimisteenus Tallinnas ja üle Eesti. Pakume ka pakkimis- ja laadimisabi.",
                ImagesJson   = Imgs(
                    "https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop",
                    "https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=800&h=600&fit=crop"),
                FeaturesJson = MovingFeatures(
                    serviceArea: ["Tallinn", "Harjumaa", "Kogu Eesti"],
                    withVan: true, packingHelp: true, loadingHelp: true,
                    pricingModel: "hourly",
                    services: ["Kolimine", "Pakkimine", "Laadimine", "Mööbli kokkupanek", "Prügi äravedu"]),
                CreatedAt    = Utc(2025, 10, 5),
                UpdatedAt    = Utc(2025, 10, 5),
            },
            new() {
                Id           = G("m2"),
                Type         = ListingType.Moving,
                SupplierId   = G("sup-2"),
                Title        = "VeoPro Tartu",
                Address      = "Turu 45",
                City         = "Tartu",
                Lat          = 58.3776,
                Lng          = 26.7290,
                PriceFrom    = 35m,
                PriceUnit    = "€/h",
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.4m,
                ReviewCount  = 78,
                Badge        = ListingBadge.Cheapest,
                Description  = "Soodne kolimisteenus Tartus. Kiire ja korralik teenindus.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1558618666-fcd25c85f82e?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(
                    serviceArea: ["Tartu", "Tartumaa"],
                    withVan: true, packingHelp: false, loadingHelp: true,
                    pricingModel: "hourly",
                    services: ["Kolimine", "Laadimine", "Transport"]),
                CreatedAt    = Utc(2025, 9, 1),
                UpdatedAt    = Utc(2025, 9, 1),
            },
            new() {
                Id           = G("m3"),
                Type         = ListingType.Moving,
                SupplierId   = G("sup-6"),
                Title        = "FlexMove Pärnu",
                Address      = "Riia mnt 130",
                City         = "Pärnu",
                Lat          = 58.3714,
                Lng          = 24.5136,
                PriceFrom    = 40m,
                PriceUnit    = "€/h",
                AvailableNow = false,
                IsActive     = true,
                Rating       = 4.3m,
                ReviewCount  = 42,
                Badge        = null,
                Description  = "Professionaalne kolimisteenus Pärnus ja ümbruses. Pakume täisteenust koos pakkimisega.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(
                    serviceArea: ["Pärnu", "Pärnumaa"],
                    withVan: true, packingHelp: true, loadingHelp: true,
                    pricingModel: "hourly",
                    services: ["Kolimine", "Pakkimine", "Laadimine", "Transport"]),
                CreatedAt    = Utc(2025, 11, 1),
                UpdatedAt    = Utc(2025, 11, 1),
            },
            new() {
                Id           = G("m4"),
                Type         = ListingType.Moving,
                SupplierId   = G("sup-4"),
                Title        = "BudgetKoli",
                Address      = "Endla 45",
                City         = "Tallinn",
                Lat          = 59.4308,
                Lng          = 24.7267,
                PriceFrom    = 25m,
                PriceUnit    = "€/h",
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.1m,
                ReviewCount  = 56,
                Badge        = ListingBadge.Cheapest,
                Description  = "Eesti soodsaim kolimisteenus. Fikseeritud hind ilma üllatusteta.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1558618666-fcd25c85f82e?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(
                    serviceArea: ["Tallinn", "Harjumaa"],
                    withVan: true, packingHelp: false, loadingHelp: false,
                    pricingModel: "fixed",
                    services: ["Transport", "Kolimine"]),
                CreatedAt    = Utc(2025, 10, 5),
                UpdatedAt    = Utc(2025, 10, 5),
            },

            // ── TRAILER RENTALS ──────────────────────────────────────────────
            new() {
                Id           = G("t1"),
                Type         = ListingType.Trailer,
                SupplierId   = G("sup-5"),
                Title        = "HaagisRent Tallinn",
                Address      = "Tehnika 14",
                City         = "Tallinn",
                Lat          = 59.4283,
                Lng          = 24.7544,
                PriceFrom    = 25m,
                PriceUnit    = "€/päev",
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.6m,
                ReviewCount  = 95,
                Badge        = ListingBadge.Closest,
                Description  = "Haagiste rent Tallinnas. Lai valik erinevaid haagiseid kinnistest avatud haagisteni.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1601628828688-632f38a5a7d0?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(
                    trailerType: "Kinnine haagis",
                    weightClass: "750 kg",
                    requirements: ["B-kategooria juhiluba", "Krediitkaart", "Isikut tõendav dokument"]),
                CreatedAt    = Utc(2025, 10, 15),
                UpdatedAt    = Utc(2025, 10, 15),
            },
            new() {
                Id           = G("t2"),
                Type         = ListingType.Trailer,
                SupplierId   = G("sup-2"),
                Title        = "Haagis24 Tartu",
                Address      = "Aardla 130",
                City         = "Tartu",
                Lat          = 58.3648,
                Lng          = 26.7056,
                PriceFrom    = 20m,
                PriceUnit    = "€/päev",
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.3m,
                ReviewCount  = 42,
                Badge        = ListingBadge.Cheapest,
                Description  = "Soodsad haagised rendiks Tartus. Saadaval 24/7 iseteenindusega.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1562962230-16e4623d36e6?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(
                    trailerType: "Avatud haagis",
                    weightClass: "500 kg",
                    requirements: ["B-kategooria juhiluba", "Deposiit"]),
                CreatedAt    = Utc(2025, 9, 1),
                UpdatedAt    = Utc(2025, 9, 1),
            },
            new() {
                Id           = G("t3"),
                Type         = ListingType.Trailer,
                SupplierId   = G("sup-6"),
                Title        = "AutoHaagis Pärnu",
                Address      = "Lai 12",
                City         = "Pärnu",
                Lat          = 58.3867,
                Lng          = 24.5030,
                PriceFrom    = 22m,
                PriceUnit    = "€/päev",
                AvailableNow = true,
                IsActive     = true,
                Rating       = 4.5m,
                ReviewCount  = 38,
                Badge        = null,
                Description  = "Kvaliteetsed haagised rendiks Pärnus. Suured kinnised haagised kuni 1000 kg.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1601628828688-632f38a5a7d0?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(
                    trailerType: "Kinnine haagis",
                    weightClass: "1000 kg",
                    requirements: ["B-kategooria juhiluba", "Krediitkaart", "Kindlustus"]),
                CreatedAt    = Utc(2025, 11, 1),
                UpdatedAt    = Utc(2025, 11, 1),
            },
            new() {
                Id           = G("t4"),
                Type         = ListingType.Trailer,
                SupplierId   = G("sup-5"),
                Title        = "RentTrailer Narva",
                Address      = "Kangelaste prospekt 30",
                City         = "Narva",
                Lat          = 59.3796,
                Lng          = 28.1790,
                PriceFrom    = 18m,
                PriceUnit    = "€/päev",
                AvailableNow = false,
                IsActive     = true,
                Rating       = 4.0m,
                ReviewCount  = 19,
                Badge        = ListingBadge.Cheapest,
                Description  = "Soodsad haagised Narvas ja Ida-Virumaal.",
                ImagesJson   = Imgs("https://images.unsplash.com/photo-1562962230-16e4623d36e6?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(
                    trailerType: "Avatud haagis",
                    weightClass: "750 kg",
                    requirements: ["B-kategooria juhiluba", "Deposiit 100€"]),
                CreatedAt    = Utc(2025, 10, 15),
                UpdatedAt    = Utc(2025, 10, 15),
            },

            // ─── Estonia expansion (sup-7..sup-32) ───────────────────────────
            new() { Id = G("l-7-1"), Type = ListingType.Warehouse, SupplierId = G("sup-7"),
                Title = "Hoidla Mustamäe", Address = "Mustamäe tee 17", City = "Tallinn",
                Lat = 59.3941, Lng = 24.6770, PriceFrom = 65m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 78,
                Description = "Modern self-storage facility in Mustamäe with 24/7 access and climate control.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Self-service"]),
                CreatedAt = Utc(2025, 11, 20), UpdatedAt = Utc(2025, 11, 20) },
            new() { Id = G("l-7-2"), Type = ListingType.Warehouse, SupplierId = G("sup-7"),
                Title = "Hoidla Lasnamäe", Address = "Punane 56", City = "Tallinn",
                Lat = 59.4376, Lng = 24.8761, PriceFrom = 95m, PriceUnit = "€/kuu", SizeM2 = 12m,
                AvailableNow = true, IsActive = true, Rating = 4.6m, ReviewCount = 94,
                Description = "Premium climate-controlled storage with monitored security and flexible terms.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(12, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: true, longTerm: true,
                    features: ["Climate control", "Loading dock", "Forklift", "Insurance"]),
                CreatedAt = Utc(2025, 11, 22), UpdatedAt = Utc(2025, 11, 22) },
            new() { Id = G("l-7-3"), Type = ListingType.Moving, SupplierId = G("sup-7"),
                Title = "Tallinna Kolimine", Address = "Sõpruse pst 145", City = "Tallinn",
                Lat = 59.4370, Lng = 24.7536, PriceFrom = 35m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 112,
                Description = "Fast and friendly moving services in Tallinn and surrounding areas. Same-day availability.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Tallinn", "Harjumaa"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Packing", "Loading"]),
                CreatedAt = Utc(2025, 12, 1), UpdatedAt = Utc(2025, 12, 1) },
            new() { Id = G("l-8-1"), Type = ListingType.Warehouse, SupplierId = G("sup-8"),
                Title = "Eesti Logistika Kesklinn", Address = "Liivalaia 33", City = "Tallinn",
                Lat = 59.4370, Lng = 24.7536, PriceFrom = 75m, PriceUnit = "€/kuu", SizeM2 = 8m,
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 65,
                Description = "Centrally located storage units, secure premises, individual unit access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Individual access"]),
                CreatedAt = Utc(2025, 12, 10), UpdatedAt = Utc(2025, 12, 10) },
            new() { Id = G("l-8-2"), Type = ListingType.Warehouse, SupplierId = G("sup-8"),
                Title = "Eesti Logistika Tartu Mnt", Address = "Tartu mnt 80", City = "Tallinn",
                Lat = 59.4258, Lng = 24.7777, PriceFrom = 110m, PriceUnit = "€/kuu", SizeM2 = 15m,
                AvailableNow = true, IsActive = true, Rating = 4.8m, ReviewCount = 85,
                Description = "Drive-up access storage units perfect for furniture and seasonal items.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(15, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: true, longTerm: true,
                    features: ["Drive-up access", "Loading dock", "Forklift", "24/7 access"]),
                CreatedAt = Utc(2025, 12, 15), UpdatedAt = Utc(2025, 12, 15) },
            new() { Id = G("l-9-1"), Type = ListingType.Warehouse, SupplierId = G("sup-9"),
                Title = "Lao24 Tartu Kesklinn", Address = "Vabaduse pst 4", City = "Tartu",
                Lat = 58.3776, Lng = 26.7290, PriceFrom = 49m, PriceUnit = "€/kuu", SizeM2 = 4m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 35,
                Description = "Affordable mini-storage in Tartu, ideal for personal belongings and small business inventory.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(4, "m²", heated: false, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["24/7 access", "Indoor", "Security"]),
                CreatedAt = Utc(2025, 9, 28), UpdatedAt = Utc(2025, 9, 28) },
            new() { Id = G("l-19-1"), Type = ListingType.Warehouse, SupplierId = G("sup-19"),
                Title = "Mustamäe Hoidla A-Korpus", Address = "Mustamäe tee 23", City = "Tallinn",
                Lat = 59.3970, Lng = 24.6680, PriceFrom = 55m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 48,
                Description = "Drive-up access storage units in A-block, quick loading and unloading.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Drive-up access", "Climate control", "24/7 access"]),
                CreatedAt = Utc(2025, 12, 20), UpdatedAt = Utc(2025, 12, 20) },
            new() { Id = G("l-19-2"), Type = ListingType.Warehouse, SupplierId = G("sup-19"),
                Title = "Mustamäe Hoidla B-Korpus", Address = "Mustamäe tee 23", City = "Tallinn",
                Lat = 59.3950, Lng = 24.6700, PriceFrom = 85m, PriceUnit = "€/kuu", SizeM2 = 10m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 41,
                Description = "Larger units in B-block with monitored security and individual unit access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(10, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Loading dock", "Climate control", "Individual access"]),
                CreatedAt = Utc(2025, 12, 20), UpdatedAt = Utc(2025, 12, 20) },
            new() { Id = G("l-20-1"), Type = ListingType.Warehouse, SupplierId = G("sup-20"),
                Title = "Lasnamäe Mini Storage", Address = "Punane 70", City = "Tallinn",
                Lat = 59.4380, Lng = 24.8800, PriceFrom = 39m, PriceUnit = "€/kuu", SizeM2 = 3m,
                AvailableNow = true, IsActive = true, Rating = 4.3m, ReviewCount = 52,
                Description = "Affordable mini-storage units for personal belongings and seasonal items.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(3, "m²", heated: false, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["24/7 access", "Indoor", "Security"]),
                CreatedAt = Utc(2026, 1, 12), UpdatedAt = Utc(2026, 1, 12) },
            new() { Id = G("l-20-2"), Type = ListingType.Warehouse, SupplierId = G("sup-20"),
                Title = "Lasnamäe XL Storage", Address = "Punane 72", City = "Tallinn",
                Lat = 59.4400, Lng = 24.8750, PriceFrom = 130m, PriceUnit = "€/kuu", SizeM2 = 18m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 36,
                Description = "Large XL storage with loading dock and forklift access for businesses.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(18, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: false, longTerm: true,
                    features: ["XL", "Loading dock", "Forklift", "Climate control", "Insurance"]),
                CreatedAt = Utc(2026, 1, 15), UpdatedAt = Utc(2026, 1, 15) },
            new() { Id = G("l-21-1"), Type = ListingType.Warehouse, SupplierId = G("sup-21"),
                Title = "Põhja Mini-Lao", Address = "Pirita tee 28", City = "Tallinn",
                Lat = 59.4500, Lng = 24.7600, PriceFrom = 42m, PriceUnit = "€/kuu", SizeM2 = 4m,
                AvailableNow = true, IsActive = true, Rating = 4.1m, ReviewCount = 18,
                Description = "Mini-storage in Põhja-Tallinn, perfect for seasonal items and household overflow.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(4, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security", "Easy access"]),
                CreatedAt = Utc(2026, 2, 10), UpdatedAt = Utc(2026, 2, 10) },
            new() { Id = G("l-22-1"), Type = ListingType.Warehouse, SupplierId = G("sup-22"),
                Title = "KesklinnBox", Address = "Roosikrantsi 8", City = "Tallinn",
                Lat = 59.4350, Lng = 24.7480, PriceFrom = 60m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 47,
                Description = "Centrally located storage units, secure premises, individual unit access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["City center", "24/7 access", "Climate control"]),
                CreatedAt = Utc(2026, 2, 1), UpdatedAt = Utc(2026, 2, 1) },
            new() { Id = G("l-23-1"), Type = ListingType.Warehouse, SupplierId = G("sup-23"),
                Title = "Kristiine Hoidla", Address = "Tulika 33", City = "Tallinn",
                Lat = 59.4250, Lng = 24.7100, PriceFrom = 58m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 39,
                Description = "Modern self-storage facility in Kristiine with 24/7 access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Modern"]),
                CreatedAt = Utc(2025, 10, 12), UpdatedAt = Utc(2025, 10, 12) },
            new() { Id = G("l-24-1"), Type = ListingType.Warehouse, SupplierId = G("sup-24"),
                Title = "Viimsi Mini Storage", Address = "Randvere tee 9", City = "Tallinn",
                Lat = 59.5050, Lng = 24.8470, PriceFrom = 40m, PriceUnit = "€/kuu", SizeM2 = 4m,
                AvailableNow = true, IsActive = true, Rating = 4.0m, ReviewCount = 14,
                Description = "Affordable storage in Viimsi peninsula, easy access from city center.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(4, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security", "Free parking"]),
                CreatedAt = Utc(2026, 3, 20), UpdatedAt = Utc(2026, 3, 20) },
            new() { Id = G("l-25-1"), Type = ListingType.Moving, SupplierId = G("sup-25"),
                Title = "Kolimine Pluss", Address = "Toompuiestee 14", City = "Tallinn",
                Lat = 59.4300, Lng = 24.7400, PriceFrom = 40m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.8m, ReviewCount = 156,
                Description = "Fast and friendly moving services across Tallinn and Harjumaa. Premium fleet.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Tallinn", "Harjumaa", "Kogu Eesti"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Packing", "Loading", "Furniture assembly"]),
                CreatedAt = Utc(2025, 12, 3), UpdatedAt = Utc(2025, 12, 3) },
            new() { Id = G("l-25-2"), Type = ListingType.Trailer, SupplierId = G("sup-25"),
                Title = "Kolimine Pluss Trailer", Address = "Toompuiestee 16", City = "Tallinn",
                Lat = 59.4310, Lng = 24.7410, PriceFrom = 25m, PriceUnit = "€/päev",
                AvailableNow = true, IsActive = true, Rating = 4.6m, ReviewCount = 72,
                Description = "Trailer rental for short-term use. Daily and weekly rates.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1601628828688-632f38a5a7d0?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(trailerType: "Open trailer", weightClass: "750 kg",
                    requirements: ["Cat B license", "Deposit 100€"]),
                CreatedAt = Utc(2025, 12, 5), UpdatedAt = Utc(2025, 12, 5) },
            new() { Id = G("l-26-1"), Type = ListingType.Warehouse, SupplierId = G("sup-26"),
                Title = "Tartu Hoiuruum", Address = "Riia 12", City = "Tartu",
                Lat = 58.3800, Lng = 26.7350, PriceFrom = 52m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 64,
                Description = "Tartu's trusted storage provider since 2015, serving residents and businesses.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Trusted local"]),
                CreatedAt = Utc(2025, 10, 25), UpdatedAt = Utc(2025, 10, 25) },
            new() { Id = G("l-27-1"), Type = ListingType.Warehouse, SupplierId = G("sup-27"),
                Title = "Annelinna Lao", Address = "Annelinna 16", City = "Tartu",
                Lat = 58.3680, Lng = 26.7600, PriceFrom = 40m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.2m, ReviewCount = 22,
                Description = "Affordable mini-storage in Annelinn, close to public transit.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 2, 25), UpdatedAt = Utc(2026, 2, 25) },
            new() { Id = G("l-28-1"), Type = ListingType.Warehouse, SupplierId = G("sup-28"),
                Title = "Riia Mini-Lao", Address = "Veeriku 22", City = "Tartu",
                Lat = 58.3700, Lng = 26.7100, PriceFrom = 38m, PriceUnit = "€/kuu", SizeM2 = 4m,
                AvailableNow = true, IsActive = true, Rating = 4.1m, ReviewCount = 17,
                Description = "Mini-storage on Riia tee, ideal for student and small business needs.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(4, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Affordable", "Security"]),
                CreatedAt = Utc(2026, 3, 10), UpdatedAt = Utc(2026, 3, 10) },
            new() { Id = G("l-29-1"), Type = ListingType.Warehouse, SupplierId = G("sup-29"),
                Title = "Pärnu Beach Storage", Address = "Rüütli 18", City = "Pärnu",
                Lat = 58.3859, Lng = 24.4971, PriceFrom = 55m, PriceUnit = "€/kuu", SizeM2 = 7m,
                AvailableNow = true, IsActive = true, Rating = 4.6m, ReviewCount = 38,
                Description = "Climate-controlled storage near the beach, perfect for seasonal items.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(7, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Coastal"]),
                CreatedAt = Utc(2025, 12, 25), UpdatedAt = Utc(2025, 12, 25) },
            new() { Id = G("l-29-2"), Type = ListingType.Trailer, SupplierId = G("sup-29"),
                Title = "Pärnu Trailer Rent", Address = "Rüütli 20", City = "Pärnu",
                Lat = 58.3870, Lng = 24.5000, PriceFrom = 20m, PriceUnit = "€/päev",
                AvailableNow = true, IsActive = true, Rating = 4.3m, ReviewCount = 26,
                Description = "Trailer rental for short-term use, daily and weekly rates.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1601628828688-632f38a5a7d0?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(trailerType: "Open trailer", weightClass: "750 kg",
                    requirements: ["Cat B license", "Deposit 80€"]),
                CreatedAt = Utc(2025, 12, 28), UpdatedAt = Utc(2025, 12, 28) },
            new() { Id = G("l-30-1"), Type = ListingType.Warehouse, SupplierId = G("sup-30"),
                Title = "Mai Hoidla", Address = "Mai 25", City = "Pärnu",
                Lat = 58.3900, Lng = 24.5100, PriceFrom = 45m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.0m, ReviewCount = 11,
                Description = "Affordable mini-storage in Pärnu Mai district.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 2, 5), UpdatedAt = Utc(2026, 2, 5) },
            new() { Id = G("l-31-1"), Type = ListingType.Warehouse, SupplierId = G("sup-31"),
                Title = "Narva Lao Keskus", Address = "Tallinna mnt 19", City = "Narva",
                Lat = 59.3770, Lng = 28.1900, PriceFrom = 50m, PriceUnit = "€/kuu", SizeM2 = 8m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 33,
                Description = "Trusted storage provider in Narva and Ida-Virumaa.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Loading dock"]),
                CreatedAt = Utc(2025, 10, 5), UpdatedAt = Utc(2025, 10, 5) },
            new() { Id = G("l-31-2"), Type = ListingType.Moving, SupplierId = G("sup-31"),
                Title = "Narva Kolimine", Address = "Tallinna mnt 19", City = "Narva",
                Lat = 59.3780, Lng = 28.1950, PriceFrom = 30m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.3m, ReviewCount = 28,
                Description = "Moving services in Narva and Ida-Virumaa.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Narva", "Ida-Virumaa"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Loading"]),
                CreatedAt = Utc(2025, 10, 8), UpdatedAt = Utc(2025, 10, 8) },
            new() { Id = G("l-32-1"), Type = ListingType.Warehouse, SupplierId = G("sup-32"),
                Title = "Sillamäe Storage", Address = "Puškini 11", City = "Narva",
                Lat = 59.4000, Lng = 27.7600, PriceFrom = 42m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.1m, ReviewCount = 16,
                Description = "Mini-storage in Sillamäe, fast and easy access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 4, 10), UpdatedAt = Utc(2026, 4, 10) },

            // ─── Latvia (sup-33..sup-48) ─────────────────────────────────────
            new() { Id = G("l-33-1"), Type = ListingType.Warehouse, SupplierId = G("sup-33"),
                Title = "Rīgas Noliktavas Centra", Address = "Brīvības iela 100", City = "Rīga",
                Lat = 56.9496, Lng = 24.1052, PriceFrom = 75m, PriceUnit = "€/kuu", SizeM2 = 8m,
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 88,
                Description = "Centrally located storage in Rīga with 24/7 access and climate control.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "City center"]),
                CreatedAt = Utc(2025, 12, 12), UpdatedAt = Utc(2025, 12, 12) },
            new() { Id = G("l-33-2"), Type = ListingType.Warehouse, SupplierId = G("sup-33"),
                Title = "Rīgas Noliktavas Mežaparks", Address = "Mežaparka 12", City = "Rīga",
                Lat = 56.9939, Lng = 24.1278, PriceFrom = 115m, PriceUnit = "€/kuu", SizeM2 = 15m,
                AvailableNow = true, IsActive = true, Rating = 4.8m, ReviewCount = 102,
                Description = "Premium climate-controlled storage with monitored security.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(15, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: true, longTerm: true,
                    features: ["Climate control", "Loading dock", "Forklift", "Insurance"]),
                CreatedAt = Utc(2025, 12, 15), UpdatedAt = Utc(2025, 12, 15) },
            new() { Id = G("l-33-3"), Type = ListingType.Moving, SupplierId = G("sup-33"),
                Title = "Rīgas Pārvešana", Address = "Brīvības iela 102", City = "Rīga",
                Lat = 56.9500, Lng = 24.1100, PriceFrom = 45m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 124,
                Description = "Moving services across Rīga and surrounding areas, premium fleet.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Rīga", "Pierīga"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Packing", "Loading", "Furniture assembly"]),
                CreatedAt = Utc(2025, 12, 20), UpdatedAt = Utc(2025, 12, 20) },
            new() { Id = G("l-34-1"), Type = ListingType.Warehouse, SupplierId = G("sup-34"),
                Title = "BalticBox Center", Address = "Tērbatas iela 50", City = "Rīga",
                Lat = 56.9528, Lng = 24.1106, PriceFrom = 60m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.6m, ReviewCount = 71,
                Description = "Modern self-storage facility in central Rīga.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Modern"]),
                CreatedAt = Utc(2026, 1, 20), UpdatedAt = Utc(2026, 1, 20) },
            new() { Id = G("l-34-2"), Type = ListingType.Warehouse, SupplierId = G("sup-34"),
                Title = "BalticBox XL", Address = "Krišjāņa Barona iela 70", City = "Rīga",
                Lat = 56.9610, Lng = 24.1200, PriceFrom = 130m, PriceUnit = "€/kuu", SizeM2 = 18m,
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 56,
                Description = "XL storage with loading dock and forklift access for businesses.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(18, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: false, longTerm: true,
                    features: ["XL", "Loading dock", "Forklift", "Insurance"]),
                CreatedAt = Utc(2026, 1, 22), UpdatedAt = Utc(2026, 1, 22) },
            new() { Id = G("l-34-3"), Type = ListingType.Trailer, SupplierId = G("sup-34"),
                Title = "BalticBox Treiler", Address = "Tērbatas iela 52", City = "Rīga",
                Lat = 56.9550, Lng = 24.1150, PriceFrom = 25m, PriceUnit = "€/päev",
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 41,
                Description = "Trailer rental for short-term use, daily and weekly rates.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1601628828688-632f38a5a7d0?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(trailerType: "Enclosed trailer", weightClass: "1000 kg",
                    requirements: ["Cat B license", "Deposit 100€"]),
                CreatedAt = Utc(2026, 1, 25), UpdatedAt = Utc(2026, 1, 25) },
            new() { Id = G("l-35-1"), Type = ListingType.Warehouse, SupplierId = G("sup-35"),
                Title = "StoragePro Pārdaugava", Address = "Pārdaugavas iela 8", City = "Rīga",
                Lat = 56.9344, Lng = 24.0708, PriceFrom = 85m, PriceUnit = "€/kuu", SizeM2 = 10m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 49,
                Description = "Drive-up access storage in Pārdaugava, perfect for furniture.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(10, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Drive-up access", "Loading dock", "Climate control"]),
                CreatedAt = Utc(2026, 2, 18), UpdatedAt = Utc(2026, 2, 18) },
            new() { Id = G("l-36-1"), Type = ListingType.Warehouse, SupplierId = G("sup-36"),
                Title = "Centra Noliktava", Address = "Stabu iela 47", City = "Rīga",
                Lat = 56.9460, Lng = 24.1080, PriceFrom = 65m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 38,
                Description = "Centrally located storage units, secure premises.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "City center"]),
                CreatedAt = Utc(2025, 10, 28), UpdatedAt = Utc(2025, 10, 28) },
            new() { Id = G("l-37-1"), Type = ListingType.Warehouse, SupplierId = G("sup-37"),
                Title = "Pārdaugavas Glabātavas", Address = "Pārdaugavas iela 14", City = "Rīga",
                Lat = 56.9300, Lng = 24.0650, PriceFrom = 70m, PriceUnit = "€/kuu", SizeM2 = 8m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 32,
                Description = "Affordable storage in Pārdaugava, individual unit access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²", heated: false, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["24/7 access", "Indoor", "Individual access"]),
                CreatedAt = Utc(2025, 11, 22), UpdatedAt = Utc(2025, 11, 22) },
            new() { Id = G("l-38-1"), Type = ListingType.Warehouse, SupplierId = G("sup-38"),
                Title = "Mežaparka Storage", Address = "Mežaparka 18", City = "Rīga",
                Lat = 56.9950, Lng = 24.1300, PriceFrom = 55m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.2m, ReviewCount = 21,
                Description = "Mini-storage in Mežaparks neighborhood.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 3, 1), UpdatedAt = Utc(2026, 3, 1) },
            new() { Id = G("l-39-1"), Type = ListingType.Warehouse, SupplierId = G("sup-39"),
                Title = "Imanta Lao", Address = "Imantas iela 6", City = "Rīga",
                Lat = 56.9430, Lng = 23.9890, PriceFrom = 50m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.1m, ReviewCount = 18,
                Description = "Affordable storage in Imanta district.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 3, 15), UpdatedAt = Utc(2026, 3, 15) },
            new() { Id = G("l-40-1"), Type = ListingType.Warehouse, SupplierId = G("sup-40"),
                Title = "Purvciema Noliktava", Address = "Purvciema iela 12", City = "Rīga",
                Lat = 56.9520, Lng = 24.2150, PriceFrom = 60m, PriceUnit = "€/kuu", SizeM2 = 7m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 27,
                Description = "Storage in Purvciems with 24/7 access and security.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(7, "m²", heated: false, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["24/7 access", "Security", "Indoor"]),
                CreatedAt = Utc(2026, 1, 3), UpdatedAt = Utc(2026, 1, 3) },
            new() { Id = G("l-41-1"), Type = ListingType.Moving, SupplierId = G("sup-41"),
                Title = "Pārvešana Rīga", Address = "Rīgas iela 30", City = "Rīga",
                Lat = 56.9510, Lng = 24.1070, PriceFrom = 40m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 56,
                Description = "Reliable moving services in Rīga and surrounding areas.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Rīga", "Pierīga"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Loading", "Packing"]),
                CreatedAt = Utc(2026, 1, 28), UpdatedAt = Utc(2026, 1, 28) },
            new() { Id = G("l-42-1"), Type = ListingType.Warehouse, SupplierId = G("sup-42"),
                Title = "Daugavpils Storage", Address = "Cietokšņa iela 25", City = "Daugavpils",
                Lat = 55.8740, Lng = 26.5360, PriceFrom = 45m, PriceUnit = "€/kuu", SizeM2 = 7m,
                AvailableNow = true, IsActive = true, Rating = 4.3m, ReviewCount = 24,
                Description = "Trusted storage provider in Daugavpils.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(7, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access"]),
                CreatedAt = Utc(2025, 11, 3), UpdatedAt = Utc(2025, 11, 3) },
            new() { Id = G("l-43-1"), Type = ListingType.Warehouse, SupplierId = G("sup-43"),
                Title = "DGV Glabātavas", Address = "Smilšu iela 11", City = "Daugavpils",
                Lat = 55.8780, Lng = 26.5420, PriceFrom = 38m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.1m, ReviewCount = 14,
                Description = "Mini-storage in Daugavpils, affordable rates.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 3, 22), UpdatedAt = Utc(2026, 3, 22) },
            new() { Id = G("l-44-1"), Type = ListingType.Warehouse, SupplierId = G("sup-44"),
                Title = "Cietoksnis Lao", Address = "Cietokšņa iela 27", City = "Daugavpils",
                Lat = 55.8810, Lng = 26.5450, PriceFrom = 40m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.0m, ReviewCount = 12,
                Description = "Storage near Daugavpils Fortress, easy access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 4, 5), UpdatedAt = Utc(2026, 4, 5) },
            new() { Id = G("l-45-1"), Type = ListingType.Warehouse, SupplierId = G("sup-45"),
                Title = "Liepājas Hoidla", Address = "Lielā iela 11", City = "Liepāja",
                Lat = 56.5118, Lng = 21.0136, PriceFrom = 48m, PriceUnit = "€/kuu", SizeM2 = 7m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 31,
                Description = "Storage in Liepāja with monitored security and individual access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(7, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Individual access"]),
                CreatedAt = Utc(2025, 11, 12), UpdatedAt = Utc(2025, 11, 12) },
            new() { Id = G("l-46-1"), Type = ListingType.Warehouse, SupplierId = G("sup-46"),
                Title = "Karostas Storage", Address = "Karostas iela 31", City = "Liepāja",
                Lat = 56.5500, Lng = 21.0200, PriceFrom = 40m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.0m, ReviewCount = 9,
                Description = "Affordable storage in Karosta neighborhood.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 4, 12), UpdatedAt = Utc(2026, 4, 12) },
            new() { Id = G("l-47-1"), Type = ListingType.Warehouse, SupplierId = G("sup-47"),
                Title = "Jelgavas Noliktava", Address = "Lielā iela 22", City = "Jelgava",
                Lat = 56.6510, Lng = 23.7280, PriceFrom = 45m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 25,
                Description = "Centrally located storage in Jelgava.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access"]),
                CreatedAt = Utc(2025, 12, 18), UpdatedAt = Utc(2025, 12, 18) },
            new() { Id = G("l-48-1"), Type = ListingType.Warehouse, SupplierId = G("sup-48"),
                Title = "Jūrmalas Glabātavas", Address = "Jomas iela 56", City = "Jūrmala",
                Lat = 56.9680, Lng = 23.7700, PriceFrom = 60m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.3m, ReviewCount = 19,
                Description = "Storage in Jūrmala, near the beach resort area.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "Coastal", "24/7 access"]),
                CreatedAt = Utc(2026, 3, 28), UpdatedAt = Utc(2026, 3, 28) },

            // ─── Lithuania (sup-49..sup-60) ──────────────────────────────────
            new() { Id = G("l-49-1"), Type = ListingType.Warehouse, SupplierId = G("sup-49"),
                Title = "Vilniaus Sandėliai Centras", Address = "Gedimino prospektas 30", City = "Vilnius",
                Lat = 54.6872, Lng = 25.2797, PriceFrom = 80m, PriceUnit = "€/kuu", SizeM2 = 9m,
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 96,
                Description = "Centrally located storage in Vilnius with 24/7 access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(9, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "City center"]),
                CreatedAt = Utc(2025, 12, 28), UpdatedAt = Utc(2025, 12, 28) },
            new() { Id = G("l-49-2"), Type = ListingType.Warehouse, SupplierId = G("sup-49"),
                Title = "Vilniaus Sandėliai Antakalnis", Address = "Antakalnio gatvė 80", City = "Vilnius",
                Lat = 54.7197, Lng = 25.3008, PriceFrom = 110m, PriceUnit = "€/kuu", SizeM2 = 14m,
                AvailableNow = true, IsActive = true, Rating = 4.8m, ReviewCount = 81,
                Description = "Premium storage with climate control in Antakalnis.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(14, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: true, longTerm: true,
                    features: ["Climate control", "Loading dock", "Forklift", "Insurance"]),
                CreatedAt = Utc(2026, 1, 2), UpdatedAt = Utc(2026, 1, 2) },
            new() { Id = G("l-49-3"), Type = ListingType.Moving, SupplierId = G("sup-49"),
                Title = "Vilniaus Pervežimas", Address = "Gedimino prospektas 32", City = "Vilnius",
                Lat = 54.6890, Lng = 25.2820, PriceFrom = 45m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 134,
                Description = "Reliable moving services across Vilnius and surrounding areas.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Vilnius", "Vilniaus rajonas"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Packing", "Loading", "Furniture assembly"]),
                CreatedAt = Utc(2026, 1, 5), UpdatedAt = Utc(2026, 1, 5) },
            new() { Id = G("l-50-1"), Type = ListingType.Warehouse, SupplierId = G("sup-50"),
                Title = "LietuvosBox Vilnius", Address = "Vilniaus gatvė 22", City = "Vilnius",
                Lat = 54.6892, Lng = 25.2829, PriceFrom = 60m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.6m, ReviewCount = 73,
                Description = "Modern self-storage in central Vilnius.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Modern"]),
                CreatedAt = Utc(2026, 1, 25), UpdatedAt = Utc(2026, 1, 25) },
            new() { Id = G("l-50-2"), Type = ListingType.Warehouse, SupplierId = G("sup-50"),
                Title = "LietuvosBox XL", Address = "Kalvarijų gatvė 35", City = "Vilnius",
                Lat = 54.6950, Lng = 25.3050, PriceFrom = 135m, PriceUnit = "€/kuu", SizeM2 = 18m,
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 58,
                Description = "Large XL storage with forklift access for businesses.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(18, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: false, longTerm: true,
                    features: ["XL", "Loading dock", "Forklift", "Climate control"]),
                CreatedAt = Utc(2026, 1, 28), UpdatedAt = Utc(2026, 1, 28) },
            new() { Id = G("l-50-3"), Type = ListingType.Trailer, SupplierId = G("sup-50"),
                Title = "LietuvosBox Treileris", Address = "Vilniaus gatvė 24", City = "Vilnius",
                Lat = 54.6900, Lng = 25.2850, PriceFrom = 25m, PriceUnit = "€/päev",
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 38,
                Description = "Trailer rental for short-term use, daily and weekly rates.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1601628828688-632f38a5a7d0?w=600&h=400&fit=crop"),
                FeaturesJson = TrailerFeatures(trailerType: "Open trailer", weightClass: "750 kg",
                    requirements: ["Cat B license", "Deposit 100€"]),
                CreatedAt = Utc(2026, 2, 2), UpdatedAt = Utc(2026, 2, 2) },
            new() { Id = G("l-51-1"), Type = ListingType.Warehouse, SupplierId = G("sup-51"),
                Title = "Saugykla LT Centras", Address = "Naugarduko gatvė 12", City = "Vilnius",
                Lat = 54.6830, Lng = 25.2750, PriceFrom = 65m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 42,
                Description = "Centrally located storage units, secure premises.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "City center"]),
                CreatedAt = Utc(2026, 2, 15), UpdatedAt = Utc(2026, 2, 15) },
            new() { Id = G("l-52-1"), Type = ListingType.Warehouse, SupplierId = G("sup-52"),
                Title = "Antakalnio Sandėliai", Address = "Antakalnio gatvė 88", City = "Vilnius",
                Lat = 54.7180, Lng = 25.2980, PriceFrom = 70m, PriceUnit = "€/kuu", SizeM2 = 7m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 36,
                Description = "Storage in Antakalnis with 24/7 access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(7, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access"]),
                CreatedAt = Utc(2025, 10, 18), UpdatedAt = Utc(2025, 10, 18) },
            new() { Id = G("l-53-1"), Type = ListingType.Warehouse, SupplierId = G("sup-53"),
                Title = "Naujamiesčio Saugykla", Address = "Naugarduko gatvė 18", City = "Vilnius",
                Lat = 54.6790, Lng = 25.2620, PriceFrom = 75m, PriceUnit = "€/kuu", SizeM2 = 8m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 31,
                Description = "Modern storage in Naujamiestis district.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Loading dock"]),
                CreatedAt = Utc(2025, 11, 28), UpdatedAt = Utc(2025, 11, 28) },
            new() { Id = G("l-54-1"), Type = ListingType.Warehouse, SupplierId = G("sup-54"),
                Title = "Šnipiškių Lao", Address = "Kalvarijų gatvė 41", City = "Vilnius",
                Lat = 54.7050, Lng = 25.2700, PriceFrom = 45m, PriceUnit = "€/kuu", SizeM2 = 4m,
                AvailableNow = true, IsActive = true, Rating = 4.1m, ReviewCount = 14,
                Description = "Affordable mini-storage in Šnipiškės.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(4, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 3, 20), UpdatedAt = Utc(2026, 3, 20) },
            new() { Id = G("l-55-1"), Type = ListingType.Warehouse, SupplierId = G("sup-55"),
                Title = "Žirmūnų Storage", Address = "Žirmūnų gatvė 50", City = "Vilnius",
                Lat = 54.7250, Lng = 25.2900, PriceFrom = 50m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.2m, ReviewCount = 17,
                Description = "Storage in Žirmūnai neighborhood.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 3, 5), UpdatedAt = Utc(2026, 3, 5) },
            new() { Id = G("l-56-1"), Type = ListingType.Warehouse, SupplierId = G("sup-56"),
                Title = "Kauno Sandėliavimas Centras", Address = "Laisvės alėja 50", City = "Kaunas",
                Lat = 54.8985, Lng = 23.9036, PriceFrom = 65m, PriceUnit = "€/kuu", SizeM2 = 8m,
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 67,
                Description = "Centrally located storage in Kaunas with 24/7 access.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "City center"]),
                CreatedAt = Utc(2025, 12, 8), UpdatedAt = Utc(2025, 12, 8) },
            new() { Id = G("l-56-2"), Type = ListingType.Warehouse, SupplierId = G("sup-56"),
                Title = "Kauno Sandėliavimas Žaliakalnis", Address = "Vytauto prospektas 70", City = "Kaunas",
                Lat = 54.9100, Lng = 23.9300, PriceFrom = 90m, PriceUnit = "€/kuu", SizeM2 = 12m,
                AvailableNow = true, IsActive = true, Rating = 4.6m, ReviewCount = 54,
                Description = "Premium storage in Žaliakalnis with loading dock and climate control.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(12, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: true, shortTerm: true, longTerm: true,
                    features: ["Climate control", "Loading dock", "Forklift"]),
                CreatedAt = Utc(2025, 12, 12), UpdatedAt = Utc(2025, 12, 12) },
            new() { Id = G("l-56-3"), Type = ListingType.Moving, SupplierId = G("sup-56"),
                Title = "Kauno Pervežimas", Address = "Laisvės alėja 52", City = "Kaunas",
                Lat = 54.9000, Lng = 23.9100, PriceFrom = 35m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.7m, ReviewCount = 89,
                Description = "Moving services in Kaunas and surrounding areas.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Kaunas", "Kauno rajonas"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Packing", "Loading"]),
                CreatedAt = Utc(2025, 12, 15), UpdatedAt = Utc(2025, 12, 15) },
            new() { Id = G("l-57-1"), Type = ListingType.Warehouse, SupplierId = G("sup-57"),
                Title = "Centro Saugykla Kaunas", Address = "Laisvės alėja 38", City = "Kaunas",
                Lat = 54.8970, Lng = 23.9050, PriceFrom = 55m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 32,
                Description = "Storage in central Kaunas with secure premises.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access"]),
                CreatedAt = Utc(2026, 1, 10), UpdatedAt = Utc(2026, 1, 10) },
            new() { Id = G("l-58-1"), Type = ListingType.Warehouse, SupplierId = G("sup-58"),
                Title = "Aleksoto Lao", Address = "Aleksoto gatvė 18", City = "Kaunas",
                Lat = 54.8780, Lng = 23.8900, PriceFrom = 45m, PriceUnit = "€/kuu", SizeM2 = 5m,
                AvailableNow = true, IsActive = true, Rating = 4.1m, ReviewCount = 13,
                Description = "Affordable mini-storage in Aleksotas.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1553413077-190dd305871c?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(5, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 4, 15), UpdatedAt = Utc(2026, 4, 15) },
            new() { Id = G("l-59-1"), Type = ListingType.Warehouse, SupplierId = G("sup-59"),
                Title = "Klaipėdos Sandėliai", Address = "Tiltų gatvė 12", City = "Klaipėda",
                Lat = 55.7033, Lng = 21.1443, PriceFrom = 60m, PriceUnit = "€/kuu", SizeM2 = 8m,
                AvailableNow = true, IsActive = true, Rating = 4.5m, ReviewCount = 35,
                Description = "Storage in Klaipėda with monitored security.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1565610222536-ef125c59da2e?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(8, "m²", heated: true, indoor: true, access24_7: true, security: true,
                    loadingDock: true, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Climate control", "24/7 access", "Loading dock"]),
                CreatedAt = Utc(2025, 9, 22), UpdatedAt = Utc(2025, 9, 22) },
            new() { Id = G("l-59-2"), Type = ListingType.Moving, SupplierId = G("sup-59"),
                Title = "Klaipėdos Pervežimas", Address = "Tiltų gatvė 14", City = "Klaipėda",
                Lat = 55.7050, Lng = 21.1450, PriceFrom = 35m, PriceUnit = "€/h",
                AvailableNow = true, IsActive = true, Rating = 4.4m, ReviewCount = 42,
                Description = "Moving services in Klaipėda and Western Lithuania.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600518464441-9154a4dea21b?w=600&h=400&fit=crop"),
                FeaturesJson = MovingFeatures(serviceArea: ["Klaipėda", "Klaipėdos rajonas"],
                    withVan: true, packingHelp: true, loadingHelp: true, pricingModel: "hourly",
                    services: ["Moving", "Loading"]),
                CreatedAt = Utc(2025, 9, 25), UpdatedAt = Utc(2025, 9, 25) },
            new() { Id = G("l-60-1"), Type = ListingType.Warehouse, SupplierId = G("sup-60"),
                Title = "Smiltynės Storage", Address = "Smiltynės gatvė 8", City = "Klaipėda",
                Lat = 55.7100, Lng = 21.1200, PriceFrom = 50m, PriceUnit = "€/kuu", SizeM2 = 6m,
                AvailableNow = true, IsActive = true, Rating = 4.0m, ReviewCount = 11,
                Description = "Storage in Smiltynė neighborhood.",
                ImagesJson = Imgs("https://images.unsplash.com/photo-1600585152220-90363fe7e115?w=600&h=400&fit=crop"),
                FeaturesJson = WarehouseFeatures(6, "m²", heated: false, indoor: true, access24_7: false, security: true,
                    loadingDock: false, forklift: false, shortTerm: true, longTerm: true,
                    features: ["Indoor", "Security"]),
                CreatedAt = Utc(2026, 4, 3), UpdatedAt = Utc(2026, 4, 3) },
        });

        await db.SaveChangesAsync();

        // Ensure every seeded listing has a SupplierLocation. Idempotent — runs
        // every startup, no-op after first time (loop body excludes already-linked
        // listings). For each (SupplierId, City, Type) group of standalone listings,
        // creates one synthetic SupplierLocation and links them.
        // Per-type grouping prevents mixed-type Locations (e.g. Moving + Warehouse
        // sharing one Location), which would conflate UI rendering and discount math.
        await db.Database.ExecuteSqlRawAsync(@"
            DO $$
            DECLARE
                grp        RECORD;
                new_loc_id UUID;
            BEGIN
                FOR grp IN
                    SELECT
                        l.""SupplierId""                                 AS supplier_id,
                        l.""City""                                       AS city,
                        l.""Type""                                       AS listing_type,
                        MIN(l.""Address"")                               AS address,
                        AVG(l.""Lat"")                                   AS lat,
                        AVG(l.""Lng"")                                   AS lng,
                        COALESCE(s.""Country"", 'EE')                    AS country
                    FROM ""Listings"" l
                    JOIN ""Suppliers"" s ON s.""Id"" = l.""SupplierId""
                    WHERE l.""LocationId"" IS NULL AND l.""IsActive"" = true
                    GROUP BY l.""SupplierId"", l.""City"", l.""Type"", s.""Country""
                LOOP
                    new_loc_id := gen_random_uuid();

                    INSERT INTO ""SupplierLocations"" (
                        ""Id"", ""SupplierId"", ""Name"", ""Address"", ""City"", ""Country"",
                        ""Lat"", ""Lng"", ""IsActive"", ""Images"", ""ImagesJson"", ""Description"",
                        ""IsSynthetic"", ""CreatedAt"", ""UpdatedAt""
                    ) VALUES (
                        new_loc_id,
                        grp.supplier_id,
                        COALESCE(NULLIF(grp.city, ''), 'Location'),
                        grp.address,
                        grp.city,
                        grp.country,
                        grp.lat,
                        grp.lng,
                        true, '{}'::text[], '[]', '',
                        true,
                        NOW() AT TIME ZONE 'UTC',
                        NOW() AT TIME ZONE 'UTC'
                    );

                    UPDATE ""Listings""
                    SET ""LocationId"" = new_loc_id
                    WHERE ""LocationId"" IS NULL
                      AND ""IsActive"" = true
                      AND ""SupplierId"" = grp.supplier_id
                      AND ""City"" = grp.city
                      AND ""Type"" = grp.listing_type;
                END LOOP;
            END $$;
        ");

        Console.WriteLine("[Seed] Listings done.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUPPLIER LOCATIONS — one per (SupplierId, Address) tuple from listings.
    // CityPage queries /api/locations?city={city}; without these rows it shows
    // an empty state regardless of how many listings exist.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedLocationsAsync(RuumlyDbContext db)
    {
        if (await db.SupplierLocations.AnyAsync()) return;

        // Load tracked listings once and group in memory: EF's PostgreSQL provider
        // can't translate the FirstListing/ListingIds projections we'd need otherwise.
        var listings = await db.Listings
            .Where(l => l.LocationId == null)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        var suppliersById = await db.Suppliers.ToDictionaryAsync(s => s.Id);

        var newLocations = new List<SupplierLocation>();

        // Only wrap multi-listing sites (2+ units at same address) in a Location.
        // Single-listing sites stay LocationId = null so they remain visible in
        // ListingService.SearchAsync, which filters out listings tied to a Location.
        foreach (var grp in listings
                     .GroupBy(l => new { l.SupplierId, l.Address })
                     .Where(g => g.Count() >= 2))
        {
            var supplier = suppliersById.GetValueOrDefault(grp.Key.SupplierId);
            if (supplier is null) continue;

            var first = grp.First();
            var location = new SupplierLocation
            {
                Id          = G($"loc:{grp.Key.SupplierId}:{grp.Key.Address}"),
                SupplierId  = grp.Key.SupplierId,
                Name        = first.Title,
                Address     = grp.Key.Address,
                City        = first.City,
                Country     = "EE",
                Lat         = first.Lat,
                Lng         = first.Lng,
                IsActive    = true,
                ImagesJson  = first.ImagesJson,
                Description = first.Description,
                CreatedAt   = first.CreatedAt,
                UpdatedAt   = first.UpdatedAt,
            };
            newLocations.Add(location);

            foreach (var listing in grp)
                listing.LocationId = location.Id;
        }

        await db.SupplierLocations.AddRangeAsync(newLocations);
        await db.SaveChangesAsync();

        Console.WriteLine($"[Seed] Locations done. ({newLocations.Count} created)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LISTING EXTRAS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedListingExtrasAsync(RuumlyDbContext db)
    {
        if (await db.ListingExtras.AnyAsync()) return;

        var listing1 = await db.Listings.FirstAsync();
        db.ListingExtras.AddRange(
            // PublicPrice=18, partner=15% → supplierPrice=15.30, customerDiscount=7% → customerPrice=16.74
            new ListingExtra { Id = Guid.NewGuid(), ListingId = listing1.Id,
                Key = "packing", Label = "Pakkimisabi",
                PublicPrice = 18m, PartnerDiscountRate = null,
                SupplierPrice = 15.30m, CustomerPrice = 16.74m,
                CustomerPriceOverride = null, SortOrder = 1 },
            // PublicPrice=24, partner=15% → supplierPrice=20.40, customerPrice=22.32
            new ListingExtra { Id = Guid.NewGuid(), ListingId = listing1.Id,
                Key = "loading", Label = "Laadimisabi",
                PublicPrice = 24m, PartnerDiscountRate = null,
                SupplierPrice = 20.40m, CustomerPrice = 22.32m,
                CustomerPriceOverride = null, SortOrder = 2 },
            // PublicPrice=12, partner=15% → supplierPrice=10.20, customerPrice=11.16
            new ListingExtra { Id = Guid.NewGuid(), ListingId = listing1.Id,
                Key = "insurance", Label = "Kindlustus", Description = "Kuutasu",
                PublicPrice = 12m, PartnerDiscountRate = null,
                SupplierPrice = 10.20m, CustomerPrice = 11.16m,
                CustomerPriceOverride = null, SortOrder = 3 }
        );

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] ListingExtras done.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // USERS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedUsersAsync(RuumlyDbContext db)
    {
        var pwHash = BC.HashPassword("demo1234", workFactor: 12);

        var seedUsers = new List<User>
        {
            new() {
                Id             = G("u1"),
                Name           = "Andres Tamm",
                Email          = "andres@email.com",
                PasswordHash   = pwHash,
                Role           = UserRole.Customer,
                Status         = UserStatus.Active,
                Phone          = "+372 5551 2345",
                RegisteredAt   = Utc(2025, 11, 5),
                LastLoginAt    = Utc(2026, 3, 20),
                BookingsCount  = 3,
            },
            new() {
                Id             = G("u2"),
                Name           = "Kati Mets",
                Email          = "kati@email.com",
                PasswordHash   = pwHash,
                Role           = UserRole.Customer,
                Status         = UserStatus.Active,
                Phone          = "+372 5123 9876",
                RegisteredAt   = Utc(2025, 12, 12),
                LastLoginAt    = Utc(2026, 3, 19),
                BookingsCount  = 1,
            },
            new() {
                Id             = G("u3"),
                Name           = "Jüri Kask",
                Email          = "jyri@email.com",
                PasswordHash   = pwHash,
                Role           = UserRole.Customer,
                Status         = UserStatus.Active,
                Phone          = "+372 5234 5678",
                RegisteredAt   = Utc(2026, 1, 8),
                LastLoginAt    = Utc(2026, 3, 18),
                BookingsCount  = 5,
            },
            new() {
                Id             = G("u4"),
                Name           = "Maria Saar",
                Email          = "maria@laopind.ee",
                PasswordHash   = pwHash,
                Role           = UserRole.Provider,
                Status         = UserStatus.Active,
                Company        = "Laobox OÜ",
                Phone          = "+372 5123 4567",
                SupplierId     = G("sup-1"),
                RegisteredAt   = Utc(2025, 10, 20),
                LastLoginAt    = Utc(2026, 3, 21),
                BookingsCount  = 0,
            },
            new() {
                Id             = G("u5"),
                Name           = "Peeter Kuusk",
                Email          = "peeter@ruumly.eu",
                PasswordHash   = pwHash,
                Role           = UserRole.Admin,
                Status         = UserStatus.Active,
                Phone          = "+372 5555 1234",
                RegisteredAt   = Utc(2025, 9, 1),
                LastLoginAt    = Utc(2026, 3, 21),
                BookingsCount  = 0,
            },
            new() {
                Id             = G("u6"),
                Name           = "Liina Rebane",
                Email          = "liina@email.com",
                PasswordHash   = pwHash,
                Role           = UserRole.Customer,
                Status         = UserStatus.Blocked,
                Phone          = "+372 5345 6789",
                RegisteredAt   = Utc(2026, 2, 14),
                LastLoginAt    = null,
                BookingsCount  = 2,
            },
            new() {
                Id             = G("u7"),
                Name           = "Mart Kivi",
                Email          = "mart@laobox.ee",
                PasswordHash   = pwHash,
                Role           = UserRole.Provider,
                Status         = UserStatus.Active,
                Company        = "Laobox OÜ",
                Phone          = "+372 5123 4567",
                SupplierId     = G("sup-1"),
                RegisteredAt   = Utc(2025, 8, 15),
                LastLoginAt    = Utc(2026, 3, 20),
                BookingsCount  = 0,
            },
            new() {
                Id             = G("u8"),
                Name           = "Tiina Rebane",
                Email          = "tiina@miniladu.ee",
                PasswordHash   = pwHash,
                Role           = UserRole.Provider,
                Status         = UserStatus.Active,
                Company        = "MiniLadu AS",
                Phone          = "+372 5234 5678",
                SupplierId     = G("sup-2"),
                RegisteredAt   = Utc(2025, 9, 1),
                LastLoginAt    = Utc(2026, 3, 19),
                BookingsCount  = 0,
            },
            new() {
                Id             = G("u9"),
                Name           = "Kristjan Mägi",
                Email          = "kristjan@haagisrent.ee",
                PasswordHash   = pwHash,
                Role           = UserRole.Provider,
                Status         = UserStatus.Active,
                Company        = "HaagisRent OÜ",
                Phone          = "+372 5567 8901",
                SupplierId     = G("sup-5"),
                RegisteredAt   = Utc(2025, 10, 15),
                LastLoginAt    = Utc(2026, 3, 15),
                BookingsCount  = 0,
            },
            new() {
                Id             = G("u10"),
                Name           = "Aleksei Ivanov",
                Email          = "aleksei@email.com",
                PasswordHash   = pwHash,
                Role           = UserRole.Customer,
                Status         = UserStatus.Active,
                Phone          = "+372 5678 9012",
                RegisteredAt   = Utc(2026, 3, 1),
                LastLoginAt    = Utc(2026, 3, 21),
                BookingsCount  = 0,
            },
        };

        // Per-email upsert: insert only users not already in the DB. The earlier
        // all-or-nothing AnyAsync guard skipped seeding entirely whenever the
        // admin row survived a partial wipe (which keeps Role = 'Admin').
        var existingEmails = await db.Users.Select(u => u.Email).ToListAsync();
        var existingSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);

        var toInsert = seedUsers.Where(u => !existingSet.Contains(u.Email)).ToList();

        if (toInsert.Count > 0)
        {
            await db.Users.AddRangeAsync(toInsert);
            await db.SaveChangesAsync();
        }

        Console.WriteLine($"[Seed] Users done. ({toInsert.Count} new, {seedUsers.Count - toInsert.Count} already existed)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ORDER ROUTING RULES
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedRoutingRulesAsync(RuumlyDbContext db)
    {
        if (await db.OrderRoutingRules.AnyAsync()) return;

        db.OrderRoutingRules.AddRange(new List<OrderRoutingRule>
        {
            new() {
                Id               = G("rule-1"),
                Name             = "API partnerid — automaatne",
                ServiceType      = ListingType.Warehouse,
                RequiresApproval = false,
                ApproverRole     = "admin",
                PostingChannel   = PostingMode.Api,
                Priority         = 1,
                IsActive         = true,
                CreatedAt        = Utc(2025, 9, 1),
                UpdatedAt        = Utc(2025, 9, 1),
            },
            new() {
                Id               = G("rule-2"),
                Name             = "Ärikliendid — admin kinnitab",
                CustomerType     = "business",
                RequiresApproval = true,
                ApproverRole     = "admin",
                PostingChannel   = PostingMode.Email,
                Priority         = 2,
                IsActive         = true,
                CreatedAt        = Utc(2025, 9, 1),
                UpdatedAt        = Utc(2025, 9, 1),
            },
            new() {
                Id               = G("rule-3"),
                Name             = "Kõrge hinnaga tellimused",
                PriceThreshold   = 500m,
                RequiresApproval = true,
                ApproverRole     = "admin",
                PostingChannel   = PostingMode.Email,
                Priority         = 3,
                IsActive         = true,
                CreatedAt        = Utc(2025, 9, 1),
                UpdatedAt        = Utc(2025, 9, 1),
            },
            new() {
                Id               = G("rule-4"),
                Name             = "Kolimine — partner kinnitab",
                ServiceType      = ListingType.Moving,
                RequiresApproval = true,
                ApproverRole     = "provider",
                PostingChannel   = PostingMode.Email,
                Priority         = 4,
                IsActive         = true,
                CreatedAt        = Utc(2025, 9, 1),
                UpdatedAt        = Utc(2025, 9, 1),
            },
            new() {
                Id               = G("rule-5"),
                Name             = "Haagise rent — manuaalne",
                ServiceType      = ListingType.Trailer,
                RequiresApproval = true,
                ApproverRole     = "admin",
                PostingChannel   = PostingMode.Manual,
                Priority         = 5,
                IsActive         = true,
                CreatedAt        = Utc(2025, 9, 1),
                UpdatedAt        = Utc(2025, 9, 1),
            },
        });

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] RoutingRules done.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BOOKINGS — demo data for admin/customer/provider testing
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedBookingsAsync(RuumlyDbContext db)
    {
        if (await db.Bookings.AnyAsync()) return;

        var customer = await db.Users.FirstOrDefaultAsync(u => u.Email == "andres@email.com");
        var listings = await db.Listings
            .Where(l => l.Type == ListingType.Warehouse)
            .OrderBy(l => l.CreatedAt)
            .Take(3)
            .ToListAsync();

        if (customer is null || listings.Count < 3)
        {
            Console.WriteLine("[Seed] Bookings skipped — required customer or listings missing.");
            return;
        }

        var now = DateTime.UtcNow;

        Booking Make(Listing listing, DateTime start, DateTime? end, BookingStatus status, string duration, DateTime createdAt)
        {
            var basePrice     = listing.PriceFrom;
            var platformPrice = Math.Round(basePrice * 0.95m, 2);
            return new Booking
            {
                Id            = Guid.NewGuid(),
                UserId        = customer.Id,
                ListingId     = listing.Id,
                SupplierId    = listing.SupplierId,
                StartDate     = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc),
                EndDate       = end.HasValue ? DateTime.SpecifyKind(end.Value.Date, DateTimeKind.Utc) : null,
                Duration      = duration,
                Status        = status,
                ContactName   = customer.Name,
                ContactEmail  = customer.Email,
                ContactPhone  = customer.Phone ?? "",
                BasePrice     = basePrice,
                PlatformPrice = platformPrice,
                ExtrasTotal   = 0m,
                VatAmount     = 0m,
                Total         = platformPrice,
                CreatedAt     = createdAt,
                UpdatedAt     = createdAt,
            };
        }

        var bookings = new List<Booking>
        {
            // Active — started a week ago, ongoing for 1 month
            Make(listings[0], now.AddDays(-7), now.AddDays(23), BookingStatus.Active, "1 month", now.AddDays(-7)),
            // Confirmed — starts in 2 weeks
            Make(listings[1], now.AddDays(14), now.AddDays(44), BookingStatus.Confirmed, "1 month", now.AddDays(-2)),
            // Pending — submitted today, starts in 5 days
            Make(listings[2], now.AddDays(5), now.AddDays(35), BookingStatus.Pending, "1 month", now.AddHours(-3)),
        };

        // ─── Historical bookings + orders for analytics charts ──────────────
        // SupplierTeamController.GetAnalytics groups Orders by month, so we
        // need actual Order rows (not just Bookings) for the 6-month chart.
        // Tier-gated to keep Starter providers analytics-empty (matches prod).
        var suppliers = await db.Suppliers
            .Where(s => s.IsActive && s.Tier >= SupplierTier.Standard)
            .ToListAsync();

        var listingsBySupplier = (await db.Listings
            .Where(l => l.IsActive)
            .ToListAsync())
            .GroupBy(l => l.SupplierId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var historicalCustomers = await db.Users
            .Where(u => u.Role == UserRole.Customer && u.Status == UserStatus.Active)
            .Take(20)
            .ToListAsync();
        if (historicalCustomers.Count == 0) historicalCustomers.Add(customer);

        // (monthOffset, minBookings, maxBookings)
        var monthDistribution = new (int offset, int min, int max)[]
        {
            (-5, 1, 2),
            (-4, 1, 2),
            (-3, 2, 3),
            (-2, 2, 3),
            (-1, 1, 3),
            (0,  1, 2),
        };
        var monthAnchorBase = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var orders = new List<Order>();
        var supplierBookingCount = 0;
        foreach (var supplier in suppliers)
        {
            if (!listingsBySupplier.TryGetValue(supplier.Id, out var supplierListings) || supplierListings.Count == 0)
                continue;

            // Deterministic per-supplier RNG → reproducible across seed runs.
            var rng = new Random(unchecked((int)BitConverter.ToUInt32(supplier.Id.ToByteArray(), 0)));

            var supplierAdded = 0;
            foreach (var (offset, min, max) in monthDistribution)
            {
                var monthCount = rng.Next(min, max + 1);
                var monthAnchor = monthAnchorBase.AddMonths(offset);
                var daysInMonth = DateTime.DaysInMonth(monthAnchor.Year, monthAnchor.Month);
                // Cap current month at today to avoid future-dated history.
                var maxDay = offset == 0 ? Math.Min(now.Day, daysInMonth) : daysInMonth;

                for (var i = 0; i < monthCount; i++)
                {
                    var listing  = supplierListings[rng.Next(supplierListings.Count)];
                    var who      = historicalCustomers[rng.Next(historicalCustomers.Count)];
                    var day      = rng.Next(1, maxDay + 1);
                    var createdAt = DateTime.SpecifyKind(
                        monthAnchor.AddDays(day - 1).AddHours(rng.Next(0, 24)),
                        DateTimeKind.Utc);
                    var startDate = createdAt.AddDays(rng.Next(1, 14));
                    var endDate   = startDate.AddMonths(1);

                    var bookingId     = Guid.NewGuid();
                    var basePrice     = listing.PriceFrom;
                    var platformPrice = Math.Round(basePrice * 0.95m, 2);
                    var supplierPrice = Math.Round(basePrice * 0.85m, 2);
                    var margin        = platformPrice - supplierPrice;

                    bookings.Add(new Booking
                    {
                        Id            = bookingId,
                        UserId        = who.Id,
                        ListingId     = listing.Id,
                        SupplierId    = supplier.Id,
                        StartDate     = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
                        EndDate       = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc),
                        Duration      = "1 month",
                        Status        = BookingStatus.Completed,
                        ContactName   = who.Name,
                        ContactEmail  = who.Email,
                        ContactPhone  = who.Phone ?? "",
                        BasePrice     = basePrice,
                        PlatformPrice = platformPrice,
                        ExtrasTotal   = 0m,
                        VatAmount     = 0m,
                        Total         = platformPrice,
                        CreatedAt     = createdAt,
                        UpdatedAt     = createdAt,
                    });
                    orders.Add(new Order
                    {
                        Id              = Guid.NewGuid(),
                        BookingId       = bookingId,
                        SupplierId      = supplier.Id,
                        ListingId       = listing.Id,
                        ListingTitle    = listing.Title,
                        ListingType     = listing.Type,
                        City            = listing.City,
                        StartDate       = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
                        EndDate         = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc),
                        Duration        = "1 month",
                        ExtrasJson      = "[]",
                        AutoDispatch    = true,
                        IntegrationType = supplier.IntegrationType,
                        CustomerName    = who.Name,
                        CustomerEmail   = who.Email,
                        CustomerPhone   = who.Phone ?? "",
                        PartnerDiscountRate = supplier.PartnerDiscountRate,
                        BasePrice       = basePrice,
                        PlatformPrice   = platformPrice,
                        SupplierPrice   = supplierPrice,
                        ExtrasTotal     = 0m,
                        Total           = platformPrice,
                        Margin          = margin,
                        Status          = OrderStatus.Confirmed,
                        LeadStatus      = LeadStatus.Won,
                        CreatedAt       = createdAt,
                        UpdatedAt       = createdAt,
                    });
                    supplierAdded++;
                }
            }
            if (supplierAdded > 0)
            {
                Console.WriteLine($"[Seed] Bookings: supplier {supplier.Name} +{supplierAdded} historical.");
                supplierBookingCount++;
            }
        }

        await db.Bookings.AddRangeAsync(bookings);
        await db.Orders.AddRangeAsync(orders);
        var historicalOrders = orders;
        await db.SaveChangesAsync();

        Console.WriteLine(
            $"[Seed] Bookings done. ({bookings.Count} bookings, {historicalOrders.Count} historical orders across {supplierBookingCount} suppliers.)");

        if (!await db.PayoutEntries.AnyAsync())
        {
            var payoutEntries = historicalOrders.Select(o => new PayoutEntry
            {
                Id             = Guid.NewGuid(),
                OrderId        = o.Id,
                SupplierId     = o.SupplierId,
                SupplierAmount = o.SupplierPrice,
                PlatformMargin = o.Margin,
                Status         = PayoutStatus.Pending,
                CreatedAt      = o.CreatedAt,
            }).ToList();

            await db.PayoutEntries.AddRangeAsync(payoutEntries);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] PayoutEntries done. ({payoutEntries.Count} created)");
        }

        if (!await db.Invoices.AnyAsync())
        {
            var invoices = historicalOrders.Select(o => new Invoice
            {
                Id            = Guid.NewGuid(),
                BookingId     = o.BookingId,
                Amount        = o.Total,
                Status        = InvoiceStatus.Paid,
                PaymentMethod = "bank",
                IssuedAt      = o.CreatedAt,
                PaidAt        = o.CreatedAt.AddDays(1),
                CreatedAt     = o.CreatedAt,
            }).ToList();

            await db.Invoices.AddRangeAsync(invoices);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Invoices done. ({invoices.Count} created)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REVIEWS — backfill actual records to match listing.ReviewCount aggregates
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedReviewsAsync(RuumlyDbContext db)
    {
        var listings = await db.Listings
            .Where(l => l.IsActive && l.ReviewCount > 0)
            .ToListAsync();
        if (listings.Count == 0)
        {
            Console.WriteLine("[Seed] Reviews skipped — no eligible listings.");
            return;
        }

        // Realistic Estonian / Latvian / Lithuanian first names + last initials.
        string[] firstNames =
        [
            // Estonian
            "Mart", "Kati", "Jüri", "Liina", "Tiina", "Andres", "Kristel", "Tõnu",
            "Anneli", "Margus", "Helen", "Rainer", "Maarja", "Indrek", "Riin", "Priit",
            "Kaire", "Toomas", "Kerli", "Henrik",
            // Latvian
            "Jānis", "Anna", "Kārlis", "Līga", "Edgars", "Ilze", "Mārtiņš", "Inese",
            "Kaspars", "Dace", "Andris", "Sandra", "Pēteris", "Zane",
            // Lithuanian
            "Tomas", "Rūta", "Mindaugas", "Eglė", "Darius", "Gintarė", "Vytautas",
            "Aušra", "Linas", "Birutė", "Saulius", "Jurga",
        ];
        string[] lastInitials =
        [
            "K.", "T.", "S.", "L.", "M.", "P.", "R.", "V.", "B.", "J.",
            "A.", "O.", "E.", "I.", "U.", "N.", "H.", "G.",
        ];
        string?[] commentTemplates =
        [
            "Great service, would recommend!",
            "Smooth booking process, helpful staff.",
            "Clean facility, easy access.",
            "Good value for money.",
            "Friendly and professional.",
            "Easy to find, good location.",
            "Exactly as described.",
            "Will use again.",
            "No complaints.",
            "Quick communication, fair pricing.",
            null, null, null, // some reviews have no comment
        ];

        // Idempotent synthetic reviewer pool — deterministic Ids so repeated
        // seed runs do not duplicate users. Sized to give variety without
        // bloating the Users table.
        const int reviewerPoolSize = 60;
        var reviewerIds = Enumerable.Range(0, reviewerPoolSize)
            .Select(i => G($"reviewer-{i}")).ToArray();
        var existingReviewerIds = (await db.Users
            .Where(u => reviewerIds.Contains(u.Id))
            .Select(u => u.Id).ToListAsync()).ToHashSet();
        if (existingReviewerIds.Count < reviewerPoolSize)
        {
            var pwHash = BC.HashPassword("demo1234", workFactor: 12);
            var poolRng = new Random(20260430);
            var newReviewers = new List<User>();
            for (var i = 0; i < reviewerPoolSize; i++)
            {
                if (existingReviewerIds.Contains(reviewerIds[i])) continue;
                var first = firstNames[poolRng.Next(firstNames.Length)];
                var initial = lastInitials[poolRng.Next(lastInitials.Length)];
                newReviewers.Add(new User
                {
                    Id           = reviewerIds[i],
                    Name         = $"{first} {initial}",
                    Email        = $"reviewer-{i}@demo.local",
                    PasswordHash = pwHash,
                    Role         = UserRole.Customer,
                    Status       = UserStatus.Active,
                    RegisteredAt = Utc(2025, 1, 1).AddDays(poolRng.Next(0, 365)),
                });
            }
            await db.Users.AddRangeAsync(newReviewers);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Reviews: created {newReviewers.Count} reviewer users.");
        }

        var totalAdded = 0;
        foreach (var listing in listings)
        {
            // Per-listing idempotency: skip listings that already have reviews.
            if (await db.Reviews.AnyAsync(r => r.ListingId == listing.Id)) continue;

            var rng = new Random(unchecked((int)BitConverter.ToUInt32(listing.Id.ToByteArray(), 0)));
            var bookingsForListing = new List<Booking>(listing.ReviewCount);
            var reviewsForListing  = new List<Review>(listing.ReviewCount);
            var sumRating = 0;

            for (var i = 0; i < listing.ReviewCount; i++)
            {
                var reviewerId = reviewerIds[rng.Next(reviewerPoolSize)];
                var createdAt  = DateTime.SpecifyKind(
                    DateTime.UtcNow.AddDays(-rng.Next(30, 730)), DateTimeKind.Utc);
                // Bias toward higher ratings to keep average around 4.0–4.7.
                var rating = rng.NextDouble() switch
                {
                    < 0.55 => 5,
                    < 0.85 => 4,
                    < 0.95 => 3,
                    < 0.98 => 2,
                    _      => 1,
                };
                sumRating += rating;
                var bookingId     = Guid.NewGuid();
                var startDate     = createdAt.AddDays(-rng.Next(20, 60));
                var endDate       = createdAt.AddDays(-rng.Next(1, 7));
                var basePrice     = listing.PriceFrom;
                var platformPrice = Math.Round(basePrice * 0.95m, 2);

                bookingsForListing.Add(new Booking
                {
                    Id            = bookingId,
                    UserId        = reviewerId,
                    ListingId     = listing.Id,
                    SupplierId    = listing.SupplierId,
                    StartDate     = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
                    EndDate       = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc),
                    Duration      = "1 month",
                    Status        = BookingStatus.Completed,
                    BasePrice     = basePrice,
                    PlatformPrice = platformPrice,
                    ExtrasTotal   = 0m,
                    VatAmount     = 0m,
                    Total         = platformPrice,
                    CreatedAt     = startDate,
                    UpdatedAt     = endDate,
                });
                reviewsForListing.Add(new Review
                {
                    Id         = Guid.NewGuid(),
                    BookingId  = bookingId,
                    UserId     = reviewerId,
                    ListingId  = listing.Id,
                    SupplierId = listing.SupplierId,
                    Rating     = rating,
                    Comment    = commentTemplates[rng.Next(commentTemplates.Length)],
                    CreatedAt  = createdAt,
                });
            }

            // Recompute Rating to stay consistent with the actual records.
            // ReviewCount is left untouched (per spec).
            listing.Rating = Math.Round((decimal)sumRating / listing.ReviewCount, 2);

            await db.Bookings.AddRangeAsync(bookingsForListing);
            await db.Reviews.AddRangeAsync(reviewsForListing);
            Console.WriteLine(
                $"[Seed] Reviews: listing {listing.Id} (+{reviewsForListing.Count}, avg={listing.Rating}).");
            totalAdded += reviewsForListing.Count;
        }

        if (totalAdded > 0)
            await db.SaveChangesAsync();

        Console.WriteLine($"[Seed] Reviews done. ({totalAdded} created)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLATFORM SETTINGS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedPlatformSettingsAsync(RuumlyDbContext db)
    {
        if (await db.PlatformSettings.AnyAsync()) return;

        var defaults = new Dictionary<string, (string value, string note)>
        {
            ["siteName"]            = ("Ruumly",         "Platform name"),
            ["siteEmail"]           = ("info@ruumly.eu", "Contact email"),
            ["sitePhone"]           = ("+372 5555 1234", "Contact phone"),
            ["defaultLanguage"]     = ("et",             "Default UI language"),
            ["currency"]            = ("EUR",            "Currency code"),
            ["commissionRate"]      = ("5",              "Platform commission % on base price"),
            ["extrasMarginRate"]    = ("20",             "Ruumly margin % on supplier extras prices (used by IPricingConfigService)"),
            ["ruumlyMinMargin"]     = ("8",              "Minimum % Ruumly keeps on every booking (customerDiscount = partnerDiscount - ruumlyMinMargin)"),
            ["warehouseMarginRate"] = ("5",              "Platform savings % shown to customer for warehouse"),
            ["movingMarginRate"]    = ("5",              "Platform savings % shown to customer for moving"),
            ["trailerMarginRate"]   = ("5",              "Platform savings % shown to customer for trailer"),
            ["packingMargin"]       = ("0",              "Margin % on packing extra"),
            ["loadingMargin"]       = ("0",              "Margin % on loading extra"),
            ["insuranceMargin"]     = ("0",              "Margin % on insurance extra"),
            ["forkliftMargin"]      = ("0",              "Margin % on forklift extra"),
            ["emailNotifications"]  = ("true",           "Send email notifications"),
            ["maintenanceMode"]     = ("false",          "Put site in maintenance mode"),
            ["autoApproveListings"] = ("false",          "Auto-approve new provider listings"),
            ["defaultVatRate"]      = ("24",             "Estonia standard VAT rate (since Jan 2024)"),
            ["openHours"]           = ("E–R 9–18",       "Weekday opening hours shown on homepage"),
            ["openHoursSat"]        = ("",               "Saturday hours (leave empty to hide)"),
            ["inviteCodeRequired"]  = ("true",           "Set to true to require invite code at registration"),
            ["inviteCode"]           = ("RUUMLY2026", "The invite code users must enter to register"),
            ["showMovingService"]    = ("true",       "Show Moving service publicly (navbar, search, homepage)"),
            ["showTrailerService"]   = ("true",       "Show Trailer rental publicly (navbar, search, homepage)"),
            ["showFeaturedListings"] = ("true",       "Show featured listings section on homepage"),
            ["showHowItWorks"]       = ("true",       "Show how-it-works section on homepage"),
            ["showProviderCta"]      = ("true",       "Show provider CTA section on homepage"),
            ["showFaq"]              = ("true",       "Show FAQ section on homepage"),
            ["showMap"]              = ("true",       "Show interactive map on homepage"),
            ["heroSubtitle"]         = ("Üks platvorm — laopinnad, kolimine ja haagised. Leia asukoht, vali sobiv ühik, broneeri.", "Homepage hero subtitle — supports {discount} placeholder"),
            ["heroDiscount"]         = ("10",         "Discount percentage shown on homepage"),
            // ── Pricing config (read by IPricingConfigService) ──────────────
            ["defaultPartnerDiscount"]         = ("15",  "Default partner discount % shown to customer"),
            ["tier.starter.customerDiscount"]  = ("5",   "Starter tier: customer discount %"),
            ["tier.starter.monthlyFee"]        = ("0",   "Starter tier: monthly subscription fee (EUR)"),
            ["tier.standard.customerDiscount"] = ("8",   "Standard tier: customer discount %"),
            ["tier.standard.monthlyFee"]       = ("49",  "Standard tier: monthly subscription fee (EUR)"),
            ["tier.premium.customerDiscount"]  = ("12",  "Premium tier: customer discount %"),
            ["tier.premium.monthlyFee"]        = ("99",  "Premium tier: monthly subscription fee (EUR)"),
            ["commission.starter"]             = ("12",  "Starter tier commission %"),
            ["commission.standard"]            = ("8",   "Standard tier commission %"),
            ["commission.premium"]             = ("6",   "Premium tier commission %"),
            ["onboardingWindowDays"]           = ("90",  "Days of free onboarding for new suppliers"),
            // ── About page settings ────────────────────────────────────────
            ["aboutPage.enabled"]    = ("true",  "Controls whether /about renders at all"),
            ["aboutPage.showStats"]  = ("false", "Show platform stats on about page (default OFF until real numbers exist)"),
            // aboutPage.founders is a JSON array of founder objects:
            // {
            //   "name": "string",
            //   "role": { "et": "string", "en": "string", "ru": "string", "lv": "string", "lt": "string" },
            //   "bio":  { "et": "string", "en": "string", "ru": "string", "lv": "string", "lt": "string" },
            //   "photoUrl": "string | null",
            //   "email": "string | null",
            //   "linkedinUrl": "string | null"
            // }
            ["aboutPage.founders"]   = ("[]",    "JSON array of founder objects (see schema above)"),
            ["aboutPage.mission.et"] = ("Ruumly on Baltikumi esimene ühtne platvorm laopindade, kolimis­teenuste ja haagiste rentimiseks. Meie missioon on muuta ruumide ja teenuste leidmine lihtsaks, läbipaistvaks ning usaldusväärseks — nii eraklientidele kui ettevõtetele.", "About page mission text (Estonian)"),
            ["aboutPage.mission.en"] = ("Ruumly is the Baltics' first unified platform for storage, moving services, and trailer rentals. Our mission is to make finding spaces and services simple, transparent, and trustworthy — for both private and business customers.", "About page mission text (English)"),
            ["aboutPage.mission.ru"] = ("",      "About page mission text (Russian) — empty = falls back to ET"),
            ["aboutPage.mission.lv"] = ("",      "About page mission text (Latvian) — empty = falls back to ET"),
            ["aboutPage.mission.lt"] = ("",      "About page mission text (Lithuanian) — empty = falls back to ET"),
            // ── Blog settings ──────────────────────────────────────────────
            ["blog.enabled"]          = ("false", "Controls whether /blog route renders (true shows blog, false returns 404)"),
            ["blog.showInNav"]        = ("false", "Show Blog link in top navigation (requires blog.enabled=true)"),
            ["blog.showInFooter"]     = ("true",  "Show Blog link in footer (requires blog.enabled=true)"),
        };

        db.PlatformSettings.AddRange(defaults.Select(kv => new PlatformSetting
        {
            Key       = kv.Key,
            Value     = kv.Value.value,
            Note      = kv.Value.note,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "system",
        }));
        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Platform settings seeded.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FEATURE DEFINITIONS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedFeatureDefinitionsAsync(RuumlyDbContext db)
    {
        var featureKeys = new[] { "features.warehouse", "features.moving", "features.trailer" };

        // Only seed keys that don't exist yet (idempotent)
        var existing = await db.PlatformSettings
            .Where(s => featureKeys.Contains(s.Key))
            .Select(s => s.Key)
            .ToListAsync();

        if (existing.Count == featureKeys.Length) return;

        static object F(string key, string et, string en, string ru) => new
        {
            key,
            type = "boolean",
            showInSearch = true,
            labels = new { et, en, ru },
        };

        var warehouseFeatures = J(new[]
        {
            F("heated",      "Küttega",             "Heated",              "С отоплением"),
            F("indoor",      "Siseruumides",         "Indoor",              "В помещении"),
            F("access24_7",  "24/7 ligipääs",        "24/7 access",         "Доступ 24/7"),
            F("security",    "Turvasüsteem",          "Security system",     "Система безопасности"),
            F("loadingDock", "Laadimisplatvorm",      "Loading dock",        "Погрузочная платформа"),
            F("forklift",    "Tõstuk",               "Forklift available",   "Погрузчик"),
            F("shortTerm",   "Lühiajaline rent",      "Short-term rental",   "Краткосрочная аренда"),
            F("longTerm",    "Pikaajaline rent",      "Long-term rental",    "Долгосрочная аренда"),
        });

        var movingFeatures = J(new[]
        {
            F("withVan",       "Kaubikuga",       "With van",       "С фургоном"),
            F("packingHelp",   "Pakkimisabi",     "Packing help",   "Помощь с упаковкой"),
            F("loadingHelp",   "Laadimisabi",     "Loading help",   "Помощь с погрузкой"),
            F("pricingFixed",  "Fikseeritud hind","Fixed pricing",  "Фиксированная цена"),
        });

        var trailerFeatures = J(new[]
        {
            F("trailerClosed", "Kinnine haagis", "Closed trailer", "Закрытый прицеп"),
        });

        var seeds = new Dictionary<string, (string value, string note)>
        {
            ["features.warehouse"] = (warehouseFeatures, "Feature definitions for warehouse listings"),
            ["features.moving"]    = (movingFeatures,    "Feature definitions for moving listings"),
            ["features.trailer"]   = (trailerFeatures,   "Feature definitions for trailer listings"),
        };

        foreach (var kv in seeds.Where(kv => !existing.Contains(kv.Key)))
        {
            db.PlatformSettings.Add(new PlatformSetting
            {
                Key       = kv.Key,
                Value     = kv.Value.value,
                Note      = kv.Value.note,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "system",
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Feature definitions seeded.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KOOKON — real-partner demo data (Development env only).
    // Production onboarding goes via the regular admin flow, NOT this seeder.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedKookonAsync(RuumlyDbContext db)
    {
        // Environment gate: localhost-only. Production onboarding goes via admin UI.
        var aspEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (!string.Equals(aspEnv, "Development", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Seed] SeedKookonAsync skipped — ASPNETCORE_ENVIRONMENT='{aspEnv ?? "(null)"}', expected 'Development'.");
            return;
        }

        // Targeted guard — checks for Kookon specifically, NOT "any supplier".
        if (await db.Suppliers.AnyAsync(s => s.Slug == "kookon")) return;

        var kookonId = G("sup-kookon");
        var now      = Utc(2026, 1, 15);

        // ─── Photo pools (real Kookon imagery from kookon.ee) ─────────────────
        // Standard interior shots — 1920x1080, sourced from the Tänassilma gallery.
        var standardInteriorPool = new[]
        {
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_1-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_2-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_3-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_4-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_5-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_6-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_7-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_8-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2021/03/Tanassilma_9-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9838-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9844-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9853-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9856-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9861-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9862-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9868-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9869-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9875-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9885-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9896-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9908-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9910-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9920-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9925-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9928-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9937-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9941-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9984-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9987-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_9990-1920x1080.jpg",
            "https://www.kookon.ee/content/uploads/2016/11/MG_0021-1920x1080.jpg",
        };

        // Light-type shots — Lennuradari exterior views + hero (Lennuradari is the only Light site).
        var lightInteriorPool = new[]
        {
            "https://www.kookon.ee/content/uploads/2018/01/Lennuradari_01-.jpg",
            "https://www.kookon.ee/content/uploads/2018/01/kookon_piloodi_1-450x253.jpg",
            "https://www.kookon.ee/content/uploads/2018/01/kookon_piloodi_2-450x253.jpg",
            "https://www.kookon.ee/content/uploads/2018/01/kookon_piloodi_3-450x253.jpg",
        };

        // Helper: pick N images from a pool starting at offset, wrapping around.
        static string[] PickImages(string[] pool, int offset, int count)
        {
            var result = new string[count];
            for (var i = 0; i < count; i++) result[i] = pool[(offset + i) % pool.Length];
            return result;
        }

        // ─── Supplier ──────────────────────────────────────────────────────────
        db.Suppliers.Add(new Supplier
        {
            Id                = kookonId,
            Name              = "Kookon",
            Slug              = "kookon",
            GooglePlaceId     = null,   // TODO: add real Place ID once verified with Kookon
            RegistryCode      = null,                          // TODO confirm with Kookon
            ContactName       = "Kookon klienditugi",          // TODO confirm contact person
            ContactEmail      = "info@kookon.ee",
            ContactPhone      = "+372 5199 9075",
            IntegrationType   = IntegrationType.Email,
            RecipientEmail    = "info@kookon.ee",
            IsActive          = true,
            IntegrationHealth = IntegrationHealth.Healthy,
            Country           = "EE",
            // NOTE: spec said SupplierTier.Business; using Premium since enum has no Business.
            Tier              = SupplierTier.Premium,
            IsVerified        = true,
            VerifiedAt        = now,
            FoundingPartner   = true,
            Rating            = 4.8m,                          // TODO replace with real once reviews seeded
            ReviewCount       = 47,                            // TODO replace with real once reviews seeded
            Tagline           = "Nutikas laopinna rent kogu Tallinnas",
            LogoUrl           = "https://www.kookon.ee/content/uploads/2016/09/vikelaod-tume.svg",
            HeroImageUrl      = "https://www.kookon.ee/content/uploads/2021/09/Viimsi_01--450x272.jpg",
            WebsiteUrl        = "https://www.kookon.ee",
            FoundedYear       = 2016,                          // TODO confirm
            IsPartnerPagePublished = true,
            LongDescriptionTranslationsJson = J(new
            {
                et = "Kookon on innovaatiline iseteenindusladude lahendus — kontaktivaba rentimine, 24/7 ligipääs läbi mobiilirakenduse, päikeseenergia ja tipptasemel turvalahendused. 9 asukohta üle Tallinna piirkonna teevad sobiva laopinna leidmise lihtsaks. Kookoni Standard-tüüpi laopinnad on köetud, varustatud vee- ja kanalisatsioonivalmidusega ning tasuta internetiga; Light-tüüpi laopinnad on lihtsam ja soodsam lahendus puhtalt ladustamiseks.",
                en = "Kookon is an innovative self-storage solution — contactless rental, 24/7 mobile-first access, solar power, and top-tier security. With 9 locations across the Tallinn area, finding the right storage space is effortless. Kookon Standard units are heated and equipped with water, sewage and free Wi-Fi; Light units are a simpler, more affordable option for pure storage use cases.",
                ru = "Kookon — инновационное решение для самостоятельного хранения: бесконтактная аренда, круглосуточный доступ через мобильное приложение, солнечная энергия и современные системы безопасности. 9 локаций по всему Таллинну делают поиск нужного склада простым. Помещения Kookon Standard отапливаются, оборудованы водой, канализацией и бесплатным Wi-Fi; помещения Light — более простое и доступное решение для хранения.",
            }),
            CreatedAt = now,
            UpdatedAt = now,
        });

        // ─── Locations + Listings ─────────────────────────────────────────────
        var locations = new[]
        {
            new {
                Key="saue", Name="Saue Kookon",
                Address="Tule tänav 39", City="Saue",
                Lat=59.3153692, Lng=24.5708577,                 // exact
                TotalUnits=14, AvailableUnits=3, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2022/10/kookon_saue_ajutine-1-450x272.jpg",
                DescEt="Saue Kookon asub aktiivse liiklusega Tule tänaval, Transpordiameti Saue teenindusbüroo kõrval. Tallinna ringtee tagab väga hea ühenduse Tallinna ja Keilaga.",
            },
            new {
                Key="tabasalu-2", Name="Tabasalu 2 Kookon",
                Address="Lillemäe tee 2, Rannamõisa küla, Harku vald", City="Harku vald",
                Lat=59.4327, Lng=24.5520,                       // approx
                TotalUnits=27, AvailableUnits=2, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2022/09/20230517_115739-450x273.jpg",
                DescEt="Tabasalu Kookon asub kiiresti arenevas Harku vallas. Harku tee tagab väga hea ühenduse Tallinna ringtee ja Laagri piirkonnaga. Haabersti ning Õismäe asuvad 10-minutilise autosõidu kaugusel.",
            },
            new {
                Key="viimsi", Name="Viimsi Kookon",
                Address="Paekaare tee 2", City="Viimsi",
                Lat=59.5099011, Lng=24.8535674,                 // exact
                TotalUnits=36, AvailableUnits=1, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2021/09/Viimsi_01--450x272.jpg",
                DescEt="Viimsi Kookon asub logistiliselt suurepärases asukohas, Pärnamäe tee ja Aiandi tee ristmikul. Väga hea ligipääs on tagatud kogu Viimsi poolsaarel; mugav ühendus Pirita tee ja Tallinna kesklinnaga.",
            },
            new {
                Key="tabasalu-1", Name="Tabasalu 1 Kookon",
                Address="Lillemäe tee 1, Rannamõisa küla, Harku vald", City="Harku vald",
                Lat=59.4327, Lng=24.5523,                       // approx
                TotalUnits=21, AvailableUnits=0, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2021/03/Tabasalu_07--450x272.jpg",
                DescEt="Tabasalu Kookoni esimene asukoht. Harku tee tagab väga hea ühenduse Tallinna ringtee ja Laagri piirkonnaga.",
            },
            new {
                Key="peetri", Name="Peetri Kookon",
                Address="Valguse tee 2, Rae vald", City="Rae vald",
                Lat=59.398548, Lng=24.828255,                   // exact
                TotalUnits=23, AvailableUnits=2, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2020/09/Peetri_01--450x272.jpg",
                DescEt="Peetri Kookon asub Tallinna linnapiiril, Tartu maantee ääres, Mõigu tehnopargis. Tagatud on suurepärane ligipääs kesklinna ja Tallinna ringteele.",
            },
            new {
                Key="laagri", Name="Laagri Kookon",
                Address="Kuuse põik 30", City="Laagri",
                Lat=59.3483487, Lng=24.6122815,                 // exact
                TotalUnits=17, AvailableUnits=0, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2018/07/Laagri_01--450x272.jpg",
                DescEt="Laagri Kookon asub Laagri südames. Laagri liiklussõlme tõttu on tagatud suurepärane ligipääs nii Tallinna, Pärnu kui Saku suunalt; väga hea ühendus ka Paldiski maanteega.",
            },
            new {
                Key="lennuradari", Name="Lennuradari Kookon",
                Address="Piloodi tee 5, Rae vald", City="Rae vald",
                Lat=59.4017, Lng=24.8323,                       // approx
                TotalUnits=28, AvailableUnits=4, IsLight=true,  // ← only Light-type site
                Img="https://www.kookon.ee/content/uploads/2018/01/Lennuradari_01-.jpg",
                DescEt="Lennuradari Kookon asub Tallinna piiril, Tallinna ringtee serval, ~12-minutilise autosõidu kaugusel kesklinnast. Kookon Light-tüüpi laopinnad — lihtsam ehitus, mõeldud eelkõige ladustavale kliendile.",
            },
            new {
                Key="ulemiste", Name="Ülemiste Kookon",
                Address="Tapri 5", City="Tallinn",
                Lat=59.4214, Lng=24.7918,                       // approx
                TotalUnits=47, AvailableUnits=1, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2017/08/Ülemiste_01--450x272.jpg",
                DescEt="Ülemiste Kookon asub Tallinna lennujaama läheduses, Tapri tänaval. Uuenenud Suur-Sõjamäe tänav, lennujaama lähedus ning hea ühendus Tallinna kesklinnaga tagavad mugava ligipääsu igal ajahetkel.",
            },
            new {
                Key="tanassilma", Name="Tänassilma Kookon",
                Address="Tänassilma tee 17", City="Tallinn",
                Lat=59.3593, Lng=24.6253,                       // approx
                TotalUnits=33, AvailableUnits=0, IsLight=false,
                Img="https://www.kookon.ee/content/uploads/2016/09/Tänassilma_01--450x272.jpg",
                DescEt="Tänassilma Kookon asub Tallinna linnapiiril. Äsja valminud Laagri liiklussõlme tulemusena on tagatud suurepärane ligipääs Tallinna, Pärnu ja Saku suunalt; kesklinn 20-minutilise autosõidu kaugusel.",
            },
        };

        // Three size brackets per location.
        // STANDARD prices: Tallinn market estimates with ~30% premium over Light (heated/water/internet).
        //                  TODO confirm with Kookon at meeting — great talking point.
        // LIGHT prices:    REAL data scraped from kookon.ee/lennuradari/ (January 2026).
        var standardSizes = new[]
        {
            (Suffix: "s", Size: 4m,  Price: 49m,  Label: "Väike (~4 m²)"),       // estimated
            (Suffix: "m", Size: 10m, Price: 119m, Label: "Keskmine (~10 m²)"),   // estimated
            (Suffix: "l", Size: 20m, Price: 219m, Label: "Suur (~20 m²)"),       // estimated
        };
        var lightSizes = new[]
        {
            (Suffix: "s", Size: 18m,   Price: 141m, Label: "Light 18 m²"),       // real (Lennuradari ladu #15)
            (Suffix: "m", Size: 24.5m, Price: 193m, Label: "Light 24,5 m²"),     // real (Lennuradari ladu #12)
            (Suffix: "l", Size: 32.3m, Price: 254m, Label: "Light 32,3 m²"),     // real (Lennuradari ladu #7)
        };

        var imageOffset = 0;     // rotates through standardInteriorPool to give every listing distinct shots

        foreach (var loc in locations)
        {
            var locId = G($"loc-kookon-{loc.Key}");

            // Location-level images: exterior hero + 4 interior shots from the pool (rotating)
            var locImages = new List<string> { loc.Img };
            if (loc.IsLight)
            {
                locImages.AddRange(lightInteriorPool.Skip(1));   // skip the hero (already added if same)
            }
            else
            {
                locImages.AddRange(PickImages(standardInteriorPool, imageOffset, 4));
                imageOffset = (imageOffset + 4) % standardInteriorPool.Length;
            }

            db.SupplierLocations.Add(new SupplierLocation
            {
                Id           = locId,
                SupplierId   = kookonId,
                Name         = loc.Name,
                Address      = loc.Address,
                City         = loc.City,
                Country      = "EE",
                Lat          = loc.Lat,
                Lng          = loc.Lng,
                IsActive     = true,
                IsSynthetic  = false,
                ImagesJson   = J(locImages),
                Description  = loc.DescEt,
                OpeningHours = "24/7 (mobiilirakenduse kaudu)",
                TotalUnitCount     = loc.TotalUnits,
                AvailableUnitCount = loc.AvailableUnits,
                CreatedAt    = now,
                UpdatedAt    = now,
            });

            var sizes     = loc.IsLight ? lightSizes : standardSizes;
            var available = loc.AvailableUnits > 0;

            foreach (var s in sizes)
            {
                // Per-listing images: 2 photos rotated through the appropriate pool
                var listingImages = loc.IsLight
                    ? PickImages(lightInteriorPool, imageOffset % lightInteriorPool.Length, Math.Min(2, lightInteriorPool.Length))
                    : PickImages(standardInteriorPool, imageOffset, 2);
                imageOffset = (imageOffset + 2) % standardInteriorPool.Length;

                var featuresJson = loc.IsLight
                    ? J(new
                    {
                        size       = (double)s.Size, sizeUnit = "m²",
                        heated     = false, indoor = true, access24_7 = true, security = true,
                        loadingDock= false, forklift = false, shortTerm = true, longTerm = true,
                        features   = new[] { "24/7 ligipääs", "VideoValve", "Mobiilirakendus", "Iseteenindus" },
                    })
                    : J(new
                    {
                        size       = (double)s.Size, sizeUnit = "m²",
                        heated     = true, indoor = true, access24_7 = true, security = true,
                        loadingDock= false, forklift = false, shortTerm = true, longTerm = true,
                        features   = new[] { "Köetud", "24/7 ligipääs", "VideoValve", "Mobiilirakendus", "Tasuta WiFi", "Iseteenindus" },
                    });

                db.Listings.Add(new Listing
                {
                    Id           = G($"listing-kookon-{loc.Key}-{s.Suffix}"),
                    Type         = ListingType.Warehouse,
                    SupplierId   = kookonId,
                    LocationId   = locId,
                    Title        = $"{loc.Name} — {s.Label}",
                    Address      = loc.Address,
                    City         = loc.City,
                    Lat          = loc.Lat,
                    Lng          = loc.Lng,
                    PriceFrom    = s.Price,
                    PriceUnit    = "€/kuu",
                    SizeM2       = s.Size,
                    AvailableNow = available,
                    IsActive     = true,
                    Rating       = 4.8m,
                    ReviewCount  = 8,
                    Badge        = ListingBadge.Promoted,
                    Description  = loc.IsLight
                        ? $"{loc.Name} Light-tüüpi laopind ({s.Size} m²). Lihtne ja soodne lahendus ladustamiseks. 24/7 ligipääs mobiilirakenduse kaudu."
                        : $"{loc.Name} Standard-tüüpi laopind ({s.Size} m²). Köetud, varustatud vee- ja kanalisatsioonivalmidusega ning tasuta WiFi-ga. 24/7 ligipääs mobiilirakenduse kaudu.",
                    ImagesJson   = J(listingImages),
                    FeaturesJson = featuresJson,
                    CreatedAt    = now,
                    UpdatedAt    = now,
                });
            }
        }

        // ─── Default contract template ─────────────────────────────────────────
        if (!await db.ContractTemplates.AnyAsync(t => t.SupplierId == kookonId))
        {
            db.ContractTemplates.Add(new ContractTemplate
            {
                Id           = G("ct-kookon-default"),
                SupplierId   = kookonId,
                Name         = "Standard storage agreement",
                IsDefault    = true,
                IsActive     = true,
                HtmlTemplate = DefaultContractHtml("Kookon OÜ"),
                CreatedAt    = now,
                UpdatedAt    = now,
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"[Seed] Kookon partner seeded (Development env): 1 supplier, {locations.Length} locations, {locations.Length * 3} listings.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BOXO — second prospective-partner demo data (Development env only).
    // Modelled on SeedKookonAsync; same pattern, env gate, and idempotency.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedBoxoAsync(RuumlyDbContext db)
    {
        // Environment gate — same as Kookon: localhost-only.
        var aspEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (!string.Equals(aspEnv, "Development", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Seed] SeedBoxoAsync skipped — ASPNETCORE_ENVIRONMENT='{aspEnv ?? "(null)"}', expected 'Development'.");
            return;
        }

        // Targeted guard — checks for Boxo specifically.
        if (await db.Suppliers.AnyAsync(s => s.Slug == "boxo")) return;

        var boxoId = G("sup-boxo");
        var now    = Utc(2026, 1, 15);

        // ─── Photo pools (real Boxo imagery from boxo.ee) ─────────────────────
        var interiorPool = new[]
        {
            "https://boxo.ee/_next/static/media/DSC03912.f8ea3d35.webp",
            "https://boxo.ee/_next/static/media/man.063f977d.webp",
            "https://boxo.ee/_next/static/media/boxes.a0b41b40.webp",
            "https://boxo.ee/_next/static/media/window.a8bf5a5f.webp",
            "https://boxo.ee/_next/static/media/couch.1a4e9a98.webp",
            "https://boxo.ee/_next/static/media/sport-things.0b371e15.webp",
        };

        static string[] PickImages(string[] pool, int offset, int count)
        {
            var result = new string[count];
            for (var i = 0; i < count; i++) result[i] = pool[(offset + i) % pool.Length];
            return result;
        }

        // ─── Supplier ──────────────────────────────────────────────────────────
        db.Suppliers.Add(new Supplier
        {
            Id                = boxoId,
            Name              = "BOXO",
            Slug              = "boxo",
            GooglePlaceId     = null,   // TODO: add real Place ID once verified with BOXO
            RegistryCode      = "16794172",
            ContactName       = "BOXO klienditugi",       // TODO confirm
            ContactEmail      = "kliendid@boxo.ee",
            ContactPhone      = "+372 55 555 826",
            IntegrationType   = IntegrationType.Email,
            RecipientEmail    = "kliendid@boxo.ee",
            IsActive          = true,
            IntegrationHealth = IntegrationHealth.Healthy,
            Country           = "EE",
            Tier              = SupplierTier.Standard,    // tier rotation — Kookon is Premium
            IsVerified        = true,
            VerifiedAt        = now,
            FoundingPartner   = false,
            Rating            = 4.7m,
            ReviewCount       = 32,
            Tagline           = "Minilaod Tallinnas — broneeri 15 minutiga",
            LogoUrl           = "https://boxo.ee/_next/static/media/logo.3dcd9476.svg",
            HeroImageUrl      = "https://boxo.ee/_next/static/media/DSC03912.f8ea3d35.webp",
            WebsiteUrl        = "https://boxo.ee",
            FoundedYear       = 2021,
            IsPartnerPagePublished = true,
            LongDescriptionTranslationsJson = J(new
            {
                et = "BOXO pakub nutikaid mini-laopindu Tallinnas — täielikult veebis broneeritavad, professionaalse transporditeenusega kohale ja 15-minutilise ligipääsu seadistusega. 6 asukohta üle Tallinna, suurused 1,4 m² kuni 30 m² (XS kuni XL). Kõik laod on köetud, valvesignalisatsiooni ja videovalvega. Tegutseb nii Eestis kui Lätis (Riias).",
                en = "BOXO offers smart mini-storage in Tallinn — fully online booking, professional door-to-storage transport, and 15-minute access setup. 6 locations across the city, sizes from 1.4 m² (XS) to 30 m² (XL). All units are heated and protected by alarm + CCTV. Operating in Estonia and Latvia (Riga).",
                ru = "BOXO предлагает умные мини-склады в Таллинне — полностью онлайн-бронирование, профессиональная доставка вещей и настройка доступа за 15 минут. 6 локаций по городу, размеры от 1,4 м² (XS) до 30 м² (XL). Все боксы отапливаются, под охраной и видеонаблюдением. Работаем в Эстонии и Латвии (Рига).",
            }),
            CreatedAt = now,
            UpdatedAt = now,
        });

        // ─── Locations ────────────────────────────────────────────────────────
        // Real Boxo addresses from boxo.ee. Coords are approximate (within ~200m);
        // refine when verified. Availability counts are illustrative.
        var locations = new[]
        {
            new {
                Key="lasnamae-punane", Name="BOXO Lasnamäe (Punane)",
                Address="Punane tn 6", City="Tallinn",
                Lat=59.4358, Lng=24.8217,                       // approx
                TotalUnits=18, AvailableUnits=4,
                DescEt="Lasnamäe BOXO Punase tänaval, mugav ligipääs Punase ja Pae tänavate ristmikult. Lasnamäe Centruumi ja Smuuli tee lähedal — sobib eriti Lasnamäe ning Kesklinna idapoolsete elanike jaoks.",
            },
            new {
                Key="mustamae-tuuliku", Name="BOXO Mustamäe (Tuuliku)",
                Address="Tuuliku tee 2", City="Tallinn",
                Lat=59.4097, Lng=24.6608,                       // approx
                TotalUnits=22, AvailableUnits=2,
                DescEt="Mustamäe BOXO Tuuliku teel, lihtne ligipääs Sõpruse pst ja Akadeemia teelt. Sobib hästi Mustamäe, Õismäe ja Nõmme elanikele; lähim Tallinna Tehnikaülikoolist.",
            },
            new {
                Key="kopli-72a", Name="BOXO Kopli 72a",
                Address="Kopli tn 72a", City="Tallinn",
                Lat=59.4534, Lng=24.7156,                       // approx
                TotalUnits=16, AvailableUnits=3,
                DescEt="Kopli BOXO Kopli tänaval, Põhja-Tallinna südames. Hea ligipääs Telliskivi ja Kalamajast; trammipeatus mõne minuti kaugusel.",
            },
            new {
                Key="lasnamae-vesse", Name="BOXO Lasnamäe (Vesse)",
                Address="Vesse 12", City="Tallinn",
                Lat=59.4400, Lng=24.8050,                       // approx
                TotalUnits=14, AvailableUnits=1,
                DescEt="Lasnamäe BOXO Vesse tänaval, ligipääs Lasnamäe põhjapoolsest osast. Sobib eriti Sikupilli, Kuristiku ja Pae piirkondade klientidele.",
            },
            new {
                Key="mustamae-artelli", Name="BOXO Mustamäe (Artelli)",
                Address="Artelli 17", City="Tallinn",
                Lat=59.4106, Lng=24.7028,                       // approx
                TotalUnits=20, AvailableUnits=5,
                DescEt="Mustamäe BOXO Artelli tänaval, mugav asukoht Tammsaare tee ja Akadeemia tee vahelisel alal. Lähim Mustamäe keskuse ja Olümpia spordikeskuse külastajatele.",
            },
            new {
                Key="kopli-heina", Name="BOXO Kopli (Heina)",
                Address="Heina tn 33", City="Tallinn",
                Lat=59.4520, Lng=24.7280,                       // approx
                TotalUnits=12, AvailableUnits=0,                // ← fully booked
                DescEt="Kopli BOXO Heina tänaval, Põhja-Tallinnas. Otsene ligipääs sadama-, raudtee- ja Põhja pst kaudu; sobib Kopli ja Pelguranna elanikele.",
            },
        };

        // Three size brackets per location, modelled on Boxo's real pricing
        // (boxo.ee, Jan 2026). Net of VAT, monthly equivalent (their site quotes
        // 4-week periods so monthly ≈ 4-week × 1.083). TODO confirm with BOXO.
        var sizes = new[]
        {
            (Suffix: "s", Size: 3.7m,  Price: 38m,  Label: "S (~3.7 m²)"),     // BOXO S
            (Suffix: "m", Size: 7.4m,  Price: 50m,  Label: "M (~7.4 m²)"),     // BOXO M
            (Suffix: "l", Size: 15m,   Price: 77m,  Label: "L (~15 m²)"),      // BOXO L
        };

        var imageOffset = 0;

        foreach (var loc in locations)
        {
            var locId = G($"loc-boxo-{loc.Key}");

            // Each location: 4 interior shots (rotated through the pool)
            var locImages = PickImages(interiorPool, imageOffset, 4);
            imageOffset = (imageOffset + 2) % interiorPool.Length;

            db.SupplierLocations.Add(new SupplierLocation
            {
                Id                 = locId,
                SupplierId         = boxoId,
                Name               = loc.Name,
                Address            = loc.Address,
                City               = loc.City,
                Country            = "EE",
                Lat                = loc.Lat,
                Lng                = loc.Lng,
                IsActive           = true,
                IsSynthetic        = false,
                ImagesJson         = J(locImages),
                Description        = loc.DescEt,
                OpeningHours       = "24/7 (mobiilirakenduse kaudu)",
                TotalUnitCount     = loc.TotalUnits,
                AvailableUnitCount = loc.AvailableUnits,
                CreatedAt          = now,
                UpdatedAt          = now,
            });

            var available = loc.AvailableUnits > 0;

            foreach (var s in sizes)
            {
                var listingImages = PickImages(interiorPool, imageOffset, 2);
                imageOffset = (imageOffset + 1) % interiorPool.Length;

                db.Listings.Add(new Listing
                {
                    Id           = G($"listing-boxo-{loc.Key}-{s.Suffix}"),
                    Type         = ListingType.Warehouse,
                    SupplierId   = boxoId,
                    LocationId   = locId,
                    Title        = $"{loc.Name} — {s.Label}",
                    Address      = loc.Address,
                    City         = loc.City,
                    Lat          = loc.Lat,
                    Lng          = loc.Lng,
                    PriceFrom    = s.Price,
                    PriceUnit    = "€/kuu",
                    SizeM2       = s.Size,
                    AvailableNow = available,
                    IsActive     = true,
                    Rating       = 4.7m,
                    ReviewCount  = 6,
                    Badge        = ListingBadge.Promoted,
                    Description  = $"{loc.Name} mini-laopind ({s.Size} m²). Köetud, varustatud valvesignalisatsiooniga ja videovalvega. 24/7 ligipääs. Online-broneerimine ja transporditeenus saadaval.",
                    ImagesJson   = J(listingImages),
                    FeaturesJson = J(new
                    {
                        size       = (double)s.Size, sizeUnit = "m²",
                        heated     = true, indoor = true, access24_7 = true, security = true,
                        loadingDock= false, forklift = false, shortTerm = true, longTerm = true,
                        features   = new[] { "Köetud", "24/7 ligipääs", "Häiresüsteem", "VideoValve", "Online-broneerimine", "Transporditeenus" },
                    }),
                    CreatedAt    = now,
                    UpdatedAt    = now,
                });
            }
        }

        // ─── Default contract template ─────────────────────────────────────────
        if (!await db.ContractTemplates.AnyAsync(t => t.SupplierId == boxoId))
        {
            db.ContractTemplates.Add(new ContractTemplate
            {
                Id           = G("ct-boxo-default"),
                SupplierId   = boxoId,
                Name         = "Standard storage agreement",
                IsDefault    = true,
                IsActive     = true,
                HtmlTemplate = DefaultContractHtml("BOXO OÜ"),
                CreatedAt    = now,
                UpdatedAt    = now,
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"[Seed] BOXO partner seeded (Development env): 1 supplier, {locations.Length} locations, {locations.Length * 3} listings.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0)
        => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    // Uses a non-interpolated raw string to keep the {{variable}} template placeholders
    // intact, then substitutes only the supplier name via .Replace().
    private static string DefaultContractHtml(string supplierName) =>
        """
        <div style="font-family: Arial, sans-serif; max-width: 700px; margin: 0 auto; padding: 40px 20px; color: #111;">
          <h1 style="font-size: 22px; font-weight: bold; border-bottom: 2px solid #111; padding-bottom: 12px;">
            STORAGE RENTAL AGREEMENT
          </h1>
          <p style="margin: 16px 0;"><strong>Service provider:</strong> __SUPPLIER__</p>
          <p style="margin: 16px 0;"><strong>Tenant:</strong> {{tenant_name}}</p>
          <p style="margin: 16px 0;"><strong>ID code:</strong> {{tenant_id_code}}</p>
          <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;" />
          <h2 style="font-size: 16px;">1. STORAGE UNIT</h2>
          <p><strong>Unit:</strong> {{unit_title}}</p>
          <p><strong>Address:</strong> {{unit_address}}</p>
          <p><strong>Rental price:</strong> {{price}} {{price_unit}}</p>
          <p><strong>Start date:</strong> {{start_date}}</p>
          <h2 style="font-size: 16px; margin-top: 20px;">2. TERMS</h2>
          <p>The tenant agrees to use the storage unit only for storing lawful personal or business goods.
             Prohibited items include hazardous materials, perishable goods, and items prohibited by
             Estonian law. The provider reserves the right to terminate this agreement with 30 days
             written notice.</p>
          <h2 style="font-size: 16px; margin-top: 20px;">3. PAYMENT</h2>
          <p>Rent is payable in advance per the agreed billing period. Late payment incurs a
             0.05% daily penalty on the outstanding amount.</p>
          <h2 style="font-size: 16px; margin-top: 20px;">4. ACCESS</h2>
          <p>Access to the storage unit is provided 24/7 via the Ruumly platform or the
             provider's mobile application.</p>
          <div style="margin-top: 40px; border-top: 1px solid #ddd; padding-top: 20px;">
            <p><strong>Signed:</strong> {{signed_date}}</p>
            <p style="margin-top: 16px;"><strong>Tenant signature:</strong></p>
          </div>
        </div>
        """.Replace("__SUPPLIER__", supplierName);
}
