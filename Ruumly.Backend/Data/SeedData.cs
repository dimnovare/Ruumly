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
            await SeedUsersAsync(db);
            await SeedRoutingRulesAsync(db);
            await SeedPlatformSettingsAsync(db);
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
        });

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Listings done.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // USERS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedUsersAsync(RuumlyDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Email == "andres@email.com")) return;

        var pwHash = BC.HashPassword("demo1234", workFactor: 12);

        db.Users.AddRange(new List<User>
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
        });

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Users done.");
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
            ["extrasMarginRate"]    = ("0",              "Platform margin % on extras (0 = pass-through)"),
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
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0)
        => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);
}
