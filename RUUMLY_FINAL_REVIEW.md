# Ruumly — Final Review (2026-08-20 round)

What changed this round, why, and how it was verified. The full audit is in
`RUUMLY_FULL_AUDIT.md`; the ranked backlog in `RUUMLY_IMPLEMENTATION_PLAN.md`.

**Gates at end of round:**
- Backend: `dotnet test` (with `RUUMLY_TEST_PG=127.0.0.1:5433`) → **1084 passed /
  0 failed / 2 skipped** (was 1075; +9 new tests; the 2 skips are the known
  InMemory-provider skips, all 11 Npgsql integration tests ran).
- Frontend: `npm run typecheck` → **0 errors**; `npm run test` → **283 passed**;
  `npm run build` → clean, 240 prerendered route heads.
- The decline feature was additionally **verified live** end-to-end against a
  local backend + seeded token (GET→POST→GET, DB row confirmed `Declined`).

Nothing was committed or pushed — the tree is staged for your review.

---

## What was changed, by theme

### 1. The provider's third answer — decline (P0)

The outreach email has invited "a short 'not possible' is a perfectly good
answer" since 2026-08-18, but `ProviderOutreachStatus.Declined` was written by
**zero** production paths — a real "no" arrived as free text in a shared inbox,
was recorded nowhere, kept counting as *silence* in the provider-silence metric,
and left the provider in range for the next fan-out. This is the single change
most likely to move behaviour, because it turns the most common provider
intention into a recorded fact.

- **Backend:** `POST /api/quote/{token}/decline` mirroring the need-info contract
  — unknown token indistinguishable from missing, closed lead 409s, and a
  provider who already **quoted** gets a distinct `already_quoted` 409 (a live
  offer option must not be silently retracted by a button). Idempotent: a repeat
  press updates the reason, keeps the first timestamp, and never erases a note on
  a blank repeat.
- `DeclineReasons` catalogue (wrong-area / no-capacity / not-our-service /
  too-small / other) — stored as a slug, never an enum name; two of the reasons
  tell ops the **directory row** is mis-filed.
- Model columns `DeclineReason` / `DeclineNote` / `DeclinedAt` + migration
  `AddProviderOutreachDecline`; ops alert threaded on the lead reference; GET now
  carries `declined`.
- **Frontend:** `QuoteDecline.tsx` — quietest of the three answers (muted text
  trigger, `radiogroup`, outline-danger confirm, never a filled CTA competing
  with Send); a dedicated declined-state screen; an `already_quoted` inline
  notice. **5-language copy, Estonian formal (`teie`)** — verified present in all
  five built locale chunks (17 keys × 5).
- **Tests:** `QuoteDeclineTests` (7) — bare decline, reason+note, unknown-reason
  collapse, already-quoted 409, closed-lead 409, unknown token, idempotence,
  GET-reports-declined. **Live-verified**: seeded a real token, drove
  GET→POST→GET, confirmed the DB row flips to `Declined` and `declined:true`
  serialises camelCase for the page.

Files: `QuoteController.cs`, `ProviderOutreach.cs`, `DeclineReasons.cs`,
`OfferRequests.cs`, `QuoteDtos.cs`, migration; `QuoteDecline.tsx`, `QuotePage.tsx`,
`services/index.ts`, `locales/*.ts`, `QuoteDeclineTests.cs`.

### 2. Security (P0/P1)

- **CORS wildcard hole.** `host.StartsWith("estonia-space-hub-")` +
  `AllowCredentials()` trusted **any** attacker-registerable
  `estonia-space-hub-evil.vercel.app`. Rewrote to **fail-closed, team-slug-pinned**
  preview matching: the project's own production alias matches exactly; a preview
  must both start with the project prefix **and** end with `-{teamSlug}.vercel.app`
  from `Cors:VercelTeamSlug`; with no slug configured, **no** wildcard preview is
  trusted. Closes the hole by default in prod (slug unset). +4 tests including the
  exact `estonia-space-hub-evil` lookalike the old regression test never checked.
  - *Follow-up (planned, security-review first):* the paired `/auth/refresh` CSRF
    skip trusted this list — now that the root cause is closed, tighten it to
    require the CSRF token unconditionally.
- **SSRF.** Two request-time guards that attach the supplier's bearer token used
  the DNS-blind synchronous `IsAllowed` (which only inspects IP literals) — a
  hostname resolving to `169.254.169.254` passed. Switched both
  (`IntegrationDispatchService`, `AdminSuppliersController.TestSupplier`) to the
  DNS-resolving `IsAllowedAsync`. `SupplierPollingService` already did this right.
- **CSV formula injection.** All four exports quoted cells but never neutralised a
  leading `= + - @` — and `city`/`query`/`email` come straight from the anonymous
  public lead POST, opened on the founder's own machine. Added `lib/csv.ts`
  (`csvCell`/`csvRow`, RFC-4180 quoting + formula-trigger escaping) and routed
  `AdminLeads` and the three provider exports through it.

Files: `CorsPolicySetup.cs`, `Program.cs`, `CorsPolicyTests.cs`,
`IntegrationDispatchService.cs`, `AdminSuppliersController.cs`; `lib/csv.ts` + 4
callers.

### 3. Reliability (P1)

- **`safeStorage` guard.** Unguarded `localStorage` ran on paths that execute
  before React mounts (the API client on every request, `main.tsx`, the error
  boundary itself) — a storage-blocked browser got a **blank white page**, not a
  degraded feature. Added `lib/safeStorage.ts` and adopted it in `apiClient`,
  `main.tsx`, `ErrorBoundary`, `CookieConsent`, `LanguageContext`.
- **Date-shift.** `NeedDate` is stored as UTC midnight; formatting without
  `timeZone:"UTC"` rendered the move date a **day early** for any viewer west of
  UTC — on the offer the customer accepts and the job date the provider prices.
  Fixed at the source formatter (`offerDate.ts`) and routed `QuotePage`'s
  hand-rolled date through it. *(The admin `LeadWorkspace` date is the same class
  and is in the plan.)*
- **Admin cockpit "lies green".** `AdminLeads` and `AdminSuppliers` destructured
  only `{ data, isLoading }`, so a failed fetch rendered the *empty* state — "no
  requests yet" / an empty directory while the API was down. Promoted
  `SectionError` into the shared `admin/kit` and wired `isError`/retry into both.

Files: `safeStorage.ts` + 5 callers; `offerDate.ts`, `QuotePage.tsx`;
`kit/SectionError.tsx`, `AdminDashboard.tsx` (now imports the shared one),
`AdminLeads.tsx`, `AdminSuppliers.tsx`, locales (2 new error keys ×5).

### 4. Performance (P1/P2)

- **i18n weight — the big one.** The five language dictionaries shipped as one
  **369 KB-brotli** chunk (≈63% of first-load JS); a visitor reads one. Split
  `translations.ts` into `locales/{et,en,ru,lv,lt}.ts`, made Estonian eager (it is
  the default *and* the fallback) and the other four **lazy `import()`** chunks,
  with a `useSyncExternalStore` registry so `t()` stays synchronous and
  re-renders when a chunk lands. `vite.config` emits one chunk per language;
  `prerender-seo` injects a `modulepreload` per route so the active language is in
  flight before React renders. Result in the build: five `locale-*` chunks
  (~67–86 KB brotli each) instead of one 369 KB blob — a non-Estonian visitor
  drops ~70 KB+ of first-load JS and no visitor downloads four unused languages.
- **DemandLead index.** Added a composite `(Status, CreatedAt)` index + migration
  — the founder's most-hit screen filters Status and orders CreatedAt DESC and was
  a seq scan + sort.

Files: `translations.ts` (now a 24-line aggregate), `locales/*` (new),
`LanguageContext.tsx`, `vite.config.ts`, `scripts/prerender-seo.mjs`;
`RuumlyDbContext.cs` + migration `AddDemandLeadStatusCreatedAtIndex`.

### 5. Dead code

Deleted `MapPlaceholder.tsx`, `NavLink.tsx`, `LeadSummaryStrip.tsx` (zero
importers; one carried stale hardcoded city labels no translation guard covers).
Confirmed `useLeadSummary`/`crm.summary.*` are still used by
`ProviderIncomingOrders`, so those were **kept** — the bug-sweep's claim that they
were dead was wrong, and verifying it avoided breaking a live screen.

---

## Migrations added (apply on deploy)

- `20260820101324_AddProviderOutreachDecline` — 3 nullable columns on
  `ProviderOutreaches`.
- `20260820102948_AddDemandLeadStatusCreatedAtIndex` — one composite index.

Both applied to the local Docker DB and verified. Railway auto-migrates on
Production startup. Both are additive and safe.

---

## Change surface

- **Backend:** 12 files changed (+295/−27), 2 new files (`DeclineReasons.cs`,
  `QuoteDeclineTests.cs`), 2 migrations.
- **Frontend:** 22 files changed, 5 new files (`QuoteDecline.tsx`,
  `kit/SectionError.tsx`, `lib/csv.ts`, `lib/safeStorage.ts`, `locales/*`), 3
  deletions. `translations.ts` shows −21,757 lines because its content moved into
  `locales/` — no strings were lost (translation-coverage test green).

---

## Metrics moved

- **Provider-silence metric stops miscounting declines as silence** — the metric
  built to prove the outreach is failing was being fed a category of *success*.
- **First-load JS for a non-Estonian visitor: −70 KB+ brotli.**
- **Admin lead-queue query: index seek instead of seq-scan-plus-sort.**
- **Two whole classes of blank-page / lies-green failure removed** (storage-blocked
  browsers; API-down cockpit).

---

## Remaining weaknesses (in the plan, not done this round)

- Provider is still never told the outcome of their quote (S3) — the highest
  remaining lever on second-quote rate.
- No in-app "customer chose" / "quotes in draft" / "waiting on customer" surfaces
  (A2/A3) — the loop's weakest links are at its *end*.
- 226 directory rows with no contact email (S2) — bounded manual data work.
- Quote token never expires (S5); homepage still fetches 739 KB of `/api/locations`
  (perf 2.3); ~15 computed-key `t()` families still unguarded (B9).

## Remaining founder decisions

- **ET `sina`→`teie`** conversion across `provPage/quote/claim` (~30 strings) —
  approved in principle for provider-facing surfaces; needs a go before it ships.
- **What a provider pays if a job completes** — the marketing page's "pay only for
  results" line has no success-fee behind it; either build it or delete the claim.
- **The 15 public copy claims** flagged in the earlier public-surfaces audit, and
  the `OperatorLegalLine` footer text.
- **CSRF tightening on `/auth/refresh`** (0.9) — a security change; wants your
  explicit sign-off before it ships.
