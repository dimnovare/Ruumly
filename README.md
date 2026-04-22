# Ruumly — Backend API

B2B2C marketplace for warehouse storage, moving services, and trailer rental across the Baltics (Estonia, Latvia, Lithuania).

**Live site:** https://ruumly.eu
**Frontend repo:** [estonia-space-hub](https://github.com/dimnovare/estonia-space-hub)
**Live API:** https://api.ruumly.eu

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | ASP.NET Core 8 (.NET 8) |
| ORM | Entity Framework Core 8 + PostgreSQL |
| Auth | JWT Bearer + HttpOnly refresh cookie rotation + CSRF double-submit, Google OAuth, BCrypt |
| Email | Resend (production), console logger (development) |
| Payments | Montonio (bank links + card + pay-later, all 3 Baltic states) — not yet connected in production |
| Storage | Cloudflare R2 (production), local disk (development) |
| Background jobs | Hangfire + PostgreSQL storage |
| Cache | Redis (production), in-memory (development fallback) |
| Monitoring | Sentry + Serilog |
| API versioning | URL segment + header-based (Asp.Versioning) |
| Deployment | Railway (Docker, europe-west4) |

## Architecture

```
Ruumly.Backend/
├── Controllers/          # 30 API controllers + AdminMappers
├── Services/
│   ├── Interfaces/       # 14 service contracts
│   └── Implementations/  # 18 service implementations
├── Models/               # 23 domain entities + enums
├── DTOs/
│   ├── Requests/         # Inbound request objects
│   └── Responses/        # Outbound response objects
├── Validators/           # 8 FluentValidation rules
├── Jobs/                 # Hangfire background jobs
├── Middleware/            # ExceptionMiddleware, SecurityHeaders, SentryUserContext
├── Helpers/              # Error messages (5-language i18n), email translations, tier rules, token protector
├── Data/                 # RuumlyDbContext, SeedData
├── Migrations/           # EF Core migrations
└── Program.cs            # Composition root

Ruumly.Backend.Tests/     # 20 test files (xUnit + FluentAssertions, EF InMemory provider)
```

## Pricing Model

Ruumly uses a **commission-based freemium model**. There are no trials and no unit limits.

### Onboarding window (90 days)

When an admin activates a supplier (`IsActive` flips to `true`), `OnboardingStartedAt` is set to `DateTime.UtcNow`. For the next 90 days the supplier pays **0% commission and €0 subscription** — zero cost to get started.

The computed property `Supplier.IsInOnboarding` returns `true` while the window is open.

### Post-onboarding tiers

| Tier | Subscription | Commission | Search placement | Analytics |
|------|-------------|------------|-----------------|-----------|
| Starter (free default) | €0/mo | 12% | standard | basic |
| Standard | €49/mo | 8% | boosted | full |
| Premium | €99/mo | 6% | priority | full |

Higher tiers buy down the commission rate and unlock paid features.

### Founding Partner

A permanent flag (`Supplier.FoundingPartner`) granted by admin via `POST /admin/suppliers/{id}/grant-founding-partner`. Benefits:

- **-2% commission** (e.g. Starter pays 10% instead of 12%)
- **20% off subscription** (e.g. Standard pays €39.20 instead of €49)

The flag never expires. It can be revoked via `POST /admin/suppliers/{id}/revoke-founding-partner`.

### Source of truth

`PricingConfigService.GetEffectivePricingAsync(supplier)` computes the final `EffectivePricing(SubscriptionFee, CommissionRate)` for any supplier, accounting for onboarding window, tier, and Founding Partner status. Tier base rates and fee amounts are stored in `PlatformSettings` (key-value table, admin-editable) with compile-time fallback defaults.

## Paid-Tier Features

- **Boosted search placement** — `ListingService.SearchAsync` uses supplier tier as a secondary sort (tiebreaker) in all sort modes. Default sort is tier-descending first.
- **Verified badge** — Admin-granted via `POST /admin/suppliers/{id}/verify`. `IsVerified` is exposed on `ListingDto` and `SupplierDto`. Revoke via `POST /admin/suppliers/{id}/unverify`.
- **Priority inbox** — `OrderService.GetAllAsync` sorts admin queries by `Supplier.PriorityLevel` descending, then by `CreatedAt`. Admin sets priority via `PATCH /admin/suppliers/{id}/priority` (Standard / High / Critical).
- **Featured partner rotation** — `FeaturedPartnersService` returns up to 6 verified Standard+ suppliers at `GET /api/suppliers/featured`. Daily rotation via date-seeded deterministic shuffle. 10-minute cache. Filters: `IsActive && IsVerified && Tier >= Standard`.
- **iCal calendar sync** — Business-tier providers export bookings as `.ics` via `SupplierTeamController`.
- **Full analytics flag** — `GET /api/supplier/stats` includes `hasFullAnalytics: true|false` based on `Tier >= Standard`. No backend gate; the frontend controls what charts Starter sees.

## Getting Started

Prerequisites: .NET 8 SDK, PostgreSQL 15+, Redis (optional — falls back to in-memory).

```bash
git clone https://github.com/dimnovare/Ruumly.git
cd Ruumly/Ruumly.Backend
dotnet restore
```

Configure `appsettings.Development.json` with your local PostgreSQL connection string and JWT secret (see Environment Variables below).

```bash
dotnet ef database update
dotnet run
```

- Swagger: http://localhost:3000/swagger
- Health check: http://localhost:3000/health
- Hangfire dashboard: http://localhost:3000/hangfire (development only)

## Environment Variables

| Variable | Description |
|----------|------------|
| `DATABASE_URL` | PostgreSQL connection string (Railway format or standard) |
| `Jwt:Secret` | HMAC-SHA256 signing key for access/refresh tokens |
| `Jwt:Issuer` | Token issuer claim |
| `Jwt:Audience` | Token audience claim |
| `Jwt:AccessTokenExpiryMinutes` | Access token TTL (default: 15) |
| `Jwt:RefreshTokenExpiryDays` | Refresh token TTL (default: 7) |
| `Google:ClientId` | Google OAuth client ID |
| `Resend:ApiKey` | Resend email API key |
| `Email:FromName` | Sender display name (default: "Ruumly") |
| `Email:FromAddress` | Sender address (default: noreply@ruumly.eu) |
| `Montonio:AccessKey` | Montonio payment access key |
| `Montonio:SecretKey` | Montonio payment secret key |
| `Montonio:ApiUrl` | Montonio API base URL |
| `Montonio:ReturnUrl` | Payment return redirect URL |
| `Montonio:NotifyUrl` | Payment webhook callback URL |
| `Storage:R2AccountId` | Cloudflare R2 account ID |
| `Storage:R2AccessKey` | Cloudflare R2 access key |
| `Storage:R2SecretKey` | Cloudflare R2 secret key |
| `Storage:R2BucketName` | R2 bucket name |
| `Storage:R2PublicUrl` | R2 public URL prefix |
| `Storage:BasePath` | Local upload path (development, default: /app/uploads) |
| `Storage:BaseUrl` | Local upload URL prefix (development) |
| `Sentry:Dsn` | Sentry DSN for error tracking |
| `REDIS_URL` | Redis connection string (optional) |
| `AppUrl` | Frontend base URL (used in emails and notifications) |
| `PORT` | HTTP listen port (default: 3000) |

On Railway, use `__` (double underscore) as the section separator: `JWT__SECRET`, `GOOGLE__CLIENTID`, `STORAGE__R2ACCOUNTID`, etc.

## Running Tests

```bash
dotnet test
```

20 test files covering auth, booking pricing, overlap detection, payment flows, freemium pricing, tier differentiation, lead CRM, sitemap, robots.txt, and tier rules. Tests use EF Core InMemory provider via `TestDbContext`.

## Background Jobs

| Job | Schedule | Description |
|-----|----------|------------|
| `expire-reservations` | Every 15 min | Cancels `Reserved` bookings past `ReservedUntil` |
| `stale-booking-cleanup` | Hourly | Cancels `Pending` bookings older than 24h that do not have a paid invoice |
| `abandoned-booking-reminder` | Every 15 min | Sends reminder notifications for abandoned bookings |
| `cleanup-tokens` | Daily | Removes expired refresh tokens |

All jobs are registered as Hangfire recurring jobs in `Program.cs`.

## Admin Endpoints of Note

| Endpoint | Description |
|----------|------------|
| `POST /admin/suppliers/{id}/grant-founding-partner` | Grants permanent Founding Partner status |
| `POST /admin/suppliers/{id}/revoke-founding-partner` | Revokes Founding Partner status |
| `POST /admin/suppliers/{id}/verify` | Grants verified badge (KYC-confirmed) |
| `POST /admin/suppliers/{id}/unverify` | Removes verified badge |
| `PATCH /admin/suppliers/{id}/priority` | Sets PriorityLevel (Standard / High / Critical) |
| `PATCH /admin/suppliers/{id}/tier` | Changes supplier tier (Starter / Standard / Premium) |
| `PATCH /admin/suppliers/{id}/status` | Activates/deactivates supplier (triggers onboarding on first activation) |

## Deployment

Railway auto-deploys on push to `master`. The Docker build uses a two-stage `Dockerfile` (SDK build + ASP.NET runtime). Migrations run automatically on startup via `db.Database.MigrateAsync()`.

- Health check: `GET /health` (returns JSON with status and individual check durations)
- Hangfire dashboard is disabled in production

## SEO Endpoints

- **`GET /sitemap.xml`** — Dynamic XML sitemap with hreflang alternates for 5 languages (et, en, ru, lv, lt) plus x-default. All hreflang hrefs use the canonical URL (no `?lang=` query parameters). Includes static pages, active listings, locations, and city landing pages.
- **`GET /robots.txt`** — Single spec-compliant `User-agent: *` block with path disallows, AI training crawler blocks (Amazonbot, GPTBot, ClaudeBot, CCBot, etc.), and `Sitemap:` directive. Cloudflare's managed robots.txt injection is disabled to prevent duplicate `User-agent: *` blocks.

Vercel rewrites `/sitemap.xml` and `/robots.txt` from the apex `ruumly.eu` domain to this backend.

## Contributing

- Claude Code is used for backend development prompts.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).
- Run `dotnet test` before pushing. All tests must pass.
- One feature per commit.

## License

Proprietary. Copyright &copy; 2026 Ruumly OÜ. All rights reserved.
