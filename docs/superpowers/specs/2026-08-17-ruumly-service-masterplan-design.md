# Ruumly service masterplan — design

**Date:** 2026-08-17
**Status:** design, awaiting review
**Scope:** all five consumer services (warehouse, moving, trailer, cleaning, vanrental), end to end

---

## 1. Why this exists

The concierge loop runs, and it does not convert. Last measured reading of
`GET /api/admin/leads/metrics`, taken **before** the Adduco offer landed on the Haapsalu
lead — so these are a floor, not a current statement, and Phase 0 re-reads them:

| metric | value |
|---|---|
| `matchRate30d` | 1 of 8 |
| `quoteRate30d` | 0 |
| `bookingRate30d` | 0 |

Requests arrive. Providers get emailed. Almost nothing comes back. Two real requests in
the last week each exposed a distinct structural gap rather than bad luck:

- **Haapsalu (`d0f5b140`).** Customer typed a street address into the city field. The
  anchor resolved to null, so widening the radius was futile and 34 movers in range were
  never contacted. Already fixed (`looksLikeStreetAddress`), but it revealed that a
  request can fail silently and completely.
- **Adduco's reply.** A mover refused to quote a live job: *"selle info pealt adekvaatset
  pakkumist paraku ei saa teha"* — they needed to know whether origin and destination
  share an address, and they needed photos. Both were things the intake structurally
  could not tell them. A full round trip was lost on a move the customer needed that week.

The organising principle of this plan follows directly from the second one.

## 2. Organising principle

> **Can a provider price this request from what we sent, without asking a question?**

Every finding below is ranked by that test. It is objective, it is measurable per service,
and it is the thing that stands between a request and a quote. Volume growth is deliberately
sequenced last: more requests against a loop that cannot produce quotes means more providers
burned, and provider goodwill does not regenerate.

## 3. Per-service audit

Each service is scored on what the intake collects versus what a provider in that trade
actually needs to produce a number.

### 3.1 Storage (`warehouse`)

- **Collects:** size (6 options), duration (5 options). Date not required.
- **Date correctly optional** — a unit is continuously available, unlike booked capacity.
- **Radius ladder:** 15 / 25 / 40 km. Correct; nobody drives 100 km to visit their own boxes.
- **Gaps:** never asks *what* is stored. A car, a boat, a motorbike or climate-sensitive
  goods change which unit type can be offered at all. No start date.
- **Verdict:** the only service that mostly passes the test. Price is €/m²/month, and two
  answers nearly suffice.

### 3.2 Moving

- **Collects:** home size (6), floor/lift (5), packing add-on (4). Date required.
- **Gaps, in order of cost:**
  1. `movingAccess` is a **single** answer. A move has **two** addresses. This is the exact
     question Adduco asked, and the schema cannot express the answer.
  2. No distance/route detail beyond city → city. Two Haapsalu addresses 400 m apart price
     nothing like Haapsalu → Tallinn.
  3. No heavy or awkward items (piano, safe, gym equipment, aquarium).
  4. No parking or carry distance.
- **Verdict:** highest-volume service, largest gap, and the gap is evidenced by a real
  provider's refusal rather than inferred.

### 3.3 Trailer

- **Collects:** duration (5), what is hauled (5). Date required.
- **Gaps:** nothing establishes the customer **can tow**. No tow bar, no towing vehicle, no
  licence category — category B covers trailers up to 750 kg, BE is required above it. We
  can currently route a request that no provider is able to lawfully fulfil.
- **Supply problem, separate from intake:** the rows behind the trailer hubs are largely
  petrol-station chains — Viada (35 branch rows), VIADA Baltija (27), Baltic Petroleum (25).
  A head-office inbox does not answer a concierge request about one trailer.

  **Corrected 2026-08-19.** This section originally read "124 Estonian trailer city hubs,
  the widest footprint of any service". That was wrong. `/et/` in the sitemap is the
  LANGUAGE prefix, not a country: `/et/trailer/*`, `/lv/trailer/*` and `/lt/trailer/*` are
  the same 124 slugs, and the cities include Ādaži, Aizkraukle, Alytus and Daugavpils.
  Estonian trailer coverage is **20** city hubs, not 124 — the widest footprint claim was an
  artefact of counting Baltic cities and calling them Estonian. See
  docs/ops/supplier-directory-audit-2026-08-19.md.
- **Verdict:** unfulfillable as currently asked, and matched against supply that will not
  reply. Both halves need a decision; see §7.3.

### 3.4 Van rental (`vanrental`)

- **Collects:** duration (5), van size (4). Date required.
- **Gaps:**
  1. Never asks **with or without a driver**. That answer *is* the boundary between van
     rental and moving. It is precisely why the Õismäe request could not be classified.
  2. No driver age or licence-held-since — rental companies have hard minimums.
  3. No pickup/return branch.
  4. No estimated kilometres. Van rental is priced per km; a day rate alone is not a quote.
- **Verdict:** missing the question that defines the service.

### 3.5 Cleaning

- **Collects:** type (5), size in m² (5). Date required.
- **Both are right** — type and area are the correct primary axes.
- **Gaps:** the add-ons that swing a Baltic cleaning price 30–50% — windows, oven, fridge —
  and who supplies materials. No access/keys arrangement.
- **Radius ladder:** 20 / 35 / 50 km. Correct; a crew works a metro area.
- **Verdict:** closest to quotable after storage; the misses are price-swinging rather than
  blocking.

## 4. Cross-cutting findings

| # | Finding | Where | Severity |
|---|---|---|---|
| 1 | Quote page renders **no photos**. The outreach email prints "Photos: N" and `PublicQuoteLeadDto` carries `photoCount`, but `QuotePage.tsx` never reads it. Provider is promised photos and shown nothing. | `QuotePage.tsx` | **P0** |
| 2 | Quote page defaults the price unit to **"month" for every service**. A mover entering a one-time price has `/month` preselected, and that unit propagates into the customer's offer. | `QuotePage.tsx:97` | **P0** |
| 3 | No "I cannot quote this" path. A blocked provider's only option is replying to a shared ops inbox. | `QuotePage.tsx` | P1 |
| 4 | Scope answers are rendered with `t()` in the **customer's** language and pasted verbatim into `Details`, which the provider email prints as-is. A Russian customer produces Russian scope lines inside an Estonian mover's email. | `RequestPage.tsx:410` | P1 |
| 5 | **No street address is ever collected**, for any service, at any stage. A mover cannot finalise without one; today the founder brokers it by hand. | intake | P1 |
| 6 | A concierge customer has **no status page**. `RequestDetailPage` redirects to `/account`, which a concierge customer does not have. Between the receipt email and the offer email there is silence. | `RequestDetailPage.tsx` | P1 |
| 7 | `aboutPage.mission` claims **"kontrollitud" / "verified" partners** — a claim nothing enforces — and names only storage, moving and trailer, two services out of date. | platform settings | P1 |
| 8 | `sitePhone` is empty everywhere. | platform settings | P2 |
| 9 | Partner-page contact dialog promises **the partner will reply** in all five languages (`partner.contactIntro`, `partner.contactToast`), but `POST /api/contact` emails only `siteEmail`. The partner is never contacted, and no lead row is created — so a storage enquiry on a partner page is not even counted as demand. Both messages the form has ever received (2026-08-16 Peetri Miniladu, 2026-08-17 GREENAS UAB) failed this way, and both partners are unclaimed `isDirectory` rows. | `PartnerPage.tsx:91`, `SupportController.Contact` | P1 |

## 5. Decisions taken

| Decision | Choice | Rationale |
|---|---|---|
| Scope data storage | **One nullable JSON column, `DemandLead.ScopeJson`** | Mirrors the existing `PhotoKeysJson` pattern. Phase 1 is largely about *adding* intake questions, so typed per-question columns would mean a migration per iteration. The JSON column is queryable enough for admin filtering and needs no schema churn. |
| Provider blocked | **"I need more info" button on the quote page** | Converts a dead-end reply into a tracked step with a structured reason. |
| Thin verticals | **Audit honestly, decide after the data** | No keep/pause decision on trailer or vanrental before the outcome data is in. |
| Automation ceiling | **Automate the chase, never the send** | The system may nudge a silent provider and ask a customer for missing information. Releasing an offer to a customer stays a human action. `offerAutoSend` remains off. |
| Goal sequencing | **Convert → automate → grow** | Volume against a non-converting loop burns providers. |

## 6. Architecture

### 6.1 `DemandLead.ScopeJson`

One nullable `text` column holding the raw chip answers as submitted:

```json
{ "movingSize": 2, "movingAccessOrigin": 3, "movingAccessDestination": 1, "packingHelp": 4 }
```

Read through a `LeadScope` helper following the exact shape of `LeadPhotos`: never throws,
validates on the way out, unknown question ids dropped. Labels are resolved **at compose
time in the recipient's language**, which is what fixes finding #4 — the provider email
renders `movingAccess` in Estonian for an Estonian mover regardless of the language the
customer used.

Question definitions live in one shared catalogue so intake, provider email, quote page and
admin all read the same source. Adding a question is a catalogue entry, not a migration.

### 6.2 Quote page

- Photo gallery reading `photoCount` and `GET /api/quote/{token}/photos/{index}`. Index-
  addressed, `private, no-store`, already built server-side.
- Unit default derived from the lead's category rather than hardcoded: warehouse → month,
  moving → onetime, cleaning → onetime, trailer → day, vanrental → day.
- Structured scope chips rendered from `ScopeJson` in the provider's language, replacing the
  wall of `details` prose.
- **"I need more info"**: structured reasons (photos / exact address / access / date / other)
  plus a free-text line. Writes a `ProviderInfoRequest`, flags the lead in admin, and fires
  a localized question to the customer.

### 6.3 Intake additions, per service

Additive only; every new question keeps the existing "not sure" escape so nobody is hard-blocked.

- **Moving:** access asked at **both ends**; heavy/awkward items; parking or carry distance.
- **Trailer:** tow bar and towing vehicle; licence category.
- **Van rental:** with or without a driver (asked first — it may reroute the request to
  moving); driver age; estimated kilometres.
- **Cleaning:** add-ons (windows / oven / fridge); who supplies materials.
- **Storage:** what is being stored; start date.
- **All:** street address, collected at step 3 alongside contact details, never shown to a
  provider before the customer accepts an offer.

### 6.4 Customer status page

`/{lang}/request/{token}` — tokenized, no account, `noindex`. Shows request state, what
happens next, how many providers were contacted, and any outstanding question from a
provider. Deliberately does **not** show provider identities or prices before the offer is
released; that stays the founder's decision to make.

### 6.5 LLM assistance (Phase 2)

A single `ILeadIntelligenceService`, behind a platform setting defaulting to **off**, routed
through `OutboundEndpointValidator` per project convention. Every output is a **suggestion
surfaced in admin**, never an action.

In scope:

1. **Provider reply → draft quote.** Highest leverage: most providers reply by email rather
   than opening the quote page, and `"teeme 350 eurot, saame kolmapäeval"` is a complete
   quote that currently dies unstructured. **Depends on inbound email processing, which does
   not exist** — Resend is outbound-only. That dependency is itself a Phase 2 work item.
2. **Ambiguous intake classification.** Proposes a service and flags ambiguity — the Õismäe
   case. Suggestion only; never triggers fan-out.
3. **Free-text → scope extraction.** `"3. korrus, liftita, klaver"` proposes
   `movingAccess` plus a heavy-item flag.
4. **Post-mortem drafting** for the ritual in §7.4.
5. **Supplier directory enrichment** (Phase 3, offline batch): dedupe the ~7 known redundant
   rows, and identify which branch row behind a chain like Viada is a human who answers mail.

Explicitly out of scope, permanently:

- Deciding which providers get cold-emailed. Irreversible, and provider goodwill is the
  scarcest asset in the business.
- Setting or adjusting any price.
- Sending anything to a customer or provider without human review.
- City → coordinates. A geocoder is deterministic, cheaper and correct; `PlacesService`
  already exists. This is named explicitly because the Haapsalu bug superficially looks like
  an LLM problem and is not.

## 7. Phases

**Decomposition.** Only Phase 0 and Phase 1 are specified here to implementation depth and
get the first implementation plan. Phases 2 and 3 are scoped deliberately — enough to prove
the sequencing holds and to stop Phase 1 from painting them into a corner — and each gets its
own spec and plan when reached. Phase 2 in particular depends on inbound email processing,
which is a substantial subsystem and deserves its own design rather than a bullet here.

### 7.0 Phase 0 — validate the ranking against reality

Pull the 8 real leads with an admin token: category, fan-out result, provider replies, where
each died. Confirm or reorder Phase 1 accordingly. This is a prerequisite, not a placeholder —
the ranking above is derived from code plus two observed requests, and must be checked
against outcomes before large work is committed.

### 7.1 Phase 1 — make every request quotable

1. Photos on the quote page (P0, half-shipped).
2. Per-category unit defaults (P0).
3. `ScopeJson` + shared question catalogue + provider-language rendering.
4. Per-service intake additions (§6.3).
5. "I need more info" path.
6. Street address collection.
7. Customer status page.
8. Remove the unenforced "verified" claim; correct the service list in `aboutPage.mission`.
9. Partner-page contact dialog. A message on a partner page is **demand**, so capture it as a `DemandLead` (`Source = "partner-page"`, `SupplierId` set, category/city derived from the supplier) rather than an untracked email — `POST /api/leads/quote` already does exactly this. Then branch delivery on `isDirectory`, which the public DTO already exposes: **claimed** partners get the supplier email and provider notification, making the existing copy true; **directory rows** are answered by ops, and the copy changes to make no promise on a stranger's behalf. Cold-forwarding arbitrary mail to 1,187 imported rows is not the fix.

### 7.2 Phase 2 — automate the chase, never the send

1. Inbound email processing, so provider replies are captured at all.
2. Silent-provider nudge after a configurable interval.
3. Automatic localized question to the customer when a provider reports missing information.
4. Lead reference on every inbound reply, resolved into the workspace.
5. `ILeadIntelligenceService` uses 1–4 from §6.5.

### 7.3 Phase 3 — grow volume, and settle the thin verticals

1. Decide trailer and vanrental on Phase 0 evidence: keep, narrow to cities with a provider
   that has actually replied, or pause. **No decision is pre-committed here.**
2. Supplier directory enrichment.
3. SEO and demand channels, judged on cost per qualified request — which `Attribution` now
   makes computable.

### 7.4 The per-request ritual — running from Phase 0

Every real request produces a short structured post-mortem in `docs/ops/requests/`:

```
Lead reference, service(s), city/route
Where it stalled
What the provider had to ask that we should have known
What would have prevented it
Backlog item created (or: none needed)
```

Requirements: written within 48 hours of the request closing or going silent; one file per
lead; the backlog item is a real task, not a note. This is what turns "each request should
improve something" into a mechanism rather than an intention.

## 8. Testing

- **`LeadScope`** gets the same treatment as `LeadPhotos`: malformed JSON never throws,
  unknown question ids are dropped, and the column can never break the outreach email or the
  quote page that carry the request itself.
- **Provider-language rendering**: a Russian-language lead must produce Estonian scope lines
  in an Estonian supplier's email. This is a regression test for finding #4.
- **Unit defaults**: one test per category asserting the preselected unit.
- **Photo gallery**: index-addressed fetch, out-of-range index 404s, closed lead shows no
  photos.
- **Intake**: every new question keeps a "not sure" escape and cannot hard-block submission.
- **Any production canary sets the honeypot.** On 2026-08-17 an end-to-end test omitted it
  and fanned out to six real Tallinn movers about a request that did not exist. The honeypot
  holds the fan-out while leaving the test fully observable in the ops alert. This is a
  standing rule, recorded here because it was learned expensively.

## 9. Open items

- **Admin token** for Phase 0. Blocks validation, not the Phase 1 work already evidenced by
  code inspection.
- **Six providers cold-emailed in error** on 2026-08-17 (Moving24 OÜ, KLIN, Kolimine ja Vedu,
  Kolimine.ee, Kolimisabi OÜ, Kolimisabi Tallinnas). Canary lead needs hard-deleting so it
  does not pollute `requestsThisWeek` or the rate denominators; whether to send those six a
  short apology is the founder's decision.
- **Estonian register**: provider mail uses formal *teie*, customer mail uses informal *sina*.
  A systematic conversion across `EmailTranslations.cs` and `translations.ts` was offered and
  is not yet approved.
