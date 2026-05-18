# Local Development Setup

## Prerequisites
- .NET 8 SDK
- Node.js 20+
- Docker Desktop (for Postgres)

## Quick Start

### 1. Start Postgres
```bash
docker run -d --name ruumly-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=ruumly \
  -p 5433:5432 postgres:16

# On subsequent starts:
docker start ruumly-postgres
```

### 2. Create appsettings.Development.json
Create this file in `Ruumly.Backend/` (gitignored — never commit):

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

> ⚠️ **`AllowedOrigins` array is REQUIRED.**
> Without it CORS blocks `localhost:8080` and the frontend shows a blank page.

### 3. Start backend
```bash
cd Ruumly.Backend
dotnet run --launch-profile http
# → http://localhost:3000
# → Swagger: http://localhost:3000/swagger
```

Migrations and seed data run automatically on first startup.

### 4. Start frontend
Create `estonia-space-hub/.env.local` (gitignored):

```
VITE_API_URL=http://localhost:3000/api
VITE_ENABLE_PAYMENTS=false
```

Then:
```bash
cd estonia-space-hub
npm run dev
# → http://localhost:8080/et
```

## Test Accounts  (password: demo1234)
| Role     | Email                  |
|----------|------------------------|
| Admin    | peeter@ruumly.eu       |
| Provider | maria@laopind.ee       |
| Customer | andres@email.com       |
| Customer | liina@email.com (blocked) |

## Testing Booking Flow Locally
Select **"Pay later"** on the booking page — skips Montonio entirely and
creates the full Booking → Order → Invoice → PayoutEntry chain without
a payment gateway.

## Adding a New Migration
```bash
dotnet ef migrations add <MigrationName>
dotnet ef database update
```
