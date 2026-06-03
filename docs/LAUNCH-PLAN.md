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
- [ ] **[CC] Manual-invoice fallback.** A documented, admin-triggerable wire-transfer path so a Montonio hiccup never blocks a booking. Verify it produces a valid Invoice + PayoutEntry.
- [ ] **[CC] E2E smoke suite (critical path).** Commit Playwright specs: search → listing → register/login → booking → payment (sandbox/stub) → contract → provider onboarding → admin approve → publish. Wire to run on demand + pre-deploy.
- [ ] **[CC] Failure alerting.** Sentry alerts specifically on booking-create and payment-callback failures; surface a "stuck/failed orders" view in admin.
- [ ] **[Founder] Montonio sandbox test** end-to-end before the 9th using test credentials, if available.

**Done when:** the full booking→payment→contract→confirmation path passes E2E on sandbox, every failure branch is handled, and a failed payment is recoverable manually.

---

## PHASE B — Trust + signature  (parallel with A, finish by launch)

Goal: remove the "this isn't a real business" signals. This is the highest-leverage non-payment work.

### B1 — Signature & identity via Dokobit Documents Gateway

**Decision: Use Dokobit Documents Gateway** (https://www.dokobit.com/et/lahendused/dokumentide-api).
Sandbox: https://gateway-sandbox.dokobit.com · SDK: https://github.com/dokobit
Sandbox access will be obtained by the Founder; Claude Code prepares the full integration as
plug-and-play so it activates the moment sandbox/production tokens arrive.

- [ ] **[CC] Reframe the canvas signature now.** Relabel `ContractSigningModal` + terms as an
  *acknowledgment of terms*, not identity verification. Stop implying the typed ID code is verified.
  One-session change — kills the "fake" feeling immediately, ships before Dokobit is wired up.
- [ ] **[CC] Backend: Dokobit integration scaffold.** Add `DokobitService` (C#) using the Documents
  Gateway REST API: create document, upload rendered contract HTML→PDF, invite signer (Smart-ID /
  Mobile-ID / ID-card), poll/webhook for completion, download signed container (ASiC-E or PDF).
  All secrets (`DOKOBIT_API_TOKEN`, `DOKOBIT_BASE_URL`) as Railway env vars; default to sandbox URL.
  The service must be fully functional with sandbox credentials and require **zero code change** to
  switch to production.
- [ ] **[CC] Backend: ContractController sign endpoint upgrade.** When `DOKOBIT_API_TOKEN` is present,
  route the sign request through Dokobit instead of the canvas flow. When absent (env not set),
  fall back to the existing canvas acknowledgment — so dev/staging always works without the token.
- [ ] **[CC] Frontend: Dokobit signing UX.** Replace the canvas step with a "Sign with Smart-ID /
  Mobile-ID / ID-card" selector when Dokobit is enabled. Poll the backend until signing completes,
  show a clear progress state, handle decline/timeout/error gracefully.
- [ ] **[CC] Strengthen the evidence model** on `SignedContract`: add `DokobitDocumentId`,
  `DokobitSigningUrl`, `SigningMethod` ("smartid"|"mobileid"|"idcard"|"canvas"), SHA-256 hash of
  `RenderedHtml`, and `VerifiedName`/`VerifiedIdCode` (populated by Dokobit on success, null for
  canvas). This upgrades the record to carry a real identity assertion when Dokobit is used.
- [ ] **[Founder] Obtain Dokobit sandbox token** and add as Railway env var `DOKOBIT_API_TOKEN` +
  `DOKOBIT_BASE_URL=https://gateway-sandbox.dokobit.com`. Test one end-to-end signing with real
  Smart-ID once the code ships.
- [ ] **[Ext/Founder] Confirm Dokobit production pricing** and sign up; swap env var to production URL.

### B2 — Trust layer (cheap, fast, high-impact)
- [ ] **[CC] Footer + trust points:** legal entity name + registry code, named support contact (real person).
- [ ] **[CC] Cancellation/refund clarity** + a short "how payments & contracts work" explainer near booking.
- [ ] **[CC] "Verified partner" rule** shown explicitly; partner logos only if truly active.

**Done when:** no surface claims identity it didn't verify; a visitor can see who they're dealing with, how to get help, and what happens if they cancel.

---

## PHASE C — Flow polish: easy + logical  (overlaps B)

- [ ] **[CC] Customer booking flow audit.** Walk each step for friction; ensure progress indication, reassurance near payment/signature, clean inline-auth, and a clear success state with next step.
- [ ] **[CC] Partner onboarding/activation.** Make it concierge-friendly. **Bulk listing import** (paste/CSV) so onboarding a 20-unit operator takes minutes (directly removes failure mode #3 "partners don't activate"). Onboarding completion meter.
- [ ] **[CC] Admin approve → publish** path solid and obvious (it's the gate that turns supply into live inventory).
- [ ] **[CC] Empty/thin-inventory states** never look fake or broken (graceful copy, "notify me," nearby suggestions).

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
