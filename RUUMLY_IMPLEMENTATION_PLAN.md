# Ruumly Implementation Plan

Companion to `RUUMLY_FULL_AUDIT.md` and `RUUMLY_UX_MATRIX.md`. Ordered by
impact × (1/complexity), grouped P0→P3. **Done this round** is marked ✅ and
detailed in `RUUMLY_FINAL_REVIEW.md`; everything else is the ranked backlog.

Guiding rule, unchanged from the standing brief: **change what Ruumly says and
records before changing what it does.** Matching radii, contact policy, pricing,
opt-out logic and claim security are business decisions — this plan proposes,
it does not retune them unilaterally. Anything marked *(founder call)* needs a
decision before it ships.

---

## P0 — business-critical / broken / security

| # | Item | Files | Size | Status |
|---|------|-------|------|--------|
| 0.1 | **Decline path** — the outreach email's third answer, wired end to end: `POST /quote/{token}/decline`, `DeclineReasons`, model columns + migration, quote-page UI (5 languages, formal ET), ops alert, recorded state | `QuoteController`, `ProviderOutreach`, `DeclineReasons`, `QuoteDecline.tsx`, locales ×5 | M | ✅ shipped + 7 tests + live-verified |
| 0.2 | **CORS wildcard** — fail-closed, team-slug-pinned preview matching (was: any `estonia-space-hub-*.vercel.app` with credentials) | `CorsPolicySetup`, `Program.cs`, `CorsPolicyTests` | S | ✅ shipped + 4 tests |
| 0.3 | **SSRF** — DNS-resolving `IsAllowedAsync` at the two request-time sites that attach an auth token | `IntegrationDispatchService`, `AdminSuppliersController` | S | ✅ shipped |
| 0.4 | **CSV formula injection** — shared `csvCell`/`csvRow` across all four exports | `lib/csv.ts` + 4 callers | S | ✅ shipped |
| 0.5 | **Customer submit rate bucket** — own `lead-request` limiter | `Program.cs`, `SupportController` | XS | ✅ shipped |
| 0.6 | **Admin cockpit "lies green"** — `isError`/retry on the leads and suppliers lists | `AdminLeads`, `AdminSuppliers`, kit `SectionError` | S | ✅ shipped |
| 0.7 | **"Customer chose" queue** — `queue=chosen` predicate + cockpit counter + (ideally) provider-won notification | `AdminLeadsController`, `AdminDashboard`, `OffersController` | M | planned |
| 0.8 | **Waiting-on-customer state** — a sent offer must not fall out of every queue | `DemandLeadLifecycle`, `AdminLeadsController` | M | planned |
| 0.9 | **CSRF defence-in-depth** — require `X-CSRF-Token` (or an Origin re-check) unconditionally on `/auth/refresh` now that the CORS root cause is closed | `AuthController` | S | planned *(security review first)* |

---

## P1 — major customer / supplier / admin UX

| # | Item | Files | Size | Status |
|---|------|-------|------|--------|
| 1.1 | **Storage-guard hot paths** — `safeStorage` on everything that reads localStorage before mount / in the error boundary | `safeStorage.ts` + 6 callers | S | ✅ shipped |
| 1.2 | **Date-shift** — `timeZone:"UTC"` at the formatter; QuotePage + admin routed through it | `offerDate.ts`, `QuotePage`, `LeadWorkspace` | S | ✅ formatter + QuotePage; admin planned |
| 1.3 | **i18n weight** — per-language lazy chunks + prerender preload | `LanguageContext`, `vite.config`, `prerender-seo`, `locales/*` | M | ✅ shipped |
| 1.4 | **Moving needs a destination** — require `toCity` for moving / vanrental-with-driver; validate in `handleSubmit` and on deep-link | `RequestPage` | S | planned |
| 1.5 | **Provider told the outcome** — email the chosen provider; GET carries `wonByYou` | `OffersController`, `QuoteController`, `QuotePage` | M | planned |
| 1.6 | **Quote token expiry** — reject > 60 days (and > 14 days past terminal) | `QuoteController` | S | planned |
| 1.7 | **Localise the partner-message email** | `SupportController`, `EmailTranslations` | S | planned |
| 1.8 | **Marketing page leads with a real enquiry** — replace the trust strip with one anonymised enquiry card | `ProviderPage` | S | planned *(design)* |
| 1.9 | **ET `sina`→`teie`** across `provPage/quote/claim` (~30 strings) | `locales/et` | M | planned *(founder call)* |
| 1.10 | **Cockpit reads its own payload** — surface `blocked`/`stalled` counts already returned | `AdminDashboard` | S | planned |
| 1.11 | **Unsaved-edit guard on offer send** | `LeadWorkspace`, `LeadOfferStage` | S | planned |
| 1.12 | **Workspace polling** — `refetchInterval` while a lead is expanded | `LeadWorkspace` | XS | planned |
| 1.13 | **Status state machine** — transition table + disable chips on `Converted` | `DemandLeadLifecycle`, `LeadWorkspace` | M | planned |
| 1.14 | **Location-detail → `/request` CTA** | `LocationDetailPage` | XS | planned |

---

## P2 — performance / usability / maintainability

| # | Item | Files | Size | Status |
|---|------|-------|------|--------|
| 2.1 | **DemandLead `(Status, CreatedAt)` index** | `RuumlyDbContext` + migration | XS | ✅ shipped |
| 2.2 | **Dead-code deletion** — MapPlaceholder, NavLink, LeadSummaryStrip | — | XS | ✅ shipped |
| 2.3 | **Homepage stops fetching 739 KB of `/api/locations`** on first paint — a count endpoint + lazy map data | `LocationsController`, `HomePage` | M | planned |
| 2.4 | **Extend the computed-key coverage test** to the ~15 template-literal `t()` families | `translationCoverage.test` | S | planned |
| 2.5 | **Delete the unwired `GET /admin/leads/{id}/matches`** (duplicate matching logic) | `AdminLeadsController`, services | XS | planned |
| 2.6 | **Storage scope: start date; trailer/van: one-way vs return** | `RequestPage`, `ScopeQuestions` | S | planned |
| 2.7 | **Merge duplicate directory rows** — one row per branch today; ~43 near-certain dupes | new endpoint + `AdminSuppliers` UI | M | planned |
| 2.8 | **Backfill the 226 no-contact-email rows** — bounded manual data work | ops | — | planned |

---

## P3 — visual polish

Handed to Claude Design — see `CLAUDE_DESIGN_RUUMLY_PROMPT.md`. Highest-value
surfaces: the `/request` wizard (mobile-first, sticky nav, merged step 1), the
provider marketing page (lead with an enquiry, demote the catalogue), the admin
lead workspace (information density without the 50-row scroll), the quote/offer
cards, and the status-badge / queue-chip system.

---

## Phase gating (unchanged from `docs/ROADMAP.md`)

- **Phase 0 (now):** run the manual match loop + honest concierge-scoped metrics.
  This round strengthened Phase 0 directly — the provider can now answer three
  ways, the metrics stop miscounting declines as silence, and the cockpit stops
  lying green.
- **Phase 1:** Montonio go-live for the demoted booking layer.
- **Phase 2** (gated on provider replies existing): inbound email processing,
  silent-provider nudge, auto customer-question, `ILeadIntelligenceService`.
  Note 0.1 (decline) is the first piece of the reply substrate Phase 2 needs.
- **Phase 3:** trailer/vanrental keep-narrow decision; directory enrichment; SEO.

North-star metrics stay: qualified requests/week, supplier match rate,
quote→booking, median first response — **not** partner signups.
