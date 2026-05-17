# Ruumly — Backend API

ASP.NET Core 8 REST API powering the Ruumly marketplace platform. Handles authentication, listings, bookings, payments, supplier integrations, and the full admin/provider back-office.

---

## Table of Contents

1. [Technology Stack](#technology-stack)
2. [Project Structure](#project-structure)
3. [Getting Started](#getting-started)
4. [Configuration Reference](#configuration-reference)
5. [Database](#database)
6. [Authentication & Authorisation](#authentication--authorisation)
7. [API Overview](#api-overview)
8. [Background Jobs](#background-jobs)
9. [Security](#security)
10. [Logging & Observability](#logging--observability)
11. [Health Checks](#health-checks)
12. [Development Notes](#development-notes)

---

## Technology Stack

| Layer | Technology | Version |
|---|---|---|
| Runtime | .NET / ASP.NET Core | 8.0 |
| ORM | Entity Framework Core + Npgsql | 8.0.11 |
| Database | PostgreSQL | any recent |
| Cache | Redis (StackExchange) | 8.x |
| Background jobs | Hangfire + Hangfire.PostgreSql | 1.8.14 |
| Auth | JWT Bearer + BCrypt.Net | — |
| Google OAuth | Google.Apis.Auth | 1.73.0 |
| Email | Resend (production) / console (dev) | 0.2.2 |
| Payments | Montonio | REST |
| Object storage | Cloudflare R2 / AWS S3 SDK | 3.7.x |
| Image processing | SixLabors.ImageSharp | 3.1.12 |
| Validation | FluentValidation.AspNetCore | 11.3.0 |
| API versioning | Asp.Versioning.Mvc | 8.1.0 |
| API docs | Swashbuckle / Swagger | 6.6.2 |
| Logging | Serilog (console + rolling file) | 10.0.0 |
| Error tracking | Sentry.AspNetCore | 5.10.0 |

---

## Project Structure

```
Ruumly.Backend/
├── Controllers/          # 32 API controllers (see API Overview)
│   ├── Admin*            # Admin-only endpoints
│   ├── Provider*         # Provider-scoped endpoints
│   └── ...               # Public / authenticated endpoints
├── Data/
│   └── RuumlyDbContext.cs
├── DTOs/
│   ├── Requests/         # Inbound request models
│   ├── Responses/        # Outbound response models
│   └── PaginatedResult.cs
├── Helpers/
│   ├── AdminMappers.cs
│   ├── EmailTranslations.cs
│   ├── ErrorMessages.cs  # Localised error strings (et/en/ru/lv/lt)
│   └── ...
├── Jobs/                 # Hangfire recurring jobs (3)
├── Middleware/           # ExceptionMiddleware, SecurityHeaders, SentryUserContext
├── Migrations/           # 54 EF Core migrations
├── Models/               # 26 domain entities + Enums
├── Services/
│   ├── Interfaces/       # 18 service interfaces
│   └── Implementations/  # Concrete service implementations
├── Validators/           # 10 FluentValidation validators
├── appsettings.json
├── appsettings.Development.json   # local only — not tracked in git
└── Program.cs
```

### Domain Models (27 DbSets)

| Entity | Description |
|---|---|
| `User` | Platform user (Customer / Provider / Admin) |
| `RefreshToken` | JWT refresh token |
| `Supplier` | Supplier / service provider company |
| `IntegrationSettings` | Per-supplier external system config |
| `Listing` | Bookable service listing |
| `ListingExtra` | Optional add-ons for a listing |
| `SupplierLocation` | Physical location managed by a supplier |
| `BlockedDate` | Calendar blackout for a location |
| `Booking` | Customer booking record |
| `BookingTimeline` | Status-change audit trail for bookings |
| `Order` | Fulfilment order dispatched to a supplier |
| `OrderTimeline` | Status-change audit trail for orders |
| `FulfillmentEvent` | Webhook events from external systems |
| `Invoice` | Billing invoice tied to a booking |
| `PayoutEntry` | Pending / settled supplier payout |
| `RebateInvoice` | Monthly rebate invoice per supplier |
| `Review` | Customer review |
| `Message` | In-app messaging (booking-scoped) |
| `Notification` | In-app notification |
| `OrderRoutingRule` | Rule-based order routing config |
| `ContractTemplate` | HTML contract template |
| `SignedContract` | Signed contract snapshot |
| `PlatformSetting` | Key-value runtime configuration |
| `AuditLog` | Admin action audit log |
| `PollingLog` | Supplier API polling run log |
| `DataProtectionKey` | ASP.NET Data Protection keys (DB-persisted) |

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- PostgreSQL 14+
- Redis (optional in development — falls back to in-memory cache)
- A Resend API key (or use the dev console logger)

### Installation

```bash
git clone https://github.com/dimnovare/Ruumly.git
cd Ruumly/Ruumly.Backend

# Restore packages
dotnet restore

# Apply migrations
dotnet ef database update

# Run
dotnet run
```

The API starts on **port 3000** by default (overridable via `PORT` env var).

Swagger UI is available at `http://localhost:3000/swagger` in Development mode.

### User Secrets (Development)

Sensitive values are kept out of source control using the .NET Secret Manager:

```bash
dotnet user-secrets set "Jwt:Secret" "<your-secret>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=ruumly;..."
dotnet user-secrets set "Resend:ApiKey" "re_..."
dotnet user-secrets set "Google:ClientId" "..."
```

The project's user-secrets ID is `9f85d24a-8056-4c58-806b-d5e74d6f8448`.

---

## Configuration Reference

All keys live in `appsettings.json`; environment-specific overrides go in `appsettings.Development.json` (not tracked) or environment variables.

| Key | Description | Default |
|---|---|---|
| `Jwt:Secret` | HS256 signing key — **required** | — |
| `Jwt:Issuer` | Token issuer | `ruumly-api` |
| `Jwt:Audience` | Token audience | `ruumly-frontend` |
| `Jwt:AccessTokenExpiryMinutes` | Access token lifetime | `15` |
| `Jwt:RefreshTokenExpiryDays` | Refresh token lifetime | `7` |
| `ConnectionStrings:DefaultConnection` | Npgsql connection string | — |
| `DATABASE_URL` | Railway postgres:// URI (takes priority over above) | — |
| `REDIS_URL` | Redis connection string (production) | — |
| `Resend:ApiKey` | Resend transactional email API key | — |
| `Google:ClientId` | Google OAuth client ID | — |
| `Montonio:AccessKey` | Montonio payment access key | — |
| `Montonio:SecretKey` | Montonio payment secret key | — |
| `Montonio:ApiUrl` | Montonio API base URL | `https://api.montonio.com` |
| `Montonio:ReturnUrl` | Payment success/cancel redirect | `https://ruumly.eu/payment/return` |
| `Montonio:NotifyUrl` | Payment webhook receiver | `https://api.ruumly.eu/api/payments/webhook` |
| `Storage:R2AccountId` | Cloudflare R2 account ID | — |
| `Storage:R2AccessKey` | R2 access key | — |
| `Storage:R2SecretKey` | R2 secret key | — |
| `Storage:R2BucketName` | R2 bucket name | — |
| `Storage:R2PublicUrl` | Public CDN base URL for stored files | — |
| `Sentry:Dsn` | Sentry DSN (SDK disabled if absent) | — |
| `GooglePlaces:ApiKey` | Google Places API key | — |
| `Platform:FeePercent` | Ruumly platform fee percentage | `5.0` |
| `AppUrl` | Public frontend URL | `https://ruumly.eu` |
| `AllowedHosts` | ASP.NET Host header whitelist | `ruumly.eu;www.ruumly.eu;api.ruumly.eu` |
| `AllowedOrigins` | Additional CORS allowed origins | localhost dev URLs + ruumly.eu |
| `PORT` | HTTP listen port | `3000` |

---

## Database

### Migrations

EF Core migrations are in `Migrations/`. There are currently **54 migrations** spanning from `InitialCreate` (March 2026) through `AddListingCityTrigram` (May 2026).

```bash
# Apply all pending migrations
dotnet ef database update

# Add a new migration
dotnet ef migrations add <MigrationName>

# Roll back one migration
dotnet ef database update <PreviousMigrationName>
```

### PostgreSQL Extensions

The following extensions are enabled automatically on startup:

| Extension | Purpose |
|---|---|
| `unaccent` | Accent-insensitive full-text search |
| `pg_trgm` | Trigram similarity for `ILIKE` index on `Listing.City` |

Both are also enabled in the `AddListingCityTrigram` migration via `CREATE EXTENSION IF NOT EXISTS`.

### Notable Indexes

| Table | Column(s) | Type | Purpose |
|---|---|---|---|
| `Listings` | `SearchVector` | GIN (tsvector) | Full-text search |
| `Listings` | `City` | GIN trigram | `ILIKE` city filter |
| `Bookings` | `SupplierId` | B-tree | Supplier booking queries |
| `Bookings` | `UserId` | B-tree | User booking queries |

---

## Authentication & Authorisation

### JWT Flow

1. `POST /api/auth/login` or `POST /api/auth/register` → returns `accessToken` (15 min) + `refreshToken` (7 days, HTTP-only cookie or body).
2. `POST /api/auth/refresh` → rotates refresh token, issues new access token.
3. `POST /api/auth/logout` → revokes refresh token.

Access tokens are signed with HS256. Clock skew is set to zero — tokens expire exactly at their stated time.

### Roles

| Role | Access |
|---|---|
| `Customer` | Bookings, invoices, messages, notifications, account |
| `Provider` | Above + provider dashboard, listings, locations, orders, extras, team |
| `Admin` | Full access including all admin controllers |

### Google OAuth

`POST /api/auth/google` — validates a Google ID token server-side with `Google.Apis.Auth`. On first sign-in a User record is created automatically.

---

## API Overview

All endpoints are prefixed `/api/`. JWT is required unless noted otherwise.

### Auth (`/api/auth/`)
`register`, `login`, `refresh`, `logout`, `google`, `verify-email`, `resend-verification`, `forgot-password`, `reset-password`, `change-password`

### Listings (`/api/listings/`)
Search (public), detail (public), create/update/delete (Admin), extras (public GET, Provider/Admin mutate)

### Bookings (`/api/bookings/`)
Create, get, cancel, list (Customer/Provider/Admin scoped)

### Orders (`/api/orders/`)
List, approve/reject, mark-complete, dispatch (Provider/Admin)

### Invoices (`/api/invoices/`)
Get by booking, list all (role-scoped), generate, mark-paid

### Payments (`/api/payments/`)
Initiate (Montonio), webhook receiver (unauthenticated), return handler

### Locations (`/api/locations/`)
List (public), get by ID (public), CRUD (Provider/Admin)

### Suppliers (`/api/suppliers/`)
Public profile, apply (authenticated), partner page, team management

### Messages (`/api/messages/`)
Send, list by booking (booking participants only)

### Notifications (`/api/notifications/`)
List, mark-read, mark-all-read

### Reviews (`/api/reviews/`)
Create (Customer, post-booking), list by listing (public)

### Contracts (`/api/contracts/`)
List templates (public), sign, get signed PDF (Admin)

### Settings (`/api/settings/`)
Public platform settings; Provider bank details; Provider partner-page config

### Provider (`/api/provider/`)
Stats, incoming orders, bank details, partner page

### Sitemap & Robots (`/sitemap.xml`, `/robots.txt`)
Dynamically generated

### Admin (`/api/admin/`)
Full CRUD for users, suppliers, listings, locations, orders, payouts, rebates, refunds, routing rules, integration settings, platform settings, audit log, dashboard stats

---

## Background Jobs

Jobs are scheduled via **Hangfire** with PostgreSQL storage. The dashboard is available at `/hangfire` in Development mode.

| Job | Schedule | Description |
|---|---|---|
| `cleanup-tokens` | Daily | Remove expired refresh tokens |
| `stale-booking-cleanup` | Hourly | Cancel unpaid bookings past their reservation window |
| `expire-reservations` | Every 15 min | Release seats/slots held by abandoned bookings |
| `abandoned-booking-reminder` | Every 15 min | Email reminder to users with incomplete bookings |
| `supplier-polling-dispatcher` | Every 5 min | Poll supplier APIs for availability updates (up to 3 concurrent) |
| `prune-polling-logs` | Weekly | Delete old supplier polling log entries |

---

## Security

| Control | Implementation |
|---|---|
| Proxy trust | `UseForwardedHeaders` with full Cloudflare→Railway chain trust; real IP from `CF-Connecting-IP` |
| Rate limiting | Fixed-window, partitioned by IP (public) or User+IP (authenticated); 429 + `Retry-After` on breach |
| SSRF protection | `IsAllowedEndpoint()` blocks private RFC-1918, loopback, link-local, and non-HTTP(S) URLs before any outbound supplier API call |
| CORS | Exact origin whitelist + controlled Vercel preview slug pattern; no wildcard |
| Password hashing | BCrypt with per-user salt |
| JWT | HS256, 15-min access token, clock skew = 0 |
| Data Protection | Keys persisted to DB, application-name scoped |
| Input validation | `[MaxLength]` / `[Range]` DataAnnotations on all DTOs + FluentValidation for complex rules |
| Search safety | tsquery special characters stripped before `PlainToTsQuery`; `EF.Functions.ILike` for city filter |
| Sentry PII | User email replaced with 12-char SHA-256 hex prefix — correlatable but not reversible |
| Security headers | `SecurityHeadersMiddleware` sets `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, and a `Content-Security-Policy` |
| Audit trail | Every admin mutation queued in `AuditLogs` and committed atomically with the entity change |
| Supplier FK guard | Admin user-update validates supplier existence before assigning `SupplierId` |

---

## Logging & Observability

**Serilog** is the logging backend.

- **Development:** Console (pretty-printed) + rolling daily file (`logs/ruumly-YYYYMMDD.log`, 14-day retention).
- **Production:** Console only (stdout → Railway log drain).
- EF Core `Database.Command` log level is raised to `Warning` in all environments to suppress SQL query spam.

**Sentry** is initialised when `Sentry:Dsn` is present:
- `TracesSampleRate = 0.1` (10 % of requests sampled for performance).
- `MinimumEventLevel = Error`.
- User context enriched by `SentryUserContextMiddleware` with a non-reversible user identifier.

---

## Health Checks

| Endpoint | Auth | Response |
|---|---|---|
| `GET /health` | Public | `200 ok` / `503 unhealthy` (plain text) |
| `GET /health/details` | Admin role | JSON with check names, statuses, and durations |

Checks registered: **PostgreSQL** (`AspNetCore.HealthChecks.NpgSql`) and **Hangfire** (`AspNetCore.HealthChecks.Hangfire`).

---

## Development Notes

### Seeding

- `SeedData.SeedAsync(db)` runs automatically in Development on startup.
- `DemoSeeder.SeedIfRequestedAsync(db, config, logger)` runs in all environments when a specific config flag is set.

### Storage

In Development, files are saved to the local filesystem at `Storage:BasePath` and served as static files at `/uploads`. In Production, files go to Cloudflare R2 (S3-compatible).

### Email

In Development, all outbound email is written to the console (`DevConsoleEmailSender`). No Resend account is required to run locally.

### Code Style

- C# 12 primary constructors throughout.
- File-scoped namespaces.
- Positional `record` types for DTOs.
- `Audit()` is `void` — it queues an `AuditLog` row that is committed atomically in the caller's `SaveChangesAsync()`.
- All controller projection queries use `.Select(u => new { ... })` rather than loading full entities when only a subset of fields is needed.
