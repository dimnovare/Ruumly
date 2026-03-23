# Ruumly

Storage, moving, and trailer booking platform for Estonia.

## Stack

| Layer | Technology |
|-------|-----------|
| Backend | ASP.NET Core 8, EF Core 8, PostgreSQL |
| Frontend | React 18, Vite, TypeScript, Tailwind CSS, shadcn/ui |
| Auth | JWT Bearer + refresh tokens, Google OAuth (ID token) |
| Email | Resend (production), console logger (development) |
| Deployment | Railway (backend), Vercel (frontend) |

## Repos

```
Ruumly/
├── Ruumly.Backend/       # ASP.NET Core Web API
└── estonia-space-hub/    # React + Vite frontend
```

## Local development

### Prerequisites

- .NET 8 SDK
- Node.js 20+
- PostgreSQL (default port 5433 for local dev)

### Backend

```bash
cd Ruumly.Backend
dotnet restore
dotnet ef database update   # applies migrations + seeds data
dotnet run                  # starts on http://localhost:3000
```

Swagger UI: `http://localhost:3000/swagger`
Health check: `http://localhost:3000/health`

**Environment / secrets** — copy `appsettings.Development.json` and fill in:

| Key | Description |
|-----|-------------|
| `ConnectionStrings:DefaultConnection` | Postgres connection string |
| `Jwt:Secret` | Min 32-char signing key |
| `Google:ClientId` | OAuth client ID (Google Cloud Console) |
| `Resend:ApiKey` | Only needed in production |

### Frontend

```bash
cd estonia-space-hub
npm install
npm run dev     # starts on http://localhost:5173
```

Create `estonia-space-hub/.env.local`:

```
VITE_API_URL=http://localhost:3000
VITE_GOOGLE_CLIENT_ID=<your-google-client-id>
```

## Key API endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/auth/register` | — | Register new account |
| `POST` | `/api/auth/login` | — | Email/password login |
| `POST` | `/api/auth/google` | — | Google ID token login |
| `POST` | `/api/auth/refresh` | — | Rotate refresh token |
| `POST` | `/api/auth/forgot-password` | — | Send reset link |
| `POST` | `/api/auth/reset-password` | — | Apply reset token |
| `POST` | `/api/auth/change-password` | User | Change password |
| `GET`  | `/api/auth/me` | User | Current user profile |
| `GET`  | `/api/settings/public` | — | Public site settings |
| `GET`  | `/api/listings` | — | Browse listings |
| `POST` | `/api/bookings` | User | Create booking |
| `GET`  | `/api/orders` | Provider | Provider order list |
| `GET`  | `/api/admin/stats` | Admin | Dashboard stats |
| `GET`  | `/api/admin/inquiries` | Admin | Pending bookings |

Full API reference available at `/swagger` in development.

## Deployment

The backend is deployed on **Railway**. Railway injects a `DATABASE_URL` environment variable (postgres:// URI) which the app converts automatically to an Npgsql connection string. Migrations run automatically on startup in production.

The frontend is deployed on **Vercel** and connects to the Railway backend via `VITE_API_URL`.

## User roles

| Role | Description |
|------|-------------|
| `Customer` | Books listings, manages own bookings and messages |
| `Provider` | Manages a supplier's listings, views incoming orders |
| `Admin` | Full platform access — suppliers, settings, analytics |
