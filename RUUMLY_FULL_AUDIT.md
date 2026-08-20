# Ruumly Full Product & Technical Audit

**Date:** 2026-08-20
**Supersedes:** the 2026-08-14 audit of the same name (10 shipping commits stale).
**Scope:** `Ruumly.Backend/` (ASP.NET Core 8 + EF Core + PostgreSQL),
`estonia-space-hub/` (React 18 + Vite + TS + TanStack Query + shadcn/ui),
`workers/social-preview/`, `docs/`.
**Method:** four parallel per-perspective source audits (customer / supplier /
admin / bug-sweep), plus a live walkthrough of the deployed product at
`ruumly.eu` and `api.ruumly.eu`, plus gates actually run:

- `dotnet test Ruumly.Backend.Tests` → **1084 passed / 0 failed / 2 skipped**
  (with `RUUMLY_TEST_PG` on `127.0.0.1:5433` so all 11 Npgsql integration tests
  execute — checking **"0 skipped-that-should-run"**, not just "0 failed").
- `npm run typecheck` → **0 errors**.
- `npm run test` → **283 passed**.
- `npm run build` → clean; 240 prerendered route heads.
- Live: homepage/request/search/quote/status pages driven in-browser at 1280px
  and 375px; API payloads and bundle sizes measured on the wire.

This document describes the product **as audited**. What was **fixed in this
round** is in `RUUMLY_FINAL_REVIEW.md`; the ranked backlog is in
`RUUMLY_IMPLEMENTATION_PLAN.md`; the friction tables are in `RUUMLY_UX_MATRIX.md`.

---

## Executive summary

Ruumly is in better shape than a fresh reader expects. The concierge loop is a
real, deliberately-reasoned system, not an AI feature pile — auto fan-out on
intake with per-recipient quote tokens, inbox-level dedupe, opt-out enforced at
the candidate finder, a Resend bounce/complaint webhook, and a genuinely
thoughtful set of "safe by design" behaviours (LeadScope/LeadPhotos drop unknown
ids rather than throw; the quote page treats a closed lead as an honest dead-end,
never a broken link). The copy has been through an honesty pass: the front door
no longer promises a response time it cannot keep, and the geography is stated
truthfully (services run in Tallinn/Harjumaa; the *directory* spans EE/LV/LT).

The product's problems are not "it is broken." They cluster into four themes:

1. **The provider could only answer one way.** The outreach email invited three
   answers — a price, a question, or "no" — but only the price and the question
   had anywhere to land. Every real decline was recorded as *silence*, feeding
   the exact metric meant to prove the outreach was failing, and the same
   provider kept receiving the next lead. **(Fixed this round.)**

2. **A few real security holes.** A CORS wildcard that trusted any
   attacker-registerable `estonia-space-hub-*.vercel.app` origin with
   credentials (paired with a refresh-token CSRF skip that trusts that list); two
   SSRF guards using the DNS-blind synchronous check right before an
   authenticated outbound request; and CSV exports vulnerable to spreadsheet
   formula injection from anonymous public lead input. **(All fixed this round.)**

3. **The admin cockpit can lie green.** Several list views destructure only
   `{ data, isLoading }`, so a failed fetch renders the *empty* state — "no
   requests yet" while the API is down — on the one screen the founder trusts to
   say what needs attention. And three of the four queues the loop actually has
   (blocked-on-us, stalled, customer-chose) have no counter on the landing
   screen, even though two of them are already in the payload the dashboard
   fetches. **(The "lies green" cases fixed; the missing-queue work is planned.)**

4. **Weight in the wrong places.** 63% of the JavaScript a first-time visitor
   downloaded was five languages of translation data, of which they read one. The
   customer wizard is three screens and ~16 taps for a single-service move. The
   provider marketing page leads with a badge and a 28-item paid catalogue
   instead of a real customer enquiry. **(The i18n weight fixed this round; the
   funnel and marketing-page shape are planned.)**

### What Ruumly currently feels like, per role

- **Customer:** clear proposition, honest copy, a genuinely good structured
  intake (per-service scope chips, photos, from/to). Costs more taps than it
  needs to, and a couple of gates let a move be filed without a destination.
- **Supplier:** a strong quote-by-token page and a rewritten outreach email —
  now with all three answers wired. The remaining friction is downstream: a
  provider who quotes is never told the outcome, and 226 active directory rows
  can never be reached by any channel because they have no contact email.
- **Admin:** a real ops cockpit with a command palette and deep-linkable lead
  workspace. The daily loop works; it costs a few more clicks and a bit more
  scrolling than it should, and it hides three of its own queues.

---

## 1. Customer audit

The funnel: `HomePage` (ConciergeHome branch, `conciergeFirst=true`) → `/request`
3-step wizard → `POST /api/leads/request` → success screen with a
`/request-status/{token}` link.

### What is right

- **Structured intake.** Per-service scope chips (storage: size/duration/goods;
  moving: size + floor-and-lift at *both* ends + heavy items; trailer, van,
  cleaning each scoped), photos, from/to addresses, an explicit "my date is
  flexible" chip. The answers are stored as positions (`ScopeJson`), not rendered
  sentences, so they can be re-rendered in the *provider's* language.
- **No account required.** The endpoint is `[AllowAnonymous]`; the success screen
  never pushes registration; the status page is token-only.
- **Honest success screen.** Points at the customer's inbox (catches a typo'd
  email here rather than in silence) and offers a bookmarkable status link.
- **A bot honeypot** (`req-website`, off-screen, `aria-hidden`, `tabindex=-1`).

### Findings

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| C1 | P0 | `Program.cs` rate limits + `SupportController.cs:403` | The customer submit shared the 5-per-10-min `public-email` bucket with the contact form and notify-interest, keyed by IP — so behind office NAT or Estonian mobile CGNAT, strangers 429 a first-time customer out of the one submit the product exists for. **(Fixed: own `lead-request` bucket, 15/10min.)** |
| C2 | P1 | `RequestPage.tsx:851` | For `moving`, the destination city has no asterisk and is absent from the step gate — a move can be filed with an origin and no destination, which no mover can quote. |
| C3 | P1 | `RequestPage.tsx` | `vanrental` with a driver *is* a move, but the destination + to-address fields gate on `movingSelected` only, so those go out with no destination. |
| C4 | P1 | `RequestPage.tsx:637` | `handleSubmit` re-validates only the email; a deep-link to `?step=3` with no category/city files an empty `Category=Any` request. |
| C5 | P1 | `RequestPage.tsx:340` | Draft-restore and deep-link paths apply the service-visibility allow-list inconsistently, so a hidden vertical can end up selected-but-undeselectable and still submitted. |
| C6 | P1 | `LocationDetailPage.tsx` | No path into `/request` anywhere on the location-detail page; a visitor who drilled into a specific site with nothing bookable leaves with nothing to convert on. |
| C7 | P2 | `RequestPage.tsx` | Storage never asks *when storage starts*; trailer/van never ask one-way vs return — both change the price. |
| C8 | P2 | `RequestPage.tsx:1179` | Next/Submit sit at the bottom of a long card in normal flow; on a phone the way forward is always below the fold. |
| C9 | P2 | copy | "2-minute request" and "we pick providers by hand" are unenforced/inaccurate claims (the founder-approved "up to 3 offers" is deliberate and untouched). |

**Click count, homepage → submitted single-service move: ~16 taps** across 3
screens (4 pieces of required information: service, city, date, email). See
`RUUMLY_UX_MATRIX.md` for the breakdown and the reduction opportunities.

---

## 2. Supplier audit

The path: outreach email (`ProviderOutreachComposer`) → quote-by-token page
(`QuotePage` + `Quote*` components) → optional claim (`ClaimController`) →
optional dashboard (`ProviderDashboardPage`).

### What is right

- **The quote page is genuinely fast:** email link → price → send is 3 taps,
  unit pre-selected per category, availability and note optional. Quote *update*
  works (keyed on `CreatedFromOutreachId`, never duplicates, never wipes a note).
- **Need-info flow** ("I can't price this from what you sent") is well-built:
  merge-not-append, empty-set-is-not-a-retraction, status promoted only from a
  silent state.
- **No account needed** to quote. Outreach leaks no internal/admin URL. HTML and
  plain-text parts are kept in lockstep.
- **Inbox dedupe and the radius ladder** are the right answers to hard problems.

### Findings

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| S1 | P0 | `ProviderOutreachComposer` + `QuoteController` | The email's third answer — decline — had **no landing anywhere**. `ProviderOutreachStatus.Declined` was written by zero production paths, so a "no" was invisible: still counted as silence, still re-fanned-out. **(Fixed: `POST /quote/{token}/decline` + reason chips + recorded state + ops alert + quote-page UI in 5 languages.)** |
| S2 | P0 | `ClaimController.cs:149` | 226 active suppliers have a blank `ContactEmail`, so the claim gate (`stored.Length>0 && stored==typed`) can never match — and the *same* missing column also blocks partner-page messages *and* every concierge fan-out slot (skipped as `no_email`). One missing field, three dead channels. |
| S3 | P1 | `OffersController.cs:96` | A provider who quotes is **never told the outcome** — only ops gets the "customer chose" email. Re-opening the link shows the same "already wrapped up" line to a winner and a loser alike. Nothing kills a second quote faster. |
| S4 | P1 | `QuoteController.cs:107` + `QuotePage.tsx:314` | The email names the provider's trade (`"Moving + Storage"`); the page it links to collapses a multi-service lead to `"Multiple services"` and defaults the unit to `/month` — wrong for exactly the movers most often being asked. |
| S5 | P1 | `ProviderOutreach.cs` | The quote token **never expires** — the customer's structured ask stays readable to anyone holding a years-old emailed link. **(Recommended: 60-day cap.)** |
| S6 | P1 | `SupportController.cs:252` | The partner-page notification email is hardcoded English; a claimed Latvian partner's first real customer message arrives in a language they did not choose. |
| S7 | P1 | `translations.ts` provider copy | The Estonian marketing/quote/claim copy is informal (`sina`) while every provider *email* is formal (`teie`) — a register break a Baltic business owner reads as "a consumer app, or a foreign company that didn't check." **(Founder decision to convert — see plan.)** |
| S8 | P1 | `ProviderPage.tsx:145` | The marketing page leads with a badge and a 28-item paid catalogue; the one artefact that proves the whole proposition — a real, redacted customer enquiry — never appears. |
| S9 | P2 | `ConciergeOutreachService.cs:158` | `resend:true` mints a *new* token and a second outreach row, orphaning the earlier quote as a separate option; no cooldown, no cap. |

**Click count: email open → price submitted = 3 taps. Email open → decline =
previously impossible (free-text reply into a shared inbox); now 2 taps.**

---

## 3. Admin audit

One founder runs the daily loop from an ops-first "control room" with a Cmd+K
palette and a deep-linkable lead workspace.

### What is right

- **Quote → offer option is zero-click** (auto-seeded server-side, provenance
  preserved and badged).
- **The cockpit is queue-led**, not vanity-metric-led; the palette deep-links
  straight into a lead workspace; the Today view auto-expands a deep-linked lead.
- **Backend duplicate-contact prevention is strong** (`already_contacted` /
  `duplicate_email` refused at the send and mirrored in the preview).

### Findings

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| A1 | P0 | `AdminLeads.tsx:199` | The leads query destructured only `{ data, isLoading }` — a failed fetch rendered **"no requests yet"** while the API was down, on the founder's inbox. **(Fixed: `isError` → `SectionError` with retry; same fix applied to `AdminSuppliers`.)** |
| A2 | P0 | `AdminDashboard.tsx` | No in-app surface for **"customer chose — confirm booking"** and no **"quotes sitting in a draft"** counter. A missed ops email = a booked job that never converts. |
| A3 | P0 | `DemandLeadLifecycle` | No **"waiting on customer"** status: after an offer is sent the lead sits at `Quoted` and matches *no* queue, so a customer silent for a week is invisible. |
| A4 | P1 | `AdminDashboard.tsx:105` | The cockpit reads only `needsResponse` while `blocked` and `stalled` counts are **already in the same payload** it fetches — two queues invisible at zero request cost. |
| A5 | P1 | `LeadOfferStage.tsx` / `LeadDeliveryReview.tsx` | Unsaved price edits are **silently discarded on send**: the PATCH is built from the server copy, so retyping a price without pressing Save sends the old one. |
| A6 | P1 | `DemandLeadLifecycle.cs:33` | `MoveTo` is not a state machine — no transition table, no illegal-transition rejection. Two clicks in the workspace regress a **Booked** lead to Contacted or Lost, no confirm. |
| A7 | P1 | `LeadWorkspace.tsx` | Nothing in the workspace polls — a quote landing while it is open never appears without a browser reload. |
| A8 | P1 | `AdminSuppliers.tsx:123` | Deactivate has no confirm and no success toast; no duplicate-merge capability exists at all (the directory is one row per branch). |
| A9 | P2 | `AdminLeadsController.cs:705` | `GET /admin/leads/{id}/matches` is 110 lines, fully wired, and called by nothing — duplicate matching logic that will drift from `ProviderCandidateFinder`. |
| A10 | P2 | `AdminLeads.tsx` | Customer **name** is not shown anywhere in the workspace without opening the edit form; lead **source** and match reason are not shown at all. |

**Click counts:** new lead → one more outreach = **4 clicks**; provider quote →
offer sent = **3 clicks**; mark lost = **2 clicks**. See `RUUMLY_UX_MATRIX.md`.

---

## 4. Bug / quality / security sweep

### Security (all P0/P1 verified in source)

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| B1 | P0 | `CorsPolicySetup.cs:49` | `host.StartsWith("estonia-space-hub-")` + `AllowCredentials()` trusts **any** attacker-registerable `estonia-space-hub-evil.vercel.app`. **(Fixed: fail-closed, team-slug-pinned preview matching + 4 tests including the exact lookalike the old test missed.)** |
| B2 | P0 | `AuthController.cs:87` | `/auth/refresh` skips CSRF for cookie-sourced tokens on the assumption "CORS prevents cross-origin POST" — false given B1, and the cookie is `SameSite=None`. **(Root cause B1 fixed; a defence-in-depth CSRF tightening is recommended in the plan.)** |
| B3 | P1 | `IntegrationDispatchService.cs:119`, `AdminSuppliersController.cs:598` | DNS-blind synchronous `IsAllowed` immediately before an outbound request that **attaches the supplier's bearer token** — a hostname resolving to `169.254.169.254` passes. **(Fixed: both switched to the DNS-resolving `IsAllowedAsync`.)** |
| B4 | P1 | `AdminLeads.tsx:266` + 3 provider exports | CSV exports quote cells but never neutralise a leading `= + - @` — and `city`/`query`/`email` come straight from the anonymous public lead POST, opened on the founder's own machine. **(Fixed: shared `csvCell`/`csvRow` formula-injection guard across all four exports.)** |

### Reliability / correctness

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| B5 | P1 | `apiClient.ts`, `main.tsx`, `ErrorBoundary.tsx`, `CookieConsent.tsx`, `LanguageContext.tsx` | Unguarded `localStorage` on paths that run before React mounts (and inside the error boundary itself) — a storage-blocked browser gets a **blank white page**, not a degraded feature. **(Fixed: shared `safeStorage` helper adopted across all hot paths.)** |
| B6 | P1 | `offerDate.ts:21`, `QuotePage.tsx:425`, `LeadWorkspace.tsx:192` | Dates formatted without `timeZone:"UTC"` while `NeedDate` is stored as UTC midnight — the move date renders a **day early** for any viewer west of UTC, on the offer the customer accepts. **(Fixed at the source formatter + QuotePage; admin routed through it.)** |
| B7 | P2 | `RuumlyDbContext.cs` DemandLead | No index on `Status`/`CreatedAt` — the founder's most-hit screen (filter Status, order CreatedAt DESC) was a seq scan + sort. **(Fixed: composite `(Status, CreatedAt)` index + migration.)** |
| B8 | P2 | `MapPlaceholder.tsx`, `NavLink.tsx`, `LeadSummaryStrip.tsx` | Dead components (zero importers), one carrying stale hardcoded city labels no translation guard covers. **(Fixed: deleted.)** |
| B9 | P2 | ~15 template-literal `t()` key families | Outside the one family a coverage test protects; a new enum member on either side renders a raw key in production, invisible to typecheck and the suite (the class of bug that shipped the 2026-08-18 incident). **(Planned: extend the source-scan coverage test.)** |

### Verified clean (no findings)

`async void` / `.Result` / `.Wait()` — none. `DateTime.Now` — none (all UtcNow).
Every `Admin*Controller` carries `[Authorize(Roles="Admin")]`. All public POSTs
rate-limited. Token entropy: 32 bytes from `RandomNumberGenerator`, 43 url-safe
chars; no sequential IDs on token-auth GETs. Raw SQL parameterised. Hangfire args
serialisable. Transactions on all money/lifecycle paths. i18n: all 5 blocks equal
key count, 0 placeholder mismatches, correct per-language plural-form counts.

---

## 5. Performance

Measured on the wire against `api.ruumly.eu` and the deployed bundle:

- **i18n was 63% of first-load JS.** The five language dictionaries shipped as one
  369 KB-brotli chunk; a visitor reads one. **(Fixed: per-language lazy chunks —
  Estonian eager (default + fallback), the other four on demand, with a
  prerendered `modulepreload` per route so the active language is in flight
  before React renders. First-load JS for a non-Estonian visitor drops by ~70 KB
  brotli; an Estonian visitor no longer downloads four unused languages.)**
- **Homepage fetches 739 KB of `/api/locations` JSON** (1,179 rows, 27 fields
  each) on first paint, to derive a provider count and a map. Deferred rendering
  exists (`DeferUntilVisible` on the map) but the *fetch* is not deferred.
  **(Planned: a lightweight count endpoint + lazy map data.)**
- LCP/DCL are healthy (DCL ~160 ms, four parallel API calls ~200–275 ms each).

---

## 6. Accessibility, mobile, i18n

- **Mobile:** no horizontal overflow at 375px on the pages walked; service cards
  and scope chips are 44px targets. The wizard's Next/Submit below-the-fold
  position (C8) and a couple of 40px chips are the main gaps.
- **A11y:** service cards carry `aria-pressed`; the need-info panel is a real
  `fieldset`/`legend` with managed focus; the new decline panel is a `radiogroup`
  with the same focus discipline. The cookie banner offers accept + a policy
  link (no explicit reject, but only essential cookies are set pre-consent).
- **i18n honesty:** the deployed LV surface correctly says services run in
  Tallinn/Harjumaa while the directory covers EE/LV/LT — the geography rule is
  being followed. The remaining i18n gap is the `sina`/`teie` register split
  (S7) and the un-guarded computed-key families (B9).

---

## 7. The biggest problems, ranked

1. **The provider could only answer one way** (S1) — the outreach invited a "no"
   with nowhere to record it, so every decline read as silence and re-fanned-out.
   *Fixed this round.*
2. **The CORS/refresh security pair** (B1/B2) — credentialed access from an
   attacker-registerable origin. *Root cause fixed this round.*
3. **The provider is never told the outcome** (S3) — asking cold businesses for
   unpaid pricing work and never closing the loop. *Planned.*
4. **The admin cockpit can lie green and hides its own queues** (A1–A4). *A1
   fixed; the queues planned.*
5. **226 unreachable directory rows** (S2) — one missing column, three dead
   channels; the fix is bounded manual data work, not code.
