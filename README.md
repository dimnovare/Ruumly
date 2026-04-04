# Ruumly — Backend API

B2B2C marketplace platform for warehouse storage, moving services, and trailer rental across the Baltics (Estonia, Latvia, Lithuania).

**Frontend repo:** [estonia-space-hub](https://github.com/dimnovare/estonia-space-hub)
**Live API:** https://api.ruumly.eu
**Live site:** https://ruumly.eu

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | ASP.NET Core 8 (.NET 8) |
| ORM | Entity Framework Core 8 + PostgreSQL |
| Auth | JWT Bearer + HttpOnly refresh cookie rotation + CSRF double-submit, Google OAuth, BCrypt |
| Email | Resend (production), console logger (development) |
| Payments | Montonio (bank links + card + pay-later, all 3 Baltic states) |
| Storage | Cloudflare R2 (production), local disk (development) |
| Background jobs | Hangfire + PostgreSQL storage |
| Cache | Redis (production), in-memory (development fallback) |
| Monitoring | Sentry + Serilog |
| API versioning | URL segment + header-based (Asp.Versioning) |
| Deployment | Railway (Docker, europe-west4) |

## Architecture

Ruumly.Backend/
├── Controllers/          # 29 API controllers
├── Services/
│   ├── Interfaces/       # 13 service contracts
│   └── Implementations/  # 17 service implementations
├── Models/               # 22 domain entities + enums
├── DTOs/                 # Request/response objects
├── Validators/           # 8 FluentValidation rules
├── Jobs/                 # Hangfire background jobs
├── Middleware/            # Exception handling, security headers, Sentry context
├── Helpers/              # Error messages (5-language i18n), email translations, tier rules
├── Data/                 # DbContext, seed data
└── Migrations/           # 37 EF Core migrations

Ruumly.Backend.Tests/     # 12 test files

## Key Features

- Tier-based subscriptions — Starter €19/mo (3 units), Growth €49/mo (10 units), Business €99/mo (30 units). 30-day free trial.
- Pricing engine — Duration-based calculation, per-supplier partner discount with per-listing override, guaranteed minimum margin.
- Order routing & dispatch — API/email/manual channels with custom JSON payload templates per supplier and automatic fallback.
- Booking integrity — Idempotency key + pg_advisory_xact_lock for concurrent overlap detection.
- Payment — Montonio bank/card/pay-later across all 3 Baltic states. JWT-verified webhook.
- Security — HttpOnly refresh cookies + CSRF, rate limiting, CSP/HSTS headers, GDPR deletion + export.
- Multi-country — Per-country VAT (EE 24%, LV 21%, LT 21%). Dynamic city lists.
- Background jobs — Order dispatch, confirmation emails, stale booking cleanup, token cleanup.
- iCal export — Business-tier providers export bookings as .ics.
- SEO — Dynamic XML sitemap with hreflang (5 languages), robots.txt.

## Local Development

Prerequisites: .NET 8 SDK, PostgreSQL 15+, Redis (optional).

```
cd Ruumly.Backend
dotnet restore
dotnet ef database update
dotnet run
```

Swagger: http://localhost:3000/swagger
Health: http://localhost:3000/health
Hangfire: http://localhost:3000/hangfire (dev only)

## Environment Variables (Railway)

DATABASE_URL, JWT__SECRET, GOOGLE__CLIENTID, RESEND__APIKEY, MONTONIO__ACCESSKEY, MONTONIO__SECRETKEY, SENTRY__DSN, STORAGE__R2ACCOUNTID, STORAGE__R2ACCESSKEY, STORAGE__R2SECRETKEY, STORAGE__R2BUCKETNAME, STORAGE__R2PUBLICURL, REDIS_URL, APP_URL

## Testing

```
cd Ruumly.Backend.Tests
dotnet test
```

12 test files covering auth, booking, pricing, overlap, deletion, tiers.

## Deployment

Railway auto-builds from Dockerfile on push to master. Migrations run on startup.

## License

Proprietary. All rights reserved.
