using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Data;

/// <summary>
/// Idempotent demo seeder. Adds demo suppliers and listings so the site is
/// demonstrable when there are zero real partners. All demo suppliers have
/// Name prefixed with "[Demo]" for clear identification and removal later
/// (see DELETE /api/admin/suppliers/demo).
///
/// Gated by env var SEED_DEMO_DATA=true AND an empty Listings table.
/// Never runs in normal operation once real data exists.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedIfRequestedAsync(RuumlyDbContext db, IConfiguration config, ILogger logger)
    {
        var flag = config["SEED_DEMO_DATA"] ?? Environment.GetEnvironmentVariable("SEED_DEMO_DATA");
        var shouldSeed = string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);

        if (!shouldSeed)
        {
            logger.LogInformation("DemoSeeder: skipped — SEED_DEMO_DATA is not set to 'true'");
            return;
        }

        if (await db.Listings.AnyAsync())
        {
            logger.LogInformation("DemoSeeder: skipped — listings already exist");
            return;
        }

        logger.LogWarning("DemoSeeder: SEED_DEMO_DATA=true and listings table is empty — seeding demo data");

        var tallinnStorage = new Supplier
        {
            Id           = Guid.NewGuid(),
            Name         = "[Demo] Ruumly Tallinn Storage",
            RegistryCode = "DEMO10001",
            ContactName  = "Demo Contact",
            ContactEmail = "demo.tallinn-storage@ruumly.eu",
            ContactPhone = "+372 5000 0001",
            IsActive     = true,
            Country      = "EE",
            Tier         = SupplierTier.Starter,
            Rating       = 4.6m,
            ReviewCount  = 28,
            Notes        = "Demo data. Remove once real partners onboard.",
        };

        var tartuStorage = new Supplier
        {
            Id           = Guid.NewGuid(),
            Name         = "[Demo] Tartu Mini-Storage",
            RegistryCode = "DEMO10002",
            ContactName  = "Demo Contact",
            ContactEmail = "demo.tartu-storage@ruumly.eu",
            ContactPhone = "+372 5000 0002",
            IsActive     = true,
            Country      = "EE",
            Tier         = SupplierTier.Starter,
            Rating       = 4.4m,
            ReviewCount  = 14,
            Notes        = "Demo data. Remove once real partners onboard.",
        };

        var movingCo = new Supplier
        {
            Id           = Guid.NewGuid(),
            Name         = "[Demo] Eesti Kolimisteenused",
            RegistryCode = "DEMO10003",
            ContactName  = "Demo Contact",
            ContactEmail = "demo.moving@ruumly.eu",
            ContactPhone = "+372 5000 0003",
            IsActive     = true,
            Country      = "EE",
            Tier         = SupplierTier.Starter,
            Rating       = 4.7m,
            ReviewCount  = 35,
            Notes        = "Demo data. Remove once real partners onboard.",
        };

        var trailerCo = new Supplier
        {
            Id           = Guid.NewGuid(),
            Name         = "[Demo] Haagiste Rent Eesti",
            RegistryCode = "DEMO10004",
            ContactName  = "Demo Contact",
            ContactEmail = "demo.trailer@ruumly.eu",
            ContactPhone = "+372 5000 0004",
            IsActive     = true,
            Country      = "EE",
            Tier         = SupplierTier.Starter,
            Rating       = 4.5m,
            ReviewCount  = 19,
            Notes        = "Demo data. Remove once real partners onboard.",
        };

        db.Suppliers.AddRange(tallinnStorage, tartuStorage, movingCo, trailerCo);

        const double tallinnLat = 59.4370, tallinnLng = 24.7536;
        const double tartuLat   = 58.3776, tartuLng   = 26.7290;
        const double parnuLat   = 58.3858, parnuLng   = 24.4971;

        var listings = new List<Listing>
        {
            // ── Supplier 1: Tallinn storage (XS/S/M/L) ─────────────────────────
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = tallinnStorage.Id,
                Type         = ListingType.Warehouse,
                Title        = "XS hoiukapp · Tallinn",
                Address      = "Liivalaia 33, Tallinn",
                City         = "Tallinn",
                Lat          = tallinnLat,
                Lng          = tallinnLng,
                PriceFrom    = 19m,
                PriceUnit    = "/kuu",
                SizeM2       = 1.0m,
                Description  = "Väike kapp dokumentidele ja hooajakaubale. Sobib perele, kes vajab vaid väikest hoiukohta.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.5m,
                ReviewCount  = 7,
            },
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = tallinnStorage.Id,
                Type         = ListingType.Warehouse,
                Title        = "S hoiukapp · Tallinn",
                Address      = "Liivalaia 33, Tallinn",
                City         = "Tallinn",
                Lat          = tallinnLat,
                Lng          = tallinnLng,
                PriceFrom    = 35m,
                PriceUnit    = "/kuu",
                SizeM2       = 2.5m,
                Description  = "Väike hoiukapp — ideaalne korteri lisaruumiks, hooajaliste riiete ja spordivarustuse hoidmiseks.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.6m,
                ReviewCount  = 11,
            },
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = tallinnStorage.Id,
                Type         = ListingType.Warehouse,
                Title        = "M laoruum · Tallinn",
                Address      = "Liivalaia 33, Tallinn",
                City         = "Tallinn",
                Lat          = tallinnLat,
                Lng          = tallinnLng,
                PriceFrom    = 65m,
                PriceUnit    = "/kuu",
                SizeM2       = 5.0m,
                Description  = "Keskmise suurusega laoruum, sobib 1-toalise korteri mööblile.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.7m,
                ReviewCount  = 6,
            },
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = tallinnStorage.Id,
                Type         = ListingType.Warehouse,
                Title        = "L laoruum · Tallinn",
                Address      = "Liivalaia 33, Tallinn",
                City         = "Tallinn",
                Lat          = tallinnLat,
                Lng          = tallinnLng,
                PriceFrom    = 110m,
                PriceUnit    = "/kuu",
                SizeM2       = 10.0m,
                Description  = "Suur laoruum ettevõttele või 2-3-toalise korteri sisustuse hoidmiseks.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.6m,
                ReviewCount  = 4,
            },

            // ── Supplier 2: Tartu storage (M/XL) ───────────────────────────────
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = tartuStorage.Id,
                Type         = ListingType.Warehouse,
                Title        = "M hoiuruum · Tartu",
                Address      = "Riia 140, Tartu",
                City         = "Tartu",
                Lat          = tartuLat,
                Lng          = tartuLng,
                PriceFrom    = 55m,
                PriceUnit    = "/kuu",
                SizeM2       = 5.0m,
                Description  = "Keskmise suurusega hoiuruum Tartu südames.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.3m,
                ReviewCount  = 8,
            },
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = tartuStorage.Id,
                Type         = ListingType.Warehouse,
                Title        = "XL garaaž-laoruum · Tartu",
                Address      = "Riia 140, Tartu",
                City         = "Tartu",
                Lat          = tartuLat,
                Lng          = tartuLng,
                PriceFrom    = 180m,
                PriceUnit    = "/kuu",
                SizeM2       = 22.0m,
                Description  = "Suur garaaži-tüüpi ladustamisruum, sobib mööblile, sõidukile või ärivarudele.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.5m,
                ReviewCount  = 6,
            },

            // ── Supplier 3: Moving services (Tallinn/Tartu) ────────────────────
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = movingCo.Id,
                Type         = ListingType.Moving,
                Title        = "Kolimisteenus · Tallinn",
                Address      = "Peterburi tee 2, Tallinn",
                City         = "Tallinn",
                Lat          = tallinnLat,
                Lng          = tallinnLng,
                PriceFrom    = 35m,
                PriceUnit    = "/tund",
                SizeM2       = null,
                Description  = "Professionaalne kolimisteenus Tallinnas: pakkimine, laadimine, transport.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.8m,
                ReviewCount  = 22,
            },
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = movingCo.Id,
                Type         = ListingType.Moving,
                Title        = "Kolimisteenus · Tartu",
                Address      = "Turu 9, Tartu",
                City         = "Tartu",
                Lat          = tartuLat,
                Lng          = tartuLng,
                PriceFrom    = 32m,
                PriceUnit    = "/tund",
                SizeM2       = null,
                Description  = "Kolimisteenus Tartus ja Lõuna-Eestis. Kohalik meeskond, kiire vastus.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.6m,
                ReviewCount  = 13,
            },

            // ── Supplier 4: Trailer rental (Tallinn/Pärnu) ─────────────────────
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = trailerCo.Id,
                Type         = ListingType.Trailer,
                Title        = "Haagise rent 750 kg · Tallinn",
                Address      = "Mustamäe tee 55, Tallinn",
                City         = "Tallinn",
                Lat          = tallinnLat,
                Lng          = tallinnLng,
                PriceFrom    = 18m,
                PriceUnit    = "/päev",
                SizeM2       = null,
                Description  = "Kerghaagis 750 kg tõstevõimega. Sobib aiatööde ja väikeste kolimiste jaoks.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.5m,
                ReviewCount  = 11,
            },
            new()
            {
                Id           = Guid.NewGuid(),
                SupplierId   = trailerCo.Id,
                Type         = ListingType.Trailer,
                Title        = "Haagise rent 1500 kg · Pärnu",
                Address      = "Riia mnt 42, Pärnu",
                City         = "Pärnu",
                Lat          = parnuLat,
                Lng          = parnuLng,
                PriceFrom    = 28m,
                PriceUnit    = "/päev",
                SizeM2       = null,
                Description  = "1500 kg suletud haagis suuremate kolimiste jaoks. Asub Pärnus.",
                IsActive     = true,
                AvailableNow = true,
                Rating       = 4.4m,
                ReviewCount  = 6,
            },
        };

        db.Listings.AddRange(listings);
        await db.SaveChangesAsync();

        logger.LogWarning(
            "DemoSeeder: seeded {SupplierCount} suppliers and {ListingCount} listings",
            4, listings.Count);
    }
}
