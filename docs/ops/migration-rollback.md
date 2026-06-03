# EF Core Migration Rollback — Ruumly

## Before any migration in production
1. Take a manual DB backup (see backup-restore.md)
2. Note the current migration: `dotnet ef migrations list --connection "..."` (last Applied entry)

## Roll back one migration
```bash
dotnet ef database update <PreviousMigrationName> --connection "Host=...;Port=5432;Database=ruumly;Username=...;Password=..."
```
Replace `<PreviousMigrationName>` with the migration name just before the one you want to undo.

## Find the previous migration name
```bash
dotnet ef migrations list --connection "..."
```
The Applied list is in order — the target is the one above the one you want to remove.

## Warning
- Rollback only works if the migration was non-destructive (added columns/tables). Dropping
  columns/tables cannot be rolled back without a data restore.
- Always test rollback on a staging DB before production.
- The local Docker DB is on port 5433:
  `Host=localhost;Port=5433;Database=ruumly;Username=postgres;Password=postgres`

## Full connection string for Railway production
Retrieve via: `railway variables` or the Railway dashboard → Variables tab.
The `DATABASE_URL` variable contains the Npgsql-compatible connection string.

## EF Core CLI — run from Ruumly.Backend/
```bash
# List migrations with applied status
dotnet ef migrations list --connection "Host=localhost;Port=5433;Database=ruumly;Username=postgres;Password=postgres"

# Roll back to a specific migration
dotnet ef database update AddDokobitSigningFields --connection "Host=localhost;Port=5433;Database=ruumly;Username=postgres;Password=postgres"

# Apply all pending migrations
dotnet ef database update --connection "Host=localhost;Port=5433;Database=ruumly;Username=postgres;Password=postgres"
```
