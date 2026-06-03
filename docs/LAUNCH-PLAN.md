# Ruumly — Launch Execution Plan

> **Purpose:** finish the product so every flow is easy, logical, and *boringly reliable* —
> able to run almost autonomously. This is the tactical execution plan; `ROADMAP.md` remains
> the strategic plan. When the two disagree, this one wins for "what to build next."
>
> **Anchor date:** a real company connects production **Montonio** from **June 9**. Everything
> around payments must be ready so that day is *plug-in-credentials-and-test*, nothing more.
>
> **Scope discipline (storage-only launch):** Moving/Trailer stay hidden behind
> `showMovingService`/`showTrailerService` (done). No Partner Value Bundle, no DIY QES, no
> Next.js migration, no neighbourhood content pages yet. The gate to revenue is **supply +
> trust + working payments**, not features.

Ownership tags: **[CC]** Claude Code · **[Founder]** Dim · **[Ext]** external (lawyer / eID Easy / Montonio company)

---

## 0. Definition of "boringly reliable"

A flow is DONE only when **all** of these hold:

1. **Happy path** is covered by a committed E2E test that runs green.
2. **Every failure branch** is handled in the UI (clear message, no dead-end) — not a white screen.
3. **Money/state operations are idempotent** (double-submit, duplicate webhook, refresh mid-flow → no double charge, no duplicate booking).
4. **Failures alert someone** (Sentry event + admin-visible) instead of failing silently.
5. **A human can recover** via an admin override or manual fallback if automation breaks.

If a flow can't tick all five, it is not finished.

---

## 1. The three flows that must be bulletproof

### Customer flow
`discover (home/search/SEO) → listing/location detail → login or inline-auth → booking (date, extras, contact) → payment (Montonio) → contract sign → confirmation → manage in account`

### Partner flow
`public apply → admin approve → create locations/units → publish → receive booking/lead → manage order → get paid (payout) → sign partner contract`

### Admin flow
`review applications → approve → manage listings/locations → orders/routing → payouts/rebates → settings → audit`

Each phase below hardens one or more of these.

---

## PHASE A — Revenue path bulletproof + provable  (now → Jun 8) · BLOCKER

Goal: on Jun 9, connecting real Montonio keys is the *only* remaining step.

- [ ] **[CC] Payment-path failure audit.** Review `MontonioPaymentService` + `PaymentsController.Webhook` (JWT-verified) + the booking→order→invoice→payout chain. Enumerate and handle: failed init, user abandons at checkout, duplicate/replayed webhook, webhook before return, timeout, refund/cancellation, signature-verification failure.
- [ ] **[CC] Idempotency proof.** Confirm the existing booking idempotency key + advisory-lock overlap prevention cover: double-click submit, browser back+resubmit, duplicate webhook. Add tests that try to break them.
- [x] **[CC] Manual-invoice fallback.** ✅ 2026-06-03 — `POST /api/admin/invoices/{id}/mark-paid` in `AdminRefundsController.cs` + "Mark paid (wire transfer)" button in `AdminOrders.tsx` order detail dialog.
- [ ] **[CC] E2E smoke suite (critical path).** Commit Playwright specs: search → listing → register/login → booking → payment (sandbox/stub) → contract → provider onboarding → admin approve → publish. Wire to run on demand + pre-deploy.
- [x] **[CC] Failure alerting.** ✅ 2026-06-03 — `SentrySdk.CaptureException` added to `BookingsController` (booking create) and `PaymentsController` (3 capture points in Montonio webhook). Stuck-orders admin view was already live.
- [ ] **[Founder] Montonio sandbox test** end-to-end before the 9th using test credentials, if available.

**Done when:** the full booking→payment→contract→confirmation path passes E2E on sandbox, every failure branch is handled, and a failed payment is recoverable manually.

---

## PHASE B — Trust + signature  (parallel with A, finish by launch)

Goal: remove the "this isn't a real business" signals. This is the highest-leverage non-payment work.

### B1 — Signature & identity via SK Smart-ID / Mobile-ID

> **2026-06-03 DECISION: Dokobit scrapped.** Replaced with Smart-ID/Mobile-ID
> ported directly from the Rentaro project (`Ruumly.Backend/Identity/`).
> Smart-ID provides _identity verification_ (customer proves who they are via
> SK RP-API v2); canvas acknowledgment remains the "signature" with verified
> identity attached. Zero DB migration needed — SignedContract already has all
> required fields.
>
> **Activation:** set `SMARTID__RELYINGPARTYUUID` + `SMARTID__RELYINGPARTYNAME` +
> `SMARTID__BASEURL` + `SMARTID__HMACSECRET` in Railway. Feature is fully
> env-gated — zero code change required.

- [x] **[CC] Reframe the canvas signature.** ✅ Already done (prior session) — `ContractSigningModal` shows "self-declared, not verified" note on canvas path. `acknowledgmentTitle` + `selfDeclaredNote` translations in all 5 languages.
- [x] **[CC] Backend: Smart-ID/Mobile-ID identity scaffold.** ✅ 2026-06-03 — `Ruumly.Backend/Identity/` with `SmartIdProvider`, `MobileIdProvider`, `IdentityVerificationService`. New endpoints: `POST /contracts/identity/start`, `GET /contracts/identity/{sessionId}`. `signing-method` returns `{dokobitEnabled, smartIdEnabled, mobileIdEnabled}`. `sign` endpoint extended to accept `signingMethod` + `verifiedSessionId`.
- [x] **[CC] Frontend: Smart-ID signing UX.** ✅ 2026-06-03 — `SmartIdSigningFlow` component in `ContractSigningModal.tsx`: method selector, 4-digit verification code display, polling, auto-fills verified name, graceful error states. Env-gated via signing-method query.
- [x] **[CC] Strengthen the evidence model** on `SignedContract`. ✅ Already done (prior session) — `SigningMethod`, `VerifiedName`, `VerifiedIdCode`, `RenderedHtmlHash`, `DokobitSigningToken`, `Status` all present.
- [ ] **[Founder] Obtain SK credentials** — contact SK ID Solutions (sk.ee) for a Relying Party UUID. Set `SMARTID__RELYINGPARTYUUID` etc. in Railway. Test one end-to-end verification with a real Smart-ID user once keys arrive.

### B2 — Trust layer (cheap, fast, high-impact)
- [x] **[CC] Footer + trust points.** ✅ 2026-06-03 — footer already shows `Ruumly OÜ · reg. 16812345` (placeholder — founder must replace `16812345` with real Äriregister code in `translations.ts`, 5 occurrences tagged `// TODO`). Support email `info@ruumly.eu` visible.
- [x] **[CC] Cancellation/refund clarity** + payment explainer. ✅ 2026-06-03 — 3-point trust box added to `BookingPage.tsx` above checkout button: secure payment via Montonio / digital contract after payment / 24h cancellation policy link.
- [ ] **[CC] "Verified partner" rule** shown explicitly; partner logos only if truly active.

**Done when:** no surface claims identity it didn't verify; a visitor can see who they're dealing with, how to get help, and what happens if they cancel.

---

## PHASE C — Flow polish: easy + logical  (overlaps B)

- [ ] **[CC] Customer booking flow audit.** Walk each step for friction; ensure progress indication, reassurance near payment/signature, clean inline-auth, and a clear success state with next step.
- [x] **[CC] Partner onboarding/activation.** ✅ Done (prior session) — bulk listing import (CSV paste + preview) in `AdminLocations.tsx`; backend `BulkImportUnits` in `LocationsController.cs`; provider activation checklist with readiness score meter. Publish endpoint and publish-readiness endpoint working.
- [ ] **[CC] Admin approve → publish** path solid and obvious (it's the gate that turns supply into live inventory).
- [x] **[CC] Empty/thin-inventory states.** ✅ 2026-06-03 — `SearchPage.tsx` demand lead capture fixed (3 bugs: wrong city value, missing category/language, form shown without city context). Notify-me form properly wired to `POST /api/auth/notify-interest`.

**Done when:** a non-technical partner can go from "yes" to a published, photographed listing with founder help in well under an hour, and a customer can book without confusion.

---

## PHASE D — Go-live  (Jun 9 → launch)

- [ ] **[Ext/Founder] Connect production Montonio** credentials (Railway env).
- [ ] **[Founder] One real euro** end-to-end: real booking → checkout → webhook → Order → Invoice → PayoutEntry. Verify both success and failure paths.
- [ ] **[Founder] Partner #1 live** with real, photographed units (the operator who already agreed).
- [ ] **[CC] Pre-launch checklist pass:** run the E2E suite against production-like; smoke-check sitemap/robots/GSC (already healthy), maintenance toggle, email deliverability.

**Done when:** one real euro has moved end-to-end and partner #1's inventory is live and bookable.

---

## PHASE E — Autonomy + after launch

Make it run without babysitting, then grow.

- [ ] **[CC] Ops hardening:** error-budget dashboard, alerting on the booking/payment/contract paths, a "stuck states" admin view, backup/restore drill, migration rollback note.
- [ ] **[Founder] Review loop** after each fulfilled booking; **partner referral loop** after first successful lead.
- [ ] *(Later, post-traction only)* content SEO (Tallinn storage pages), partners #2–10, then the Partner Value Bundle (invoicing → GBP automation). **Not before real bookings recur.**

---

## NOT NOW (explicitly out of scope for launch)

- Partner Value Bundle (e-invoicing, GBP automation) — post-traction.
- DIY Smart-ID/Mobile-ID **qualified** signing / ASiC-E containers — buy auth via aggregator instead.
- Next.js / SSR migration — Worker + sitemap already cover crawler/SEO basics; revisit at real traffic.
- Neighbourhood content pages — parked by decision.
- Subscription-tier selling — economics show paid tiers are irrational below ~€1,225/mo routed GMV per partner. Sell Free.

---

## Risk register (top failure modes → mitigation)

| # | Failure mode | Mitigation in this plan |
|---|---|---|
| 1 | Partners don't sign up | Phase C concierge + bulk import; founder sales (not code) |
| 2 | Partners sign but don't activate | Phase C onboarding meter + admin publish path + bulk import |
| 3 | Payments break / can't move money | Phase A hardening + manual-invoice fallback + Phase D euro test |
| 4 | Low booking conversion | Phase B trust layer + Phase C flow polish + real inventory |
| 5 | Signature/contract looks fake → distrust | Phase B1 reframe + identity-backed signing |
| 6 | Silent breakage in prod | Phase A alerting + Phase E ops dashboard |

**The honest gate:** none of this creates revenue by itself. Supply (signing + activating partners) is the bottleneck. This plan's job is to make "yes" easy and make the machine trustworthy enough to transact — so founder sales can do the rest.

---

## Suggested execution order (for Claude Code)

1. **A:** payment-path hardening → idempotency proof → manual fallback
2. **A:** E2E smoke suite (locks in #1 so it can't regress)
3. **B1:** signature reframe (immediate) → evidence model → identity integration
4. **B2 + C:** trust layer + flow polish (parallelizable)
5. **A:** failure alerting → **E:** ops dashboard
6. **D:** go-live checklist (gated on Montonio creds, Jun 9)
