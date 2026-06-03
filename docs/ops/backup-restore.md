# Backup & Restore — Ruumly

## Database (PostgreSQL on Railway)

Railway creates automated backups. To access:
1. Go to Railway dashboard → your PostgreSQL service → Backups tab
2. Select a backup → Restore (this replaces the running DB)

## Manual backup (before risky migrations)
```bash
railway run pg_dump $DATABASE_URL > backup_$(date +%Y%m%d_%H%M%S).sql
```

## Restore from manual backup
```bash
railway run psql $DATABASE_URL < backup_YYYYMMDD_HHMMSS.sql
```

## Verification after restore
- Check booking count: `SELECT COUNT(*) FROM "Bookings";`
- Check latest booking: `SELECT MAX("CreatedAt") FROM "Bookings";`
- Smoke-test the app: browse home page, search, admin login

## Key tables (EF Core model → PostgreSQL table name)
| Model | Table |
|---|---|
| `Booking` | `Bookings` |
| `Invoice` | `Invoices` |
| `Order` | `Orders` |
| `SignedContract` | `SignedContracts` |
| `SupplierLocation` | `SupplierLocations` |
| `Supplier` | `Suppliers` |
| `User` | `Users` |

All table names use double-quoted Pascal case because EF Core maps class names directly
(e.g. `Db.Bookings` → table `"Bookings"`).
