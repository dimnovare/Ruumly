# Ruumly Improvement Plan

Companion to `RUUMLY_FULL_AUDIT.md`. Ordering deviates from the brief's suggested phases
because the repository findings justify it: the lead flow is **not** broken (739 backend
tests green, no data loss, no security hole in the critical path). What is broken is that
Ruumly **over-promises at the front door and measures nothing**. So Phase 1 is truth and
telemetry, not bug triage.

Guiding rule for every phase: **change what Ruumly says and what it records before changing
what it does.** Matching rules, radii and contact policy are business decisions — this plan
documents and proposes them, it does not retune them unilaterally (brief §40).

---

## Phase 1 — Truth, measurement, and input quality (P0)

The three things that must be true before a single euro of acquisition spend.

| # | Change | Files | Size |
|---|---|---|---|
| 1.1 | Replace the "2–3 offers within 24 hours" guarantee with an honest effort promise across all 8 keys × 5 languages | `translations.ts` | S |
| 1.2 | Instrument the concierge funnel: `request_start`, `request_service_selected`, `request_step_completed`, `request_submitted`, `request_failed` | `RequestPage.tsx`, `lib/analytics.ts` | S |
| 1.3 | Capture UTM + referrer on first touch, persist for the session, send with the lead, store it | `lib/attribution.ts` (new), `RequestPage.tsx`, `ConciergeRequest`, `DemandLead`, migration | M |
| 1.4 | Need date required for date-driven services, with an explicit "I'm flexible" option | `RequestPage.tsx`, `translations.ts` | M |
| 1.5 | Server-side date validation: reject past dates and absurd futures | `SupportController.cs` | XS |

**Definition of done:** GA shows a funnel; a lead row carries its own source; no screen
promises a deadline Ruumly does not enforce; no request reaches a provider dated last year.

---

## Phase 2 — Provider response rate (P1)

The loop's bottleneck is not requests, it is *answers*.

| # | Change | Files | Size |
|---|---|---|---|
| 2.1 | Lead reference in the outreach subject so a provider's reply is routable | `ProviderOutreachComposer.cs` | S |
| 2.2 | Send cold outreach from a human, monitored address | Railway `Email__FromAddress` (config) | XS |
| 2.3 | State plainly in the outreach that answering is free and needs no account | `EmailTranslations.cs` | XS |
| 2.4 | Scoping questions that make cleaning / trailer / van rental quotable at all | `RequestPage.tsx`, `translations.ts` | M |

**Definition of done:** a provider can answer the "is this real / what do they want / what
do I do / does it cost me" test in 15 seconds, and ops can file the reply against a lead
without guessing.

---

## Phase 3 — Reliability and abuse resistance (P1)

| # | Change | Files | Size |
|---|---|---|---|
| 3.1 | Duplicate-submit protection on intake (short-window dedupe, no migration) | `SupportController.cs` | S |
| 3.2 | Draft persistence + step in URL so refresh/Back cannot destroy the form | `RequestPage.tsx` | S |
| 3.3 | **DONE** — three automation signals gate auto fan-out: honeypot, time-on-form, and a per-address daily cap. A suspected bot's lead is still saved and queued with a note; only the automatic send is withheld | `RequestPage.tsx`, `SupportController.cs` | M |
| 3.4 | Redact `/quote/{token}` from analytics | `lib/analytics.ts` | XS |
| 3.5 | Validate the contact form's email | `SupportController.cs` | XS |

---

## Phase 4 — Operator comfort (P1/P2)

| # | Change | Files | Size |
|---|---|---|---|
| 4.1 | Show "no date — ask the customer" instead of rendering nothing | `LeadWorkspace.tsx` | XS |
| 4.2 | Show the real service list for multi-service leads in the workspace header | `LeadWorkspace.tsx` | S |
| 4.3 | Customer ack names the actual services requested | `CustomerRequestAckComposer.cs`, `SupportController.cs` | S |

---

## Phase 5 — Matching quality (P1) — **DONE, founder-approved 2026-08-14**

Both items change who gets contacted, so they waited for an explicit decision rather than
being retuned unilaterally. Both were approved and are now implemented.

| # | Change | Decision |
|---|---|---|
| 5.1 | Moving leads fan out at the origin **and** the destination, merged round-robin so neither end eats the shared quota | Approved: "yes, both endpoints" |
| 5.2 | Per-service radius ladders — storage 15/25/40 km, moving+van+trailer 25/50/100 km, cleaning 20/35/50 km | Approved: "tight storage, wide travel" |

Only moving gains the second anchor: storage, cleaning, van and trailer are all consumed at
the origin, so searching the destination for those would cold-email businesses that cannot
serve the request. Ladders are per service *within one lead*, so a "storage + moving"
request searches 15 km for the storage and 25 km for the movers at the same widening step.

---

## Phase 6 — Acquisition readiness — **DONE except one line**

- ✅ Pre-rendered `<head>` per route — 240 heads, deployed and verified in prod.
- ✅ Customer ack as branded HTML, with escaping that preserves õ/ä/ü/ų and Cyrillic.
- ✅ `radiogroup` semantics + roving tabindex + arrow keys on the single-choice chips.
- ✅ Touch targets: the funnel's entry points raised to 44px.
- ⬜ `/request` sitemap priority (0.9 → above the service×city hubs).

---

## What is left, in priority order (2026-08-14)

1. **`Email__FromAddress=info@ruumly.eu` on Railway.** Not code — `EmailFrom.cs` already
   centralises it. Cold outreach still says `From: noreply@`, which is the first thing a
   provider's spam test sees, on the one email whose entire purpose is a reply.
2. **VAT/KMKR number for Diip Solutions OÜ.** Invoice templates print a VAT line and the
   number on record belongs to the previous entity.
3. **`bankTransfer.*` PlatformSettings** — IBAN `EE517700771013151864` must be set in
   admin → Settings; it is data, not config, so no deploy carries it.
4. Footer navigation touch targets (32px). Deliberately deferred: raising them roughly
   doubles the mobile footer for the page's lowest-intent links.
5. `/request` sitemap priority.

---

## Explicitly not doing

- No new architecture, dependencies, queues, or services.
- No index tuning — volume does not justify it.
- No deletion of the marketplace/supply-side layer; it is flag-gated, inert, and load-bearing
  as the ops layer.
- No CAPTCHA as a first move — honeypot and timing first; a CAPTCHA taxes real customers.
