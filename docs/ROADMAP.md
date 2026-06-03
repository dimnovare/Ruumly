# Ruumly — Strategic Roadmap

> **One-line thesis:** The product is built. The company is not. The next phase is
> revenue plumbing + supply + demand — not more features. Every engineering task
> below earns its place only by removing revenue friction or helping close a partner.

Last updated: 2026-05. Owner: Dim. Status of platform: production-grade after 35
audit passes — zero technical blockers. Focus area: commercial traction.

---

## The honest market picture (from ChatGPT deep research + reality)

- Estonia storage TAM ≈ €24–65M/yr. Baltics ≈ €84–216M/yr.
- At 12% commission, realistic **Year-3 revenue ≈ €0.5–1.5M/yr**; blue-sky ceiling ≈ €2–4M/yr.
- **€1M ARR is the inflection** from lifestyle business to venture-scale.
- Ranked failure modes: (1) no partners, (2) low booking conversion, (3) partners
  don't activate listings, (4) payments broken, (5) runway. Notice: **4 of the top 5
  are commercial, not technical.**

**Implication:** winning = supply (partners) × demand (bookings) × working payments.
The platform is ready. Pour energy into the three levers, not the codebase.

---

## Phase 0 — Turn on revenue (Week 1–2) · BLOCKER

Nothing else matters until money can move. The code is done; this is config + testing.

- [ ] **Montonio go-live**: add production AccessKey/SecretKey to backend env (Railway).
      `MontonioPaymentService.cs` and the webhook (`PaymentsController.Webhook`, JWT-verified)
      are already implemented — this is credentials + testing, not building.
- [ ] **End-to-end payment test**: real booking → Montonio checkout → webhook → Order →
      Invoice → PayoutEntry. Verify the webhook signature path and the failure path.
- [ ] **Payment fallback**: document a manual-invoice path (wire transfer) for partners
      who want it, so a Montonio hiccup never blocks a booking.
- [ ] **Refund/cancellation path**: confirm what happens to an Invoice/PayoutEntry when a
      booking is cancelled. Add the admin action if missing.

Acceptance: one real euro moves from a test customer to a test payout, end to end.

---

## Phase 1 — Supply engine: first 10 Tallinn storage partners (Week 1–8, parallel)

This is the bottleneck. Mostly founder outreach — but the platform can make "yes" easy.

**Founder track (not code):**
- [ ] Build a target list of 30–40 Tallinn storage operators (from the competitor map:
      independents, mini-storage, container yards — not the big chains first).
- [ ] Outreach goal: 2 signed partners/month. Lead with "free to list, you only pay when
      you get a booking" (the 12% free tier is your wedge — cheaper than subscriptions at
      their volume, per the unit-economics analysis).
- [ ] White-glove the first 10: you create their listings for them.

**Engineering support (only what removes onboarding friction):**
- [ ] **Bulk listing import** — admin tool to add a partner's units from a pasted list /
      CSV, so onboarding a 20-unit operator takes minutes not hours.
- [ ] **Onboarding checklist** in the provider dashboard (photos, pricing, availability,
      payout details) with a completion meter — drives listing activation (failure mode #3).
- [ ] **Google Places prefill** on partner signup (address, hours, photos) — less typing,
      higher completion.

Acceptance: 10 active Tallinn partners with live, photographed, priced listings.

---

## Phase 2 — Demand engine: conversion + content SEO (Week 4–16)

Reviews UI, trust signals, and Schema.org already exist. The real lever is **content**,
not a framework migration.

- [ ] **20 Tallinn storage content pages** (the highest-ROI SEO work): neighbourhood guides
      ("Storage in Lasnamäe / Mustamäe / Kesklinn"), size guides ("how much storage for a
      2-room flat"), use-case guides ("storing winter tyres / business stock / moving").
      Localised ET/EN/RU. Internal-link them to city/listing pages.
- [ ] **Conversion polish**: make reviews + verified-partner badges prominent on listing
      cards and detail pages (the components exist — surface them harder).
- [ ] **Empty-state demand capture**: the `DemandLead` capture is built — make sure every
      empty search and every "no storage in your city yet" funnels into it, and that admin
      acts on those leads (they're your supply-acquisition targets too).
- [ ] **Social previews**: Cloudflare Worker is live — confirm Telegram/WhatsApp/FB previews
      render for shared listing + city links.
- [ ] **DEFER full Next.js SSR** until 10+ partners and real organic traffic exist. The
      Worker already covers social/crawler previews; SSR is a 60–120h migration that only
      amplifies traffic you don't have yet. Revisit at Phase 4.

Acceptance: visitor→booking rate measurable and trending up; organic storage queries
landing on dedicated content pages.

---

## Phase 3 — Partner stickiness: the value bundle (Month 3–6)

Once partners exist, make them never want to leave. Ranked by impact/effort (ChatGPT's
recommendation, which matches the EU context):

- [ ] **e-Invoicing (high impact)** — auto-generate compliant invoices; EU B2B increasingly
      requires e-invoice formats (PEPPOL / national networks). Saves partners real admin time.
- [ ] **Google Business Profile automation (high impact, low effort)** — sync partner
      address/photos/hours to GBP. Boosts their local SEO → they attribute new renters to you.
- [ ] **e-Signatures** — `ContractSigningModal.tsx` exists; finish/polish it into a real
      online rental-agreement signing flow. Speeds onboarding and looks professional.
- [ ] **Partner analytics** — bookings over time, occupancy, revenue, payout history. Makes
      the subscription tiers worth paying for as volume grows.

Acceptance: a partner can run their whole storage admin (invoices, contracts, GBP, reports)
through Ruumly — switching cost becomes high.

---

## Phase 4 — Scale (Month 6–12)

Only after Estonia shows repeatable supply + demand + revenue.

- [ ] **Next.js SSR migration** — now traffic justifies the 60–120h investment (plan already
      written in `ruumly-nextjs-migration-plan.md`).
- [ ] **Baltics expansion** — replicate the Tallinn playbook in Riga, then Vilnius. Flip the
      Moving/Trailer toggles back on if/when those verticals are validated.
- [ ] **Dynamic pricing** — simple occupancy-based suggestions for partners.
- [ ] **Insurance white-label** — let customers add coverage at checkout (referral revenue).

Acceptance: €1M+ ARR trajectory; multi-country supply; venture-scale conversation possible.

---

## What NOT to do right now (anti-roadmap)

- ❌ Don't migrate to Next.js before you have partners + traffic.
- ❌ Don't build dynamic pricing / insurance / loans before 10 partners.
- ❌ Don't add more service verticals (moving, trailer) until storage works — you just
      focused to storage on purpose; respect that.
- ❌ Don't run another deep technical audit. 35 passes is enough; the code is not the
      problem. The next bug that matters is a commercial one.

---

## Metrics dashboard (review weekly)

| Metric | Now | Target (M6) | Target (M12) |
|--------|-----|-------------|--------------|
| Active partners (Tallinn) | ~1–3 | 10 | 25 |
| Live, photographed listings | ? | 50+ | 150+ |
| Bookings / month | ? | 30+ | 150+ |
| MRR (commission + subs) | ~€0 | €500+ | €2–4k+ |
| Visitor→booking rate | ? | 5% | 8% |
| Organic sessions / month | ? | grow | grow |

The three numbers that decide everything: **active partners, bookings/month, MRR.**
If they're flat for 3 months, the problem is go-to-market, not the product.

---

## Sequencing summary

1. **Now:** Montonio live (Phase 0) — unblocks all revenue.
2. **Now → M2:** Partner outreach + onboarding tooling (Phase 1) — the bottleneck.
3. **M1 → M4:** Content SEO + conversion (Phase 2) — demand.
4. **M3 → M6:** Partner value bundle (Phase 3) — retention.
5. **M6+:** Next.js + Baltics + pricing (Phase 4) — scale.

Run Phases 0–2 in parallel; they don't conflict. Phase 0 is the only hard blocker.
