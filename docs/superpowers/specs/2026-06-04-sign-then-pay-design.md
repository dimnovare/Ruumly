# Sign-then-Pay booking flow — Implementation Spec

**Date:** 2026-06-04 · **Decision: APPROVED — reorder the booking flow to sign the rental contract BEFORE paying.**

## Why (decision rationale)
A storage rental is a lease: money should not change hands for a rental nobody has agreed to.
- **Today (wrong order):** book → pay (Montonio) → sign (Dokobit). Leaves a "paid-but-unsigned" gap → refund risk + cash held for an agreement that doesn't exist.
- **Target:** book → **sign** → **pay**. The "signed-but-unpaid" failure state is far cheaper (no money to refund — just void the booking).
- Decisive for Ruumly: Estonian Smart-ID/Mobile-ID signing is low-friction/normal (kills the main anti-sign-first argument); it's a real rental; pre-launch (no conversion data to defend pay-first).

## Current flow (verified, `estonia-space-hub/src/pages/BookingPage.tsx`)
- 3-step wizard: `steps = [detailsAndExtras, contactAndAuth, paymentAndReview]` (line ~134).
- On step-2 submit (~238–303): creates booking (`createBooking`), then if method is bank/card calls `paymentService.initiate` and **redirects to Montonio** (`window.location.href = result.paymentUrl`, ~291).
- The **`ContractCta` → `ContractSigningModal`** (Dokobit) appears only AFTER, on the success/return screen (`ContractCta` def ~33; mounted ~415–426). `PaymentReturnPage.tsx` shows the contract step after the user returns from payment.
- Backend: booking created via `BookingsController` (status `Pending`/`Reserved`); signing via `ContractController` `POST /contracts/dokobit/initiate` (+ postback `…/callback`); `MontonioPaymentService` has a `// TODO(cleanup)` for expiring abandoned `AwaitingPayment` invoices.

## Target flow
`Details/extras → Contact/auth → **Review & SIGN rental agreement** (eID; booking already created as Pending) → **Pay deposit & first month** (Montonio) → Confirmation/access.`
Frame steps "sign" + "pay" as ONE continuous **"Finalise your rental"** sequence (sign flows straight into payment), not two separate hurdles.

## Required changes

### A. Frontend wizard reorder (`BookingPage.tsx`, `ContractSigningModal.tsx`, `PaymentReturnPage.tsx`)
- Create the booking **before** signing (it already is created pre-payment — keep `Pending`).
- Insert a **"Review & sign"** step **before** the payment step: render the contract (provider's active template, filled) and run the Dokobit signing flow (reuse `ContractSigningModal` / the `dokobit/initiate`→sign logic) **inside the wizard**, before the Montonio redirect.
- Only after the contract is **signed** do we proceed to the Montonio payment step/redirect.
- Remove the post-payment `ContractCta` path (signing now happens earlier); keep `PaymentReturnPage` showing the final confirmation only.
- Handle the "pay-later/rebate" model path: sign → (no immediate Montonio) → done (invoice later).
- Loading/error/back states preserved; no white screens.

### B. Backend safeguard — signed-but-unpaid auto-void (CRITICAL)
- A booking that is **signed but not paid** must NOT linger. Add/finish a **pending-booking expiry** (Hangfire recurring job): bookings in `Pending`/`AwaitingPayment` older than a short window (e.g. 2–24 h, configurable) are **cancelled** and any attached `SignedContract` is marked **void** (add a `Void`/`Cancelled` status or reuse `Status`). No money is involved, so just void — no refund path.
- The rendered contract text must state the rental is **conditional on payment within X** (so a signed-unpaid contract binds no one). Add this clause to the token vocabulary / template guidance (or a standard preamble).
- Ensure booking stays `Pending` until the Montonio webhook confirms payment (already the case — verify).

### C. Content + i18n
- Reword `HowItWorksPage.tsx` to the new order: (1) search (2) book dates (3) **sign your rental agreement** (4) **pay deposit & first month** (5) move in. Update `hiw.*` + the HowTo JSON-LD step order.
- Update booking step labels (`booking.*` step names) + any "pay then sign" copy + the trust box ("digital contract after payment" → "sign then pay securely"). All in 5 languages (et/en/ru/lv/lt), equal key counts.

### D. Tests
- Update e2e `09-booking.spec.ts` + `10-contract.spec.ts` for the new order (sign step before payment). Keep 70/70 green. (Fixtures in `e2e/fixtures.ts`; CI needs `VITE_API_URL` pinned — already done in `playwright.config.ts`.)
- Backend tests: cover signed-but-unpaid → auto-void; booking stays Pending until paid; existing idempotency tests still pass.

## Parallelization plan (for the implementing session)
Decompose into bounded, mostly-disjoint agents; mind the shared seams:
- **Agent 1 — Backend safeguard (B):** Hangfire expiry job + `SignedContract` void status + booking-cancel propagation + the conditional-on-payment clause. Owns `BookingService`, the job, `SignedContract` model/migration. Backend tests.
- **Agent 2 — Frontend wizard (A):** reorder `BookingPage.tsx`, integrate signing pre-payment, success/return states. Owns `BookingPage.tsx`, `PaymentReturnPage.tsx`, the booking-flow part of `ContractSigningModal.tsx`.
- **Agent 3 — Content + i18n (C):** `HowItWorksPage.tsx` reword + JSON-LD + `hiw.*`/`booking.*` copy in 5 langs. Owns those + `translations.ts` (coordinate: it's the shared file — additive only, pull --rebase).
- **Agent 4 — E2E + verification (D):** update `09`/`10` specs after 1+2 land; run the full suite to 70 green.
SEAMS: `ContractController.dokobit/initiate` is reused as-is; the frontend (Agent 2) codes against it. `translations.ts` touched by Agent 3 only for new keys. Run a final integration pass (tsc + `npm run test:e2e` + `dotnet build`) before declaring done.

## Acceptance criteria
1. A customer **cannot reach Montonio payment without a signed contract**.
2. A **signed-but-unpaid** booking auto-voids (booking cancelled + contract void) with no money held.
3. `How it works` and all booking copy reflect **sign → pay** (5 languages, parity).
4. `npx tsc --noEmit` clean; **E2E 70/70 green**; backend builds clean (ignore MSB3492); backend tests green (the 2 EF-InMemory skips remain).
5. Commit per agent, push at the end.

## Constraints (carry over)
- Frontend check: `npx tsc --noEmit` from `estonia-space-hub/`. Backend: `dotnet build --no-restore` (ignore MSB3492). Run EF/dotnet from `Ruumly.Backend/`. Local Postgres on host port 5433.
- Storage-only launch — no Moving/Trailer copy. Translations equal across 5 languages.
