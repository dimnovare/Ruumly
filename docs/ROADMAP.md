# Ruumly — Strategic Roadmap

> **One-line thesis:** Ruumly is a **demand-first concierge for the "I'm moving" event**.
> We sell *customers*, not the platform. The public front door is "tell us what you need →
> we return 2–3 offers." Every task below earns its place only by growing qualified demand,
> raising the supplier match rate, or proving the loop converts — not by adding features.

Last updated: 2026-08-08. Owner: Dim. Direction: the 2026-07 Sergei Anikin pivot
(see `docs/superpowers/specs/2026-07-10-overhaul-design.md` and the `pivot-concierge-direction`
memory). Status: the concierge loop, the event framing, the free provider directory
(163 real Estonian providers, now expanding to LV/LT), the admin offer/outreach workspace,
and the SEO foundation are **shipped and live**. Focus now: **run the manual match loop and
prove the funnel converts.**

---

## Why the pivot (the honest market picture)

Storage/moving is a **low-frequency, high-CAC** category. Selling a marketplace *to partners*
fails: they join only for incremental profit ("bring me 5 paying clients and I'll join in 5
minutes"), so partner-acquisition-first is pushing on a string. The leverage is **demand**:
organize around the life event ("I'm moving" pulls in storage, movers, a van, a trailer,
cleaning — plus packing as part of the move), capture the request, and hand suppliers
*real customers who are searching right now*. Supply is then acquired by showing ROI, not by
selling software.

**Geography (updated 2026-08).** The **ops loop** stays **Tallinn/Harjumaa-first** (density
before breadth — the auto-fanout starts at a 25 km radius). The **directory is going Baltic**:
the admin import validates EE/LV/LT rows against per-country bounding boxes, and
`Supplier.Country` picks the outreach language so a Latvian provider is never cold-emailed in
Estonian. LV/LT = directory coverage and SEO surface, **not** a second concierge ops loop.

**The business problem = the demand→match→offer loop.** Make it work, measure it honestly,
then charge suppliers for delivered customers. That is the whole plan.

---

## The service set (decided 2026-08 — 5 sold, 2 retained-for-data)

**Consumer-selectable: `warehouse · moving · trailer · cleaning · vanrental`.**

`packing` and `insurance` were **withdrawn as consumer categories** on the evidence of market
research run independently across Estonia, Latvia and Lithuania:

- **packing** is *never* sold as a standalone business anywhere in the Baltics — it is always a
  line item inside a moving company's offer. Lithuania's national directory even has a dedicated
  "pakavimo paslaugos" category that lists the same two companies as its general moving category.
  Advertising it as bookable sends a customer into a dead end with no supplier to quote.
- **insurance** in this market means **CMR carrier-liability cover sold B2B to hauliers**, not
  goods-in-transit cover a household can buy. Wrong customer entirely, and it was already the
  thinnest vertical (one Estonian city).

What that means concretely: **packing is now an add-on attribute of a moving request** (intake
maps it to `moving` and records the intent as a `+packing-addon` marker in the lead's Query
machine summary, which the ops alert reports and `ProviderOutreachComposer` reads back to render a
*localized* packing line — never as English prose in `Details`, which is printed verbatim into a
cold email written in the provider's own language); **insurance falls back to `Any`** so an admin
routes it by hand rather than the request being dropped.

**Retained for data, NOT deleted.** The `Packing`/`Insurance` enum members and every stored
`ServiceTypesJson` value stay: production `DemandLead` rows already carry those categories and the
column persists enum NAMES, so deleting a member makes historical leads unreadable. Knowing which
movers also pack is *useful supplier metadata* — it is what powers the add-on. Single source of
truth: `Constants/ServiceCategories` (`BySlug` = storage catalogue, `ConsumerSlugs` = sales
catalogue, `PublicAliasFor` = how a retired slug resolves on a public surface).

---

## North-star metrics (review weekly) — these ARE the funding story

| Metric | What it proves | Source |
|--------|----------------|--------|
| **Qualified requests / week** | Demand exists and the funnel captures it | `GET /admin/leads/metrics` (concierge-scoped) |
| **Supplier match rate** | We can actually serve the demand (supply coverage) | matched ÷ worked; `Unmatched` = explicit miss |
| **Quote → booking conversion** | The offers convert to real deals | offer `chosen` ÷ leads `quoted` |
| **Median time-to-first-response** | The manual loop is fast enough to win | first admin touch − request time |

NOT tracked as success: partner signups, feature count, marketplace GMV. If qualified
requests are flat for 3–4 weeks, the problem is demand/GTM, not the product.

---

## Phase 0 — Run the loop + honest metrics (NOW) · the only thing that matters

The tooling shipped today. The job now is **operating it** and **trusting the numbers**.

- [ ] **Daily match loop** (founder ops, `docs/CONCIERGE-OPS.md`): every concierge request →
      review → outreach to matched providers → compile 2–3 options → send the offer → track
      viewed/chosen. Target the median-first-response metric.
- [x] Metrics scoped to the concierge funnel (Source="concierge"), match rate computed,
      contact/response no longer polluted by Dismissed/Unmatched.
- [x] **Guided lead-operations workspace** (2026-07-15 design, `docs/CONCIERGE-OPS.md`):
      geographic provider discovery (25 km nearby ranked by real distance + All-Estonia
      search), exact outreach review before send + explicit resend, one deletable offer
      draft, exact delivery preview that never fakes a Viewed receipt, and the corrected
      booking semantics — a customer selection is a **pending preference** (lead stays
      Quoted); only admin **Confirm with provider and mark booked** converts. No payment/
      Booking/Order/contract is created on selection.
- [ ] **First 10 real qualified requests worked end-to-end**, at least a few reaching an
      offer sent and one chosen — the proof the loop converts.
- [ ] **Seed demand**: point the directory-launch social posts + the SEO city hubs (now
      indexable, re-indexing requested) at Tallinn moving/storage intent; watch GSC.

Acceptance: a real customer request → outreach → offer → chosen, with the metrics reflecting
it truthfully.

---

## Phase 1 — Monetize the loop: charge for delivered customers (Week 2–8)

The pivot's revenue model is **"you pay only for real customers,"** and it must become
backable by facts from Phase 0.

- [ ] **Prove ROI to a handful of directory providers**: "we sent you N requests near you
      this week." The outreach emails (lead facts, zero customer PII — we broker the intro)
      are already the paywall mechanism.
- [ ] **Introduce a charge per delivered/chosen customer** (per-lead or success fee) once a
      few suppliers have felt the value. Keep listing free; this is the concierge revenue line.
- [ ] **Montonio go-live** for the *ops layer* (bookings that do happen still need payments):
      production keys on Railway; end-to-end booking → checkout → webhook → Order → Invoice →
      PayoutEntry; document a manual wire-transfer fallback. Payments are built — this is
      credentials + testing, not building. (Marketplace booking is the demoted ops layer, not
      the front door, but when a booking occurs the money must move.)
  - **PREREQUISITE (found 2026-07-16, moot until Montonio is live — do NOT ship online payment
    before fixing):** `BookingPage.tsx:126` resolves the partner via `useSuppliers`, which is
    gated `enabled: isAuthenticated && (role === admin|provider)` — so for a CUSTOMER the query
    never fires, `supplier` is undefined, and the page silently falls back to
    `paymentMethod:"bank_transfer"` ("arrange payment with the partner"). Harmless today
    (online payment is off), but a marketplace partner's customer will never see online
    payment. The fix is NOT just repointing the flags: `directPaymentEnabled` /
    `ruumlyPaymentEnabled` already exist on the public listing DTO, but **`billingModel`
    (`marketplace|rebate`) is Supplier-only and decides whether the customer should pay online
    at all** — switching the flags without it would show online payment to *rebate* partners'
    customers, i.e. taking money through the wrong commercial model. REQUIRED: expose
    `billingModel` on the public listing DTO (`GET /listings`, `/listings/{id}`), then
    BookingPage drops `useSuppliers` entirely.

Acceptance: at least one supplier paying for a Ruumly-delivered customer; ops-layer payments live.

---

## Phase 2 — Widen demand: SEO + content for the 7-service event (Week 4–16)

The SEO foundation was silently broken (client head never rendered in prod) and is now
fixed + guarded; the ranking surfaces are the storage city hubs. Amplify from there.

- [ ] **City-hub SEO** for the 5 consumer services (storage/moving/trailer live; cleaning/
      vanrental being added), leading with the concierge CTA. Tallinn is the biggest
      opportunity (116 impressions at pos 49 pre-fix; re-indexing requested).
- [ ] **Retire the packing/insurance hubs without losing the equity** (backend done 2026-08:
      the sitemap no longer emits `/packing/{city}` or `/insurance/{city}`, and the search API
      resolves the retired filters instead of dead-ending — `?type=packing` serves the moving
      pool, `?type=insurance` serves the generic search). **Still to do off-backend:**
      1. **Frontend router** (`estonia-space-hub`): 301 `/{lang}/packing/{city}` →
         `/{lang}/moving/{city}` and `/{lang}/insurance/{city}` → `/{lang}/search?city={city}`;
         drop both from the service pickers on the request form and search filters.
      2. **Edge (Vercel `vercel.json` rewrites or the Cloudflare Worker)**: a real 301 is worth
         more than a client-side redirect for the already-indexed URLs.
      3. Re-submit the sitemap in GSC afterwards so the removals are picked up.
- [ ] **Event-intent content**: "moving checklist / 30-day plan", "moving to Tallinn",
      neighbourhood + size guides — internal-linked home ↔ hubs ↔ categories ↔ request funnel.
      ET ("laopindade rent / kolimine") + EN ("storage near me / self storage {city}"), the
      queries GSC actually shows.
- [ ] **Every empty state → the funnel**: no search result, no provider in a city → /request.
- [ ] **ruumly-next (SSR) cutover** when traffic justifies it — it already server-renders the
      per-page head (the durable version of today's client-side fix). Resolve the booking-flow
      404 risk before flipping the domain (see `ruumly-next/MIGRATION.md`).

Acceptance: qualified-requests/week trending up, driven by organic event-intent traffic.

---

## Phase 3 — Deepen the loop (Month 2–5)

Only after Phase 0 shows the loop converts.

- [ ] **Supplier-side proof surface**: a lite "requests near you this week" view for claimed
      providers — the ROI story made self-serve.
- [ ] **Loop quality**: offer expiry + reminder, SMS notify on offer (Estonian numbers),
      post-move review request feeding provider trust scores.
- [ ] **Admin efficiency**: outreach reply auto-tracking, per-category templates, response-time
      SLA surfacing, weekly metrics digest.
- [ ] **Second city** (Tartu/Pärnu) once Tallinn/Harjumaa repeats.

Acceptance: the loop runs with less founder time per request; a second geography starts.

---

## What NOT to do right now (anti-roadmap)

- ❌ Don't rebuild the marketplace as the front door — it stays a demoted, functional ops layer.
- ❌ Don't chase partner self-serve signups as a success metric — supply follows demand ROI.
- ❌ Don't add a new vertical before Tallinn's loop converts — the count went **down** to 5 in
      2026-08 (packing folded into moving, insurance dropped), and that was the right direction.
- ❌ Don't confuse the Baltic **directory** expansion with opening a second **ops loop**. LV/LT
      get directory rows and SEO surface; the concierge match loop stays Tallinn/Harjumaa until
      it converts here.
- ❌ Don't ship SEO changes without the production head-in-DOM gate (react-helmet-async taught
      us: a green dev build is not proof the tags reach prod).

---

## Sequencing summary

1. **Now:** run the match loop + honest concierge metrics (Phase 0) — the only real work.
2. **Wk 2–8:** prove supplier ROI → charge per delivered customer; Montonio for the ops layer (Phase 1).
3. **Wk 4–16:** 5-service SEO + event content to widen demand (Phase 2).
4. **M2–5:** deepen the loop + a second city (Phase 3).

Phase 0 is the whole game right now. Everything else amplifies a loop that must first convert.
