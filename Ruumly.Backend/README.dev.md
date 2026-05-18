# Local Development Setup

## appsettings.Development.json

Create `appsettings.Development.json` in `Ruumly.Backend/` (gitignored — do not commit):

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

> **The `AllowedOrigins` array is required.** Without it the CORS policy only
> uses the database platform settings which contain only the production URL.
> The frontend on localhost:8080 will receive a CORS error and show a blank page.

## Start order

1. `docker start ruumly-postgres` — PostgreSQL on port 5433
2. `dotnet run --launch-profile http` — API on port 3000
3. `npm run dev` — Frontend on port 8080 (in `estonia-space-hub/`)

## Database

```bash
# Apply all pending migrations
dotnet ef database update

# Add a new migration
dotnet ef migrations add <MigrationName>
```

## Seeding

`SeedData.SeedAsync(db)` runs automatically on startup in the Development environment. It creates:
- 3 suppliers, locations, listings
- Historical bookings and orders
- PayoutEntries and Invoices (seeded from historical orders)
- Demo users (see main README for credentials)
