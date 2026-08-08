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
partner signups. The marketplace (search, listings, booking/payment/contract infra, free
listing + optional boosts) stays fully functional as the ops layer — the hero flip is
gated by the `conciergeFirst` platform setting. Don't build deep partner self-serve
features unless they improve the manual demand loop.

**Geography (2026-08):** the concierge ops loop — intake, outreach, offers, bookings —
is still **Tallinn/Harjumaa-centred** (density before breadth; auto-fanout starts at a
25 km radius). It is the **DIRECTORY that is going Baltic**: the admin import accepts
EE/LV/LT rows, validated against per-country bounding boxes, and `Supplier.Country`
picks the provider outreach language. Latvian/Lithuanian coverage is directory listings,
not a live ops loop there.

**Services (2026-08 founder decision):** 5 consumer-selectable —
`warehouse | moving | trailer | cleaning | vanrental`. `packing` and `insurance` are
**retained for data, not for sale**: still valid `Supplier.ServiceTypesJson` metadata and
still present on historical `DemandLead` rows, but never offered in the intake, public
search or sitemap. Market research across EE/LV/LT: packing is never sold standalone in
the Baltics (it is a line item inside a mover's offer — LT's national directory lists the
same two companies under "pakavimo paslaugos" as under moving), and "insurance" here is
CMR carrier liability sold B2B to hauliers, not a household product. Intake maps
`packing → moving` (the intent is recorded in the lead's Query + Details, which the
provider outreach email prints) and `insurance → Any` (admin routes it by hand).
**Never delete the `Packing`/`Insurance` enum members** — the Category column persists
enum NAMES, so removing one makes production rows unreadable. Single source of truth:
`Constants/ServiceCategories` (`BySlug` = storage catalogue, `ConsumerSlugs` = sales
catalogue, `PublicAliasFor` = how a retired slug resolves on a public surface).

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