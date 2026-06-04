# Montonio Go-Live Runbook (Jun 9)

> The single hard revenue blocker. On the day, this should be **plug-in-credentials-and-test** —
> nothing more. The code path is already hardened (idempotency, failure branches, Sentry alerts,
> manual-invoice fallback). This runbook is the exact sequence.

**Railway project:** `protective-compassion` (= Ruumly). **Backend service:** `Ruumly`.
**Domains:** `api.ruumly.eu` (backend), `ruumly.eu` (frontend, Vercel).

---

## 0. Prerequisites (have these ready)
- Montonio **production** Access Key + Secret Key (from the Montonio partner dashboard).
- The Montonio account configured with the correct **payout bank account** + **return/notify URLs**
  whitelisted (do this in the Montonio dashboard).
- A real payment method for the €1 test (or Montonio's smallest live amount).

## 1. Set production env vars on the `Ruumly` backend service
The code reads these (config key → env var):

| Config key | Env var | Value |
|---|---|---|
| `Montonio:AccessKey`  | `MONTONIO__ACCESSKEY`  | *(production access key)* |
| `Montonio:SecretKey`  | `MONTONIO__SECRETKEY`  | *(production secret key)* |
| `Montonio:UseSandbox` | `MONTONIO__USESANDBOX` | **`false`** (→ uses `https://api.montonio.com`) |
| `Montonio:ReturnUrl`  | `MONTONIO__RETURNURL`  | the post-payment return page, e.g. `https://ruumly.eu/et/payment/return` |
| `Montonio:NotifyUrl`  | `MONTONIO__NOTIFYURL`  | the webhook endpoint that `PaymentsController` listens on (confirm the exact route, e.g. `https://api.ruumly.eu/api/payments/montonio/webhook`) |

```bash
# CLI must be linked to the RUUMLY project first:
railway link --project protective-compassion --environment production
railway variables --service Ruumly \
  --set "MONTONIO__ACCESSKEY=..." \
  --set "MONTONIO__SECRETKEY=..." \
  --set "MONTONIO__USESANDBOX=false" \
  --set "MONTONIO__RETURNURL=https://ruumly.eu/et/payment/return" \
  --set "MONTONIO__NOTIFYURL=https://api.ruumly.eu/api/payments/montonio/webhook"
```
Setting variables triggers a redeploy. **Re-link the CLI back to your other project afterward if needed.**

## 2. Confirm the deploy is healthy
- `railway status` → `Ruumly` deploy `SUCCESS`.
- `curl https://api.ruumly.eu/health` → `200 ok`.
- Quick: `railway variables --service Ruumly --kv | grep MONTONIO` shows the 5 keys.

## 3. The one-euro test (happy path) — the real gate
Do a real booking end-to-end on `ruumly.eu`:
1. Book a real listing → reach the Payment step → pay via Montonio with a real card/bank (€1 / smallest).
2. Confirm the Montonio checkout opens (production), complete payment.
3. Verify the **webhook fired** and the chain advanced: **Order** created → **Invoice** marked Paid →
   **PayoutEntry** created. Check the admin **Orders / Ops** views and Sentry (no errors).
4. Verify the customer returns to the return URL and sees the success state + receives the
   confirmation email (and the contract-ready step).

## 4. Verify the failure + recovery branches
- **Abandoned checkout:** start a booking, close Montonio without paying → booking stays `Pending`/
  `AwaitingPayment`, no Order/Invoice created, no double-charge. (Pending bookings expire later.)
- **Duplicate/replayed webhook:** already guarded — a second webhook for a paid invoice is a no-op
  (returns 200, no second invoice/payout). The €1 test exercises the real one; trust the integration tests.
- **Manual-invoice fallback:** if a webhook ever fails to land, an admin can mark an invoice paid via
  **Admin → Orders → order detail → "Mark paid (wire transfer)"** (`POST /api/admin/invoices/{id}/mark-paid`).

## 5. Pre-launch smoke (Phase D)
- E2E suite green (CI). `curl` checks: `/health` 200, `sitemap.xml` 200, `robots.txt` 200, homepage 200,
  OG card storage-only. (All confirmed green 2026-06-04.)
- Maintenance toggle works (admin settings) — keep OFF.
- Email deliverability: confirm Resend is sending (booking confirmation, contract-ready, supplier welcome).
- Partner #1: real, photographed, **published** units are live and bookable.

## 6. Rollback (if payments misbehave)
- Fastest: set `MONTONIO__USESANDBOX=true` (reverts to sandbox; stops real charges) and investigate,
  **or** Railway → `Ruumly` service → Deployments → **redeploy the previous SUCCESS deployment**.
- Customers mid-flow are safe: bookings without a paid invoice stay `Pending` (no charge, no order).
- DB rollback only if a migration is implicated — see `docs/ops/migration-rollback.md`.

**Done when:** one real euro has moved end-to-end (Order → Invoice Paid → PayoutEntry), both success and
failure paths behave, and partner #1's inventory is live and bookable.
