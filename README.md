# Ruumly — Backend API

Marketplace platform for warehouse storage, moving services, and trailer rental in Estonia.

**Frontend repo:** [estonia-space-hub](https://github.com/dimnovare/Ruumly)
**Live API:** https://api.ruumly.eu
**Live site:** https://ruumly.eu

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | ASP.NET Core 8 (.NET 8) |
| ORM | Entity Framework Core 8 + PostgreSQL |
| Auth | JWT Bearer + refresh token rotation, Google OAuth, BCrypt (workFactor 12) |
| Email | Resend (production), console logger (development) |
| Payments | Montonio (Estonian bank links + card) |
| Storage | Cloudflare R2 (production), local disk (development) |
| Background jobs | Hangfire + PostgreSQL storage |
| Cache | Redis (production), in-memory (development fallback) |
| Monitoring | Sentry + Serilog |
| Deployment | Railway (Docker) |

## Architecture

```
Ruumly.Backend/
├── Controllers/          # 23 API controllers (auth, bookings, admin, payments, etc.)
├── Services/
│   ├── Interfaces/       # 12 service contracts
│   └── Implementations/  # 16 service implementations
├── Models/               # 18 domain entities
├── DTOs/                 # Request/response objects
├── Validators/           # FluentValidation rules
├── Middleware/            # Exception, security headers, Sentry context
├── Helpers/              # Error messages (i18n), tier rules, extensions
├── Data/                 # DbContext, seed data
└── Migrations/           # 21 EF Core migrations

Ruumly.Backend.Tests/     # 43 unit tests (xUnit + FluentAssertions)
```

## Key Features

- **Tier-based pricing** — Starter (free), Standard (€49/mo), Premium (€99/mo). Per-supplier customer discount derived from negotiated partner rate. All rates configurable via admin panel (PlatformSettings).
- **Order routing** — bookings create orders that are dispatched to suppliers via API, email, or manual channel with automatic fallback (API fails → email → manual + admin notification).
- **Email verification** — registration sends a verification email; unverified users cannot book. Google OAuth auto-verifies.
- **Refund flow** — admin can initiate refunds (marks invoice as PendingRefund, notifies customer, audit logged).
- **Background jobs** — Hangfire processes order dispatch and booking confirmation emails asynchronously. Daily cron cleans up stale refresh tokens.
- **Full-text search** — PostgreSQL tsvector on listings with GIN index.
- **Soft deletes** — bookings and orders support soft delete with global query filters.
- **Security** — rate limiting (auth/search/upload), CSP + HSTS + security headers, timing-safe login, 500 errors never expose internals.

## Local Development

### Prerequisites

- .NET 8 SDK
- PostgreSQL 15+ (default port 5432)
- Node.js 20+ (for frontend)

### Setup

```bash
cd Ruumly.Backend
dotnet restore
# Set connection string in appsettings.Development.json or environment
dotnet ef database update   # applies all 21 migrations + seeds demo data
dotnet run                  # starts on http://localhost:3000
```

- Swagger UI: http://localhost:3000/swagger
- Health check: http://localhost:3000/health
- Hangfire dashboard: http://localhost:3000/hangfire (dev only)

### Environment Variables (Railway)

| Variable | Description |
|----------|-------------|
| `DATABASE_URL` | PostgreSQL connection URI (auto-injected by Railway) |
| `JWT__SECRET` | Min 32-char JWT signing key |
| `GOOGLE__CLIENTID` | Google OAuth client ID |
| `RESEND__APIKEY` | Resend email API key |
| `MONTONIO__ACCESSKEY` | Montonio payment access key |
| `MONTONIO__SECRETKEY` | Montonio payment secret key |
| `SENTRY__DSN` | Sentry error tracking DSN |
| `STORAGE__R2ACCOUNTID` | Cloudflare R2 account ID |
| `STORAGE__R2ACCESSKEY` | R2 access key |
| `STORAGE__R2SECRETKEY` | R2 secret key |
| `STORAGE__R2BUCKETNAME` | R2 bucket name |
| `STORAGE__R2PUBLICURL` | R2 public URL for images |
| `REDIS_URL` | Redis connection (optional, falls back to in-memory) |

## API Endpoints

### Public
| Method | Path | Description |
|--------|------|-------------|
| POST | /api/auth/register | Register + sends verification email |
| POST | /api/auth/login | Email/password login |
| POST | /api/auth/google | Google OAuth login |
| POST | /api/auth/refresh | Rotate refresh token |
| POST | /api/auth/verify-email | Verify email with token |
| POST | /api/auth/forgot-password | Send password reset link |
| POST | /api/auth/reset-password | Apply reset token |
| GET | /api/listings | Search listings (filters, pagination, full-text) |
| GET | /api/listings/featured | Featured listings (badged) |
| GET | /api/listings/{id} | Listing detail |
| GET | /api/locations | Supplier locations |
| GET | /api/settings/public | Public site settings |
| GET | /api/bookings/extras-config | Extras pricing |
| GET | /api/bookings/stats | Booking stats (cached) |
| GET | /sitemap.xml | Dynamic XML sitemap |

### Authenticated (Customer/Provider)
| Method | Path | Description |
|--------|------|-------------|
| POST | /api/bookings | Create booking (requires verified email) |
| GET | /api/bookings | List own bookings (paginated) |
| POST | /api/bookings/{id}/cancel | Cancel booking |
| POST | /api/payments/initiate | Start Montonio payment |
| POST | /api/payments/webhook | Montonio payment callback |
| GET | /api/orders | Provider order list |
| POST | /api/orders/{id}/confirm | Confirm order |
| GET | /api/messages | Booking messages |
| POST | /api/messages | Send message |
| POST | /api/reviews | Submit review |
| GET | /api/supplier/team | Provider team members |
| POST | /api/supplier/team/invite | Invite team member |

### Admin
| Method | Path | Description |
|--------|------|-------------|
| GET | /api/admin/users | Paginated user list |
| GET | /api/admin/suppliers | Paginated supplier list |
| GET | /api/admin/dashboard/stats | Revenue + booking metrics |
| POST | /api/admin/bookings/{id}/refund | Initiate refund |
| GET | /api/admin/integrations | Integration settings |
| GET | /api/admin/routing-rules | Order routing rules |
| GET | /api/admin/audit-log | Audit trail |
| PATCH | /api/admin/suppliers/{id}/tier | Change supplier tier |

Full reference at `/swagger` (development only).

## User Roles

| Role | Description |
|------|-------------|
| Customer | Browse, book, review, manage own bookings and messages |
| Provider | Manage listings, view incoming orders, team management, analytics |
| Admin | Full platform access — users, suppliers, settings, routing, refunds, audit |

## Testing

```bash
cd Ruumly.Backend.Tests
dotnet test
```

43 tests covering: auth (registration, login, refresh, invite codes), booking creation and pricing, listing search and pagination, pricing consistency across tiers, tier rules, error message i18n, and role-based access scoping.

## Deployment

Railway auto-builds from the Dockerfile. Migrations run automatically on startup in production. Health check at `/health` is configured in `railway.json`.

## License

Proprietary. All rights reserved.
