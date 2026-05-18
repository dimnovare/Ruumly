# Local Development Setup

## Prerequisites
- .NET 8 SDK
- Node.js 20+
- Docker Desktop

## Quick Start

### 1. Start Postgres
```bash
docker run -d --name ruumly-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=ruumly \
  -p 5433:5432 \
  postgres:16

# On subsequent starts:
docker start ruumly-postgres
```

### 2. Create appsettings.Development.json

Create this file in `Ruumly.Backend/` (gitignored — never commit it):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=ruumly;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "Secret": "LocalDevSecretKeyAtLeast32CharsLong!1234",
    "AccessTokenExpiryMinutes": "60",
    "RefreshTokenExpiryDays": "7"
  },
  "AllowedOrigins": ["http://localhost:8080", "http://localhost:3000", "https://ruumly.eu"],
  "Resend": { "ApiKey": "" },
  "GooglePlaces": { "ApiKey": "" },
  "Montonio": { "AccessKey": "", "SecretKey": "" },
  "Cloudflare": { "AccountId": "", "R2AccessKey": "", "R2SecretKey": "", "R2BucketName": "", "R2PublicUrl": "" },
  "Sentry": { "Dsn": "" },
  "AllowedHosts": "*"
}
```

> ⚠️ **`AllowedOrigins` is required.** Without it the CORS policy only includes
> production URLs. The frontend on `localhost:8080` will receive a CORS error
> and show a blank page.

### 3. Apply migrations and run

```bash
cd Ruumly.Backend
dotnet ef database update
dotnet run --launch-profile http
# API → http://localhost:3000
# Swagger → http://localhost:3000/swagger
```

Seed data runs automatically on first startup (suppliers, listings, bookings,
PayoutEntries, Invoices, demo users).

### 4. Start the frontend

```bash
cd estonia-space-hub
# Create .env.local (gitignored):
echo "VITE_API_URL=http://localhost:3000/api" > .env.local
echo "VITE_ENABLE_PAYMENTS=false" >> .env.local
npm run dev
# → http://localhost:8080/et
```

## Seed Accounts

Password for all accounts: **`demo1234`**

| Role     | Email                | Notes                         |
|----------|----------------------|-------------------------------|
| Admin    | peeter@ruumly.eu     | Full admin access             |
| Provider | maria@laopind.ee     | Laobox OÜ — supplier ID sup-1 |
| Customer | andres@email.com     | Has historical bookings       |
| Customer | liina@email.com      | Blocked account (for testing) |

## Testing the Booking Flow Locally

Use **"Pay later"** on the booking page — this skips the Montonio payment
gateway and creates the full Booking → Order → Invoice → PayoutEntry chain
immediately, so you can test the provider dashboard and admin payouts without
a live payment integration.

## Adding a New Migration

```bash
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

Migrations are applied automatically on startup in Development via
`MigrateAsync()`, so re-running `dotnet run` after adding a migration is
sufficient for local testing.
