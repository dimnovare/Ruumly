# Overnight QA — 2026-06-04 (sign-then-pay launch readiness)

Autonomous QA run requested ~22:30 GMT+3, continue until ~08:30. User asleep — no prompts.
Goal: test everything (every process, button, UI element, desktop + mobile), fix bugs found, verify, commit.

## Environment (verified)
- **CLIs authed**: railway (dim.novare@gmail.com), vercel (dimnovare-9994), wrangler (dim.novare@gmail.com), gh.
- **Prod frontend**: https://ruumly.eu  (Vercel project `estonia-space-hub`; auto-deploys on push to `main`). My sign-then-pay push deployed ~15m before this run.
- **Prod API**: https://api.ruumly.eu — health 200. Route prefix is **`/api`** (e.g. `/api/settings/public`). Railway project `protective-compassion`, service **Ruumly** (also: Postgres, gotenberg). Backend deployed clean, "No migrations applied — DB up to date" (my change added none). Smart-ID DISABLED in prod.
- **Worker**: `ruumly-social-preview` on `ruumly.eu/*` (Cloudflare). API_BASE_URL=https://api.ruumly.eu, SITE_URL=https://ruumly.eu.
- **Prod has NO data**: `SEED_DEMO_DATA` unset → `/api/listings` empty, `/api/locations/cities` empty. Deep flows must be tested locally.
- **Invite code** `RUUMLY2026` gates registration only (not browsing).

## Local stack (for data-rich QA)
- Local Postgres: docker `ruumly-postgres` on host :5433 (up).
- Backend dev: `localhost:3000`, ASPNETCORE_ENVIRONMENT=Development, conn=5433/ruumly. Start with `SEED_DEMO_DATA=true`.
- Frontend dev: `npm run dev` → :5173, `.env` VITE_API_URL=http://localhost:3000/api (aligned).
- Browse binary: /c/Users/Dmitri.MARKIT/.claude/skills/gstack/browse/dist/browse
- Canvas signing works locally (Dokobit/Smart-ID disabled → canvas fallback), so the sign gate is testable without external eID.

## Safety rules (overnight)
- NO real payments (Montonio) or real eID signings against prod.
- NO risky prod deploys without full verify (tsc + e2e + build). Pushing `main`/`master` auto-deploys — gate every push.
- Backend repo = outer `Ruumly` (master). Frontend repo = `estonia-space-hub` (main). Two separate repos.

## Baseline (already green before QA)
- Frontend: `tsc --noEmit` clean; e2e 70/70.
- Backend: build clean; tests 145 passed / 2 skipped.

## Test plan / coverage tracker
- [ ] Local stack up (backend :3000 seeded, frontend :5173)
- [ ] Home page (desktop + mobile) — render, console, links, i18n
- [ ] Language switching (et/en/ru/lv/lt) — all routes prefixed
- [ ] Search page — listings, filters, empty states, map/list
- [ ] Listing detail — gallery, pricing, CTA
- [ ] Booking wizard step 0 (details+extras), step 1 (contact+auth), step 2 (review+payment)
- [ ] Sign gate (NEW) — opens after confirm, modal review→sign, canvas sign
- [ ] proceedAfterSign → payment-initiate path / pay-later path
- [ ] Sign-cancelled state + re-sign
- [ ] How-it-works — sign→pay order live, JSON-LD, 5 langs
- [ ] FAQ, Contact, Partner/onboarding application
- [ ] Auth: login, register (invite code), email verification gate
- [ ] Account/bookings pages
- [ ] Admin panel (login-gated)
- [ ] Provider portal (login-gated)
- [ ] Footer, nav, 404, legal pages
- [ ] OG / social preview worker (crawler UA)
- [ ] Mobile sticky bars, responsive breakpoints

## FINAL STATUS (end of overnight pass)
Tested (local full-stack, demo data + live prod reads, desktop+mobile, browse headless):
- Sign-then-pay flow end-to-end (gate enforces order; backend guard passes post-sign; copy live in et). ✓
- Storage-only correctness across home/search/map/featured/stats. Found + FIXED a real leak. ✓
- How-it-works sign→pay order incl. HowTo JSON-LD. ✓
- Pages render clean (home, search, listing detail, how-it-works, faq, contact, about, partners, blog, 404): no crashes, no real console errors. ✓
- Mobile responsive (home/search/how-it-works @375): clean. ✓
- Language switch (en) renders, correct geography. ✓
- Admin panel (all /api/admin/* 200) + provider portal (no API errors): load clean. ✓
- **Auto-void safeguard verified END-TO-END live:** set expiry window=1h, backdated 2 signed-but-unpaid Pending bookings (canvas-signed, payment-failed) to 3h old, triggered StaleBookingCleanupJob via Hangfire → both bookings → Cancelled, both SignedContracts → "void"; log "1h window: 2 cancelled, 2 contracts voided". Configurable window honored (guard requires hours>0). No money involved. Test setting cleaned up after. ✓ (Also covered by Agent 1's passing unit tests in the 145.)
- Payment-initiate backend guard: confirmed live — /payments/initiate returned 500 (local Montonio missing) NOT 409, i.e. a completed contract satisfies the guard; pre-sign it would 409 CONTRACT_NOT_SIGNED. ✓

**Fixes committed + pushed (estonia-space-hub main → auto-deploys Vercel):**
- b30266e fix(booking): signed-but-payment-failed ≠ not-signed (+3 i18n keys ×5 langs)
- aa88782 fix(storage-only): stop Moving/Trailer leaking into search + home
- ea6d6f8 fix(storage-only): TrustBar stats exclude hidden types
- fb7aebf test(e2e): widen login-redirect poll to 15s (cold-start)
All gated: tsc clean, e2e 70/70 (clean CI server), live-verified. Backend untouched (still 145/2).

**For the user in the morning (NOT auto-fixed — need your call):**
- **H1 (launch-critical):** mandatory sign means a supplier with NO active contract template = UNBOOKABLE listings (dead-end at gate). Demo data: only the Kookon supplier has a template. PROD has 0 listings today so no live impact, but on supplier onboarding this bites. Recommend: platform-default rental template fallback, or enforce a template on supplier activation, + a clearer no-template dead-end UX.
- L1 sign-done modal button says "Sulge"/Close not "Continue to payment". L2 contract Dialog a11y (DialogTitle/aria-describedby). L3 payment-fail toast text English. Detail pages /et/moving/:id, /et/trailer/:id still deep-linkable when service disabled (recommend 404/redirect).
- **L4 (medium): conditional-payment clause only GUARANTEED on the Dokobit path.** `ContractController.InitiateDokobitSigning` calls `docService.AppendClause(...)` so the eID/Dokobit path always binds the "void if unpaid in {N}h" clause (this is Estonia's prod signing path ✓). The canvas/preview path only gets the clause if the provider template includes the `{{payment_condition_clause}}` token. So a canvas-fallback sign on a token-less template yields a signed contract WITHOUT the clause. Recommend: AppendClause on the preview/canvas path too, so the protection is universal regardless of signing method/template. (Clause itself is unit-tested by Agent 1; verified present in code, not live-grep-able locally since Dokobit is disabled.)

## Findings (running log)

### CONFIRMED GOOD (sign-then-pay, verified live on local stack)
- Sign gate enforces order: Confirm → booking created Pending → sign modal opens, **no Montonio redirect / no payment** until signed. URL stays on booking page.
- Full path works: canvas sign → `POST /contracts/sign` 200 → `proceedAfterSign` → `POST /payments/initiate`. Backend guard **passed** (got 500 from missing local Montonio creds, NOT 409) — confirms a completed contract satisfies the guard.
- New copy LIVE + correct (et verified): step label "Vaata üle ja allkirjasta"; trust "Allkirjasta esmalt rendileping, seejärel maksa turvaliselt"; reassurance "Tasu ei võeta enne allkirjastamist · …"; mini-steps "1 Allkirjasta · 2 Maksa · 3 Koli sisse".
- Storage-only flags OFF locally (`showMovingService=false, showTrailerService=false`). API does NOT server-filter moving/trailer (frontend must) — **still need to verify search UI hides them**.

### H1 (HIGH, launch-readiness, NOT a code bug): template-less suppliers → unbookable
With sign now mandatory, a listing whose supplier has NO active contract template dead-ends at the gate: modal shows "Lepingumall pole saadaval", continue stays disabled, only Tühista/Close. Customer cannot sign → cannot pay. In demo data only 1 of the storage suppliers (Kookon, 33b7aeba) has an active template; e.g. BalticBox Center (eede7a96) has none → unbookable. Prod has 0 listings today so no live impact YET, but every onboarded supplier MUST have a template or their listings can't convert. RECOMMEND: platform-default rental template fallback OR enforce template on supplier activation + improve the dead-end UX. Flagged to user; not unilaterally building the fallback overnight.

### B1 (FIXED, committed b30266e): post-sign payment failure showed "you haven't signed"
After a successful sign, a failed `payments/initiate` fell into the signCancelled screen ("Sa pole veel allkirjastanud" + re-sign CTA) — wrong; contract IS signed. Fixed: new `paymentFailed` state + "Rendileping allkirjastatud — payment couldn't start, retry payment" screen + Retry button that re-calls initiate (no re-sign). Verified live + e2e 70/70 + tsc clean.

### B2 (FIXED, committed aa88782): Moving/Trailer leaked into search + home (storage-only violation)
Storage-only hides Moving/Trailer, but only the filter TABS were gated. The search RESULT list, the map markers, the homepage featured cards, and the derived counts still rendered moving/trailer (reachable /et/moving/.., /et/trailer/.. detail pages) — the API returns all types and nothing post-filtered by the flags. Verified live: search showed 71 results incl. 17 moving/trailer; homepage featured leaked too.
Fix: filter results/map/featured/counts by showMovingService/showTrailerService; show filtered count (search now "54 pakkumist leitud" = warehouse only); FALLBACK defaults flipped to false (no flash/leak if settings API slow). Verified live: 0 moving/trailer links on home+search; tsc clean; e2e 70/70 (warm server). NOTE: detail pages /et/moving/:id and /et/trailer/:id are still directly reachable by deep link (not discoverable). RECOMMEND: 404/redirect those routes when the service is disabled (follow-up, not done tonight).

### Note on e2e flake
03-auth.spec.ts:100 (login navigates away from /login, 5s poll) fails on a COLD Vite dev-server start; passes 70/70 against a warm server. Pre-warm the /login route or bump the poll timeout for cold CI starts. Not a code regression (fails with my changes stashed too).

### LOW / polish (not yet fixed)
- L1: sign-done modal proceed button says "Sulge" (Close), not "Continue to payment" — user may not realize it triggers payment. `booking.sign.continueToPayment` exists but unused there (shared modal; gate-specific label needs care).
- L2: a11y — contract Dialog missing DialogTitle / aria-describedby (Radix warnings in console).
- L3: payment-fail toast text is English ("Payment initiation failed…") from raw err.message; main screen is localized. Consider always using t("booking.errorPayment").

### INFO (local-env only, verify not present in prod)
- Google Sign-In button 403 + "[GSI_LOGGER] origin not allowed" on localhost:8080 (OAuth origins are for ruumly.eu). Benign locally.
- payments/initiate 500 locally = Montonio creds not set (MONTONIO__ACCESSKEY/SECRETKEY). Expected; not a bug.

### Credentials (local demo, password demo1234)
customer andres@email.com · admin peeter@ruumly.eu · provider maria@laopind.ee. Templated storage listings: Kookon supplier (e.g. 2062e19a, 7cf9eadc).
