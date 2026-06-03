# Ruumly Backend — Claude Code Context

ASP.NET Core 8 + EF Core + PostgreSQL. B2B2C marketplace for storage rental in the Baltics.
Deployed on Railway. The .NET project is in Ruumly.Backend/. The social-preview Cloudflare
Worker is in workers/social-preview/. Frontend lives in estonia-space-hub/ (separate repo,
gitignored here).

## Current focus
Storage-only launch in Estonia. Moving/Trailer hidden behind admin toggles
(showMovingService / showTrailerService). Read docs/ROADMAP.md before proposing new
features — we are in a go-to-market phase, not a build phase.

## Conventions
- Run dotnet/EF from the Ruumly.Backend/ folder.
- Local migrations: dotnet ef database update --connection "Host=localhost;Port=5433;Database=ruumly;Username=postgres;Password=postgres" (Docker Postgres is on host port 5433).
- PlatformSettings is a key/value table — new settings need no migration.
- All transactional emails go through EmailTranslations (5 languages: et/en/ru/lv/lt).
- Outbound HTTP must use Helpers/OutboundEndpointValidator (SSRF guard).
- Don't add new verticals or large features without checking docs/ROADMAP.md.

## Roadmap
Authoritative plan: docs/ROADMAP.md. For "what's next", pick the top unchecked item in the
current phase. Phase 0 (Montonio go-live) is the only hard revenue blocker.