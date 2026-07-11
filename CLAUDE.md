# Ruumly Backend — Claude Code Context

ASP.NET Core 8 + EF Core + PostgreSQL. B2B2C marketplace for storage rental in the Baltics.
Deployed on Railway. The .NET project is in Ruumly.Backend/. The social-preview Cloudflare
Worker is in workers/social-preview/. Frontend lives in estonia-space-hub/ (separate repo,
gitignored here).

## Current focus
Pivoted (2026-07, after Sergei Anikin's feedback) to a **demand-first concierge**: the
public front door is "tell us what you need" (POST /api/leads/request → DemandLead with
Source="concierge"), organized around the life event ("I'm moving"), not the inventory.
Ops = manual match loop in admin (Received→Contacted→Quoted→Booked/Lost/Unmatched; see
docs/CONCIERGE-OPS.md). North-star metrics: qualified requests/week, contact rate,
quote→booking conversion, median first response (GET /api/admin/leads/metrics) — NOT
partner signups. Geography: Tallinn/Harjumaa first. The marketplace (search, listings,
booking/payment/contract infra, free listing + optional boosts) stays fully functional as
the ops layer — the hero flip is gated by the `conciergeFirst` platform setting. Don't
build deep partner self-serve features unless they improve the manual demand loop.

## Conventions
- Run dotnet/EF from the Ruumly.Backend/ folder.
- Local migrations: dotnet ef database update --connection "Host=localhost;Port=5433;Database=ruumly;Username=postgres;Password=postgres" (Docker Postgres is on host port 5433).
- PlatformSettings is a key/value table — new settings need no migration.
- All transactional emails go through EmailTranslations (5 languages: et/en/ru/lv/lt).
- Outbound HTTP must use Helpers/OutboundEndpointValidator (SSRF guard).
- Don't add new verticals or large features without checking docs/ROADMAP.md.

## Roadmap
Authoritative plan: docs/ROADMAP.md (rewritten 2026-07 to the concierge direction). For
"what's next", pick the top unchecked item in the current phase. Phase 0 = run the manual
match loop + honest concierge-scoped metrics (the whole game right now); Montonio go-live is
Phase 1 for the demoted ops/booking layer, not the front door. North-star = qualified
requests/week, supplier match rate, quote→booking, median first response — not partner signups.