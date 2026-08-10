# Concierge ops playbook — the manual demand loop

> "Пока нет устойчивого потока заявок, никакая автоматизация не имеет значения." — S. Anikin
>
> The product is now a demand-first concierge. The software (admin match queue) supports
> a loop that is **run by a human**. This document is that loop.

## The promise
- **Customer:** tell us what you need (moving / storage / trailer) — we send you 2–3
  relevant local offers, usually within 24h. Free, no obligation.
- **Supplier:** we don't sell placement. We bring people who are looking *right now*.

## The exact state flow (v2 guided workspace — 2026-07)
```text
New       -> outreach sent          -> Contacted
Contacted -> offer sent             -> Quoted / Offer Sent
Quoted    -> customer opens link    -> Quoted / Offer Viewed
Quoted    -> customer requests opt. -> Quoted / Offer Chosen   (a PREFERENCE, not a booking)
Quoted    -> admin confirms provider-> Converted               (the only booked outcome)
```
A customer selecting an option is a **pending preference** — the lead stays **Quoted** and
Ruumly is alerted. It becomes **Converted (=Booked)** only when *you* click **Confirm with
provider and mark booked**, after the provider confirms availability. `Converted` is no
longer clickable in the status pipeline; only booking-confirmation sets it.

## Daily cadence (~30 min, morning + evening check)
1. Open **Admin → Leads** (`/admin?tab=leads`). New requests arrive with status **New**
   (also emailed to the ops inbox). Expand the lead → the guided **3-stage workspace**.

2. **Stage 1 — Find & contact providers** (first touch target: **same day**):
   - Provider search shows **unique suppliers** (not listing rows). Default scope is
     **Nearby (25 km)** ranked by real distance from the lead's city — so nearby
     municipalities (e.g. Vahi / Tõrvandi / Reola for a Tartu lead) appear, not just exact
     city-string matches. Switch to **All Estonia** (with optional **All services**) to
     search the whole active directory by name / location / city / address / email / phone.
   - Select the providers to contact → **Review message to N providers**: this shows every
     recipient and the **exact** localized subject + body the backend will send. Confirm to
     send. Already-contacted providers are **skipped by default**; use the explicit
     **resend** action on an outreach-history row to contact one again.
   - Sending outreach moves the lead → **Contacted** (stamps the response-time clock).
     Providers without email stay visible with call/copy actions.

3. **Stage 2 — Build customer options**: outreach history shows Sent / Replied / Declined /
   No answer with notes. Replied providers get **Add to offer**; you can also add any
   provider or a **free-form** option (quote came via another channel). There is **one
   active Draft** at a time — creating again returns the same draft, never a hidden
   duplicate. Edit option title / provider / price / unit / notes / order. **Delete draft**
   (confirmation required) is available only for a Draft; Sent/Viewed/Chosen/Expired offers
   are immutable history.

4. **Stage 3 — Review & send**: **Review delivery** opens an admin-only preview — the
   **exact** email (recipient, subject, body) and the **Customer page** (same component the
   customer sees). Previewing **never** marks the offer Viewed. The final confirm lists the
   exact effects (email sent, link goes live, lead → Quoted, opening records Viewed,
   requesting an option alerts Ruumly, **no payment/booking**). Send moves the lead →
   **Quoted, Offer Sent**.

5. **Outcome**: when the customer requests an option the workspace shows **Customer
   requested** + the chosen option (lead stays **Quoted**). After the provider confirms
   availability, click **Confirm with provider and mark booked** → **Converted**. Other
   ends: **Dismissed (=Lost)** or **Unmatched** (no provider available — *demand signal*,
   note what was missing).

6. Follow-ups: no customer reply in 48h → one nudge. No provider reply in 24h → next
   provider on the list.

## Weekly review (metrics row on the Leads tab)
| Metric | Meaning | Early target |
|---|---|---|
| Requests / week | Is the demand channel working? | grow week-over-week |
| Contact rate 30d | Are we touching every lead? | ≥ 95% |
| Quote → booking 30d | Is the matching any good? | ≥ 25% |
| Median first response | Speed = trust | < 4h (work hours) |

**Do NOT steer by:** partner signups, listing counts, feature count, raw traffic.

## Where demand comes from (the actual hard problem)
The funnel captures demand; it does not create it. Weekly demand work:
- SEO city/vertical pages (already live) + blog answers to "kolimine Tallinnas" queries.
- Small paid tests: Google Search ads on high-intent terms ("laopind Tallinn",
  "kolimisteenus Tallinn"), FB/IG partner + customer ads (see ruumly-ad/ assets).
- Local channels: Facebook groups (kolimine/kirbukas), housing communities, realtors.
- Every Unmatched lead = a supplier-recruitment call with proof in hand:
  *"Meil oli eile klient, keda me ei saanud teenindada — kas soovite selliseid?"*

## Supplier recruitment (only with demand as the argument)
Pitch order: (1) here is a real customer/lead volume, (2) enquiries are free right now,
(3) later: optional boosts/tools. Never lead with the platform.

## Scale-up triggers (don't automate before these)
- >10 requests/week sustained → templated supplier outreach (email templates in admin).
- >25 requests/week → automatic lead→supplier routing for repeat partners.
- Repeated adjacent asks in Details (packing, cleaning, boxes) → consider adding as
  request categories — the event ("I'm moving") defines the scope, not the taxonomy.

## Mechanics reference
- Public funnel: `/{lang}/request` → `POST /api/leads/request` (rate-limited 5/10min/IP).
- Statuses: New → Contacted → Quoted → Converted(=Booked) | Dismissed(=Lost) | Unmatched.
  First **genuine** contact stamps `ContactedAt` (drives the median-response metric);
  Dismissed/Unmatched do not stamp it. **Converted is set only by booking-confirmation**,
  not by the customer's selection and not from the clickable pipeline.
- Hero flip: PlatformSettings `conciergeFirst` ("true"/"false") — admin → Settings.
  Old marketplace hero returns instantly when "false". `conciergeCities` = operating-area
  hint shown in the funnel.
- **Auto-fanout (2026-08):** a new concierge request emails nearby providers for a price
  immediately — outreach no longer waits for an admin to open the workspace. Candidates
  come from the same provider-candidate finder (25 km, widened to 50 then 100 only if the
  quota is unfilled); the lead moves New → Contacted and the ops alert says exactly what
  was sent ("Auto-contacted: 4 provider(s) within 25 km (1 skipped: no email) — …").
  A request with category `any` never fans out (nothing specific to ask for) and neither
  does anything else when the switch is off — the alert says so, and it is then hand-work.
  Settings: `conciergeAutoOutreach` ("true"/"false", default true),
  `conciergeAutoOutreachMax` (default 6, clamped 1..12), `opsPhone` (support phone in the
  provider email; empty = the phone line is omitted).
- **Dead provider addresses (2026-08):** Resend posts every bounce and spam complaint to
  `POST /api/webhooks/resend`. The endpoint is public (Resend cannot hold our JWT) and
  authenticated by the **Svix signature alone** — a forged bounce would retire a live
  provider's address, so verification is never skipped and it is idempotent on the
  `svix-id` header (Resend retries).
  - A **hard** bounce or a **spam complaint** sets `Supplier.ContactEmailUnusable`. Auto
    fan-out then skips that provider and fills the slot with the next candidate, and the
    admin batch refuses it with reason `email_bounced` (not overridable by `resend=true`).
  - A **soft** bounce (full mailbox, greylisting) records the timestamp/reason but never
    retires the address.
  - Every still-open outreach row to a hard-bounced address flips `Sent → Bounced`
    (or `Complained`), so the workspace stops claiming we contacted them. The bounce
    reason is appended to the row's note. Every event is stored in `EmailDeliveryEvents`
    and audited as `email.bounced` / `email.complained` (actor `resend-webhook`).
  - **Ops fix:** the lead workspace badges the row *Email bounced* / *Phone only* and
    offers an inline email field — saving a DIFFERENT address clears the bounce verdict,
    so the provider is reachable again on the next fan-out. This is the path for the
    ~127 providers whose address exists but is published nowhere: call, type, save.
  - Setup: Resend dashboard → Webhooks → Add endpoint `https://api.ruumly.eu/api/webhooks/resend`,
    events `email.bounced` + `email.complained`, then copy the `whsec_…` signing secret
    into Railway as `RESEND__WEBHOOKSECRET`. Without it the endpoint fails closed (503)
    and logs an error — it never silently discards a bounce.
- Workspace endpoints (v2, backward-compatible — no migration):
  - Providers: `GET /api/admin/leads/{id}/provider-candidates?q=&scope=nearby|all&category=lead|any&radiusKm=25&limit=50`
    (unique suppliers, Haversine distance from the lead's city anchor, exact-city first;
    `category=any` requires `scope=all`). Legacy `GET …/matches` still works.
  - Outreach: `POST …/leads/{id}/outreach/preview {supplierIds[]}` (side-effect-free, exact
    subject/body); `POST …/leads/{id}/outreach {supplierIds[], resend}` (skips
    `already_contacted` unless `resend=true`); `PATCH …/outreach/{id} {status, note}`.
  - Offers: `POST …/leads/{id}/offers` (returns the existing newest Draft or creates one);
    `PATCH …/offers/{id}` (replace-set options); `DELETE …/offers/{id}` (Draft only → 204,
    else 409); `GET …/offers/{id}/delivery-preview` (exact email + customer page, never
    marks Viewed); `POST …/offers/{id}/send`; `POST …/offers/{id}/confirm-booking`
    (Chosen offer → Converted, idempotent, creates NO Booking/Order/payment/contract).
  - Public: `POST /api/offers/{token}/choose` sets Offer=Chosen + alerts ops but leaves the
    lead **Quoted** (a preference, not a booking).
- Metrics API: `GET /api/admin/leads/metrics` (concierge-scoped north-stars incl.
  `matchRate30d`); leads list filters: `source|category|city|needsResponse`.
- **Hard delete (test rows only):** `DELETE /api/admin/leads/{id}` (Admin role, 204/404)
  removes the lead plus its outreach rows, offers and offer options in one transaction,
  and audits `lead.deleted` with the city/category so the trail outlives the row. Use it
  ONLY for canary/smoke-test/spam rows: dismissed leads still count in `requestsThisWeek`,
  `requests30d` and every rate denominator, so fake rows quietly inflate the north-stars.
  A real request that went nowhere gets `status = Dismissed/Unmatched` instead — a lost
  lead is data, and deleting it would flatter the conversion rates rather than measure them.
