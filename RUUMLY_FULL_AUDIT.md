# Ruumly Full Product & Technical Audit

**Date:** 2026-08-14
**Scope:** `Ruumly.Backend/` (ASP.NET Core 8 + EF Core + PostgreSQL), `estonia-space-hub/`
(React 18 + Vite + TS), `workers/social-preview/`, `docs/`.
**Method:** source read of the concierge loop end-to-end, plus gates actually run:
`dotnet test Ruumly.Backend.Tests` → **739 passed / 0 failed / 2 skipped**;
`npx tsc --noEmit` → **0 errors**; `npm run lint` → **0 errors, 781 warnings**.

---

## Executive Summary

### What Ruumly currently feels like

Better than expected. This is not an AI-generated feature pile — the concierge loop is a
real, deliberate, heavily reasoned system. Several of the hardest parts are already right:

- Auto fan-out on intake (a request emails nearby providers immediately, not when an admin
  wakes up), with per-recipient quote tokens so a provider can price without an account.
- Inbox-level dedupe (not just supplier-row dedupe) so one company behind five branch rows
  is emailed once, and one company can't answer one customer with two competing prices.
- Opt-out enforced at the **candidate finder**, not at the send — so an opted-out business
  is never even suggested to an admin.
- Resend bounce/complaint webhook retiring dead provider addresses, Svix-verified, fails
  closed.
- Provider outreach that contains **zero customer PII** and **zero internal admin links** —
  I searched for that leak specifically; the only `admin?tab=leads` link in any composed
  mail goes to the ops inbox (`SupportController.cs:317`). That risk is genuinely closed.
- The customer acknowledgement composer that **deliberately refuses** to repeat the
  24-hour promise, with the reasoning written down (`CustomerRequestAckComposer.cs:23-29`).

The problem is not the engine. The problem is that the **product's promises, its
measurement, and its input quality have not caught up with the engine.**

### Maturity

| Dimension | Grade | One-line verdict |
|---|---|---|
| Product | **B−** | Strategy is right and mostly executed; the front door still over-promises. |
| Technical | **A−** | Mature, well-tested, well-reasoned. Real gaps are narrow and specific. |
| UX | **B** | Funnel is short and clean; loses data on refresh, and asks the wrong things for 3 of 5 services. |
| Operational | **B** | Workspace is genuinely good. It hides exactly the fact the operator most needs (missing date). |
| **Launch readiness** | **Conditional** | Safe to run today. **Not safe to buy traffic for** — nothing measures the funnel, and the landing promise isn't one Ruumly can keep. |

### The single most important sentence in this audit

> **You are about to spend money on acquisition into a funnel that emits no analytics
> events, captures no UTM parameters, and promises a delivery time nobody enforces.**

Everything else here is smaller than that.

---

## Top 10 Problems

| # | Problem | Priority |
|---|---|---|
| 1 | The "2–3 offers, usually within 24 hours" promise is made on the highest-intent screens in 5 languages, is not enforced anywhere, and is contradicted by Ruumly's own ack email | P0 |
| 2 | The concierge funnel fires **zero** analytics events and captures **zero** UTM data — the north-star metrics' acquisition half is unmeasurable | P0 |
| 3 | The need date is labelled "(optional)", is not validated by the API, and a past date is accepted — this already produced unquotable requests | P0 |
| 4 | Intake auto-emails up to 6 real providers with **no bot protection** — form spam is now outbound-email amplification against the supply base | P1 |
| 5 | A provider's reply cannot be tied back to its lead (shared `info@`, no lead reference in the subject) | P1 |
| 6 | Cold provider outreach is sent `From: noreply@ruumly.eu` — fails the "is this spam?" test at the envelope | P1 |
| 7 | Multi-service requests degrade: provider mail names the real services, the customer receipt and admin header say the generic "Service" | P1 |
| 8 | Moving matching ignores the destination city — a Tallinn→Tartu move never reaches Tartu movers | P1 |
| 9 | One 25/50/100 km radius ladder for all five services, though storage and cleaning have opposite geographic logic | P1 |
| 10 | No submit idempotency, and all form state is lost on refresh/back | P1 |

---

## Biggest Problem, By Discipline

**Biggest product problem** — The funnel promises a *delivery time* and a *quantity*
("2–3 offers within 24 hours"). Ruumly can promise neither. It can promise *effort* and
*breadth* ("one request instead of ten searches; we contact the local providers who fit").
The honest promise is also the more differentiated one — nobody buys a marketplace for its
SLA, they buy it for not making twelve phone calls.

**Biggest technical problem** — Nothing is broken; something is missing. The funnel has no
telemetry. `src/lib/analytics.ts` exists and works, and every one of its call sites belongs
to the *deprecated* marketplace flow (booking, listing views, provider signup). `RequestPage.tsx`
calls it zero times.

**Biggest UX problem** — All funnel state lives in `useState` and the step is not in the
URL. Refresh, Back, or an accidental swipe destroys a completed 3-step form. On mobile —
where most of this traffic will be — Back is a reflex.

**Biggest operational problem** — The lead workspace renders the need date only when it
exists (`LeadWorkspace.tsx:155`). A request with no date shows *nothing at all* in that
slot, so the single most common quality defect is invisible in the exact place the operator
decides what to do next.

**Biggest conversion problem** — Step 2 asks the wrong questions for three of five
services. Cleaning asks only *what kind*, never *how big* — no cleaner can quote that.
Trailer and van rental ask only *how long*, never *what kind*. So Ruumly reliably harvests
requests it then cannot get priced, and the concierge spends its scarcest resource (a
provider's willingness to answer a cold email) on an incomplete ask.

**Biggest security risk** — `POST /api/leads/request` is anonymous, has no CAPTCHA,
honeypot, or proof-of-work, and now triggers up to six outbound emails to real third-party
businesses. Rate limiting is 5 requests / 10 min / IP. The asset at risk is not a server —
it is Ruumly's sending reputation and the goodwill of a supply base that took a 754-address
campaign to build.

---

## Customer Journey Issues

| P | Area | File(s) | Current behaviour | Why it's a problem | Fix | Size |
|---|---|---|---|---|---|---|
| P0 | Copy / trust | `src/i18n/translations.ts` (8 keys × 5 langs: `request.success.body`, `request.hero.subtitle`, `home.concierge.step2.desc`, `home.how.step2.desc`, `home.faq.work.a`, `hiw.step3desc`, `faq.offerSpeedA`, `faq.howFastA`) | Promises "2–3 offers, usually within 24 hours" | Not enforced by any job, alert, or SLA. `matchRate30d` exists precisely because some requests get zero offers. Ruumly's own ack email refuses to repeat this promise and documents why. A first-touch promise that breaks is worse than no promise. | Replace the *deadline+quantity* promise with an *effort+next-step* promise, everywhere. Keep "usually within 24 hours" only where it is hedged AND recoverable. | S |
| P0 | Data quality | `RequestPage.tsx:462-475`, `translations.ts request.date.label` | Date literally labelled "(optional)"; no validation | Providers cannot quote a move/van/trailer without a date. Founder already hit this in production. | Make date required for `moving`/`trailer`/`vanrental`/`cleaning` with an explicit "I'm flexible" escape chip; keep it optional for `warehouse`. Validate server-side. | M |
| P0 | Data quality | `SupportController.cs:229-254` | API accepts any `NeedDate`, including years in the past | A typo'd 2025 date reaches providers as an urgent flag (`ProviderOutreachComposer.cs:79-81` treats any past date as urgent). | Reject dates before today and beyond ~2 years. | XS |
| P1 | Reliability | `RequestPage.tsx:211-222` | `mutation.mutate()` with no idempotency key | Double-tap / retry on flaky mobile creates two leads → two fan-outs → the same providers cold-emailed twice about one customer. | Client-generated `requestId` GUID, unique index, second submit returns the first result. | S |
| P1 | Data loss | `RequestPage.tsx:100-116` | All state in `useState`; step not in URL | Refresh/Back destroys a completed form. | Persist draft to `sessionStorage`, keep `?step=` in the URL. | S |
| P1 | Validation | `RequestPage.tsx:530-544`, `SupportController.cs` | Phone accepted unvalidated | For a concierge, phone is the fastest channel; a typo silently costs the lead. | Light format check + `tel` normalisation. Do **not** make it required. | XS |
| P2 | Copy | `RequestPage.tsx:233-252` | Success screen's only outbound link is "browse the directory" | The customer has just told us what they need; sending them to self-serve search undercuts the whole proposition. | Say what happens next and when they'll hear from us; keep browse as a quiet secondary. | XS |
| P2 | Email | `SupportController.cs:374-382` | Customer ack is `textBody` only (`htmlBody: null`) while providers get branded HTML | The customer's *only* proof of receipt looks less legitimate than the cold email we send strangers. | Give the ack the same minimal branded HTML shell. | S |

---

## Service-Specific Request Logic

Current step-2 questions (`RequestPage.tsx:67-73`):

| Service | Asked today | Missing for a quotable request |
|---|---|---|
| `warehouse` | size, duration | *(adequate)* — access needs would help, not required |
| `moving` | size (+ optional packing add-on, + `toCity`) | **floors / lift** — the single biggest price driver in a Baltic move |
| `trailer` | duration | **trailer type / what's being hauled** |
| `vanrental` | duration | **van size**, driver-or-self |
| `cleaning` | type | **size (m² or rooms)** — no cleaner can quote without it |

**P1** — cleaning, trailer and van rental produce structurally unquotable requests. This is
the highest-leverage conversion fix in the whole audit: it costs one entry per service in an
existing `SCOPE_QUESTIONS` map plus translation keys, and it directly raises provider
response rate.

**Verified correct, no action:** `packing` and `insurance` are already retired from sale per
the 2026-08 founder decision and handled properly — `packing` routes to `moving` and
survives as a localized fact line in provider mail; `insurance` routes to `Any` for manual
handling; the enum members are retained so historical rows stay readable
(`Constants/ServiceCategories.cs`). **Your instinct in the brief matches what the code
already does.** Note the brief lists 7 consumer categories; the shipped product sells 5.
This audit follows the code and the founder decision.

---

## Provider Journey Issues

| P | Area | File(s) | Current behaviour | Why it's a problem | Fix | Size |
|---|---|---|---|---|---|---|
| P1 | Deliverability / trust | `Helpers/EmailFrom.cs` | Cold outreach sends `From: Ruumly <noreply@ruumly.eu>` | A small Estonian firm's 5-second spam test starts at the sender. `noreply@` says "machine, don't answer" on the one mail whose entire purpose is a reply. | Set `Email__FromAddress` to a human, monitored address (`info@ruumly.eu`). Config-only change; `EmailFrom` already centralises it. | XS |
| P1 | Ops loop | `Helpers/ProviderOutreachComposer.cs:83`, `ConciergeOutreachService.cs:33` | `Reply-To: info@ruumly.eu`; subject is `{service}: {city → city}` with no lead reference | Two live Tallinn→Tartu moving leads produce two identical subjects. A provider's reply is then un-routable without human guesswork — and replying is the *primary* action we ask for. | Append a short lead reference to the subject (e.g. `[#7F3A]`, first 4 of the lead GUID). Zero migration, zero new infrastructure. Plus-addressing is the follow-up, not the first step. | S |
| P2 | Trust | `ProviderOutreachComposer.cs` | Body never states that answering is free and requires no account | The brief's "within 15 seconds: do I know whether it costs money?" test currently fails. | One sentence in `OutreachIntro`/`OutreachAsk`. | XS |

**Verified correct, no action:** no customer name/email/phone in provider mail; no internal
admin links; HTML is inline-styled with a plain-text fallback; every interpolated value is
escaped with an escaper chosen specifically so `õ/ä/ü/ų`/Cyrillic survive
(`ProviderOutreachComposer.cs:205-209`); urgency is surfaced in the subject *and* the body.

---

## Admin CRM Issues

| P | Area | File(s) | Current behaviour | Why it's a problem | Fix | Size |
|---|---|---|---|---|---|---|
| P1 | Ops blindness | `LeadWorkspace.tsx:155-159` | The date row renders only `{lead.needDate && …}` | A dateless request looks identical to a dated one. The operator cannot see the defect they most need to chase. | Render an explicit "No date — ask the customer" chip when `needDate` is null. | XS |
| P1 | Ops blindness | `LeadWorkspace.tsx:151` | Header shows `serviceTypeLabel(t, lead.category)` | For a multi-service request `category` is `any`, so the header says "Service". The real ask survives in `Query` and the operator has to decode it. | Reuse the same `SelectedSlugs` recovery the provider composer already does. | S |
| P2 | Ops speed | `AdminLeads.tsx` | Filters exist (`source`, `category`, `city`, `needsResponse`) | No "missing critical info" filter, which is the queue an operator should work first. | Derive it client-side from the fields already returned. | S |

**Verified correct, no action:** the three-stage guided workspace, side-effect-free outreach
and delivery previews, single-active-draft invariant, immutable sent offers, `Converted`
reachable only via booking confirmation, and the derived activity timeline. This is a
genuinely good ops tool — the operator test in the brief passes except for the two blind
spots above.

---

## Matching Issues

| P | Area | File(s) | Current behaviour | Why it's a problem | Fix | Size |
|---|---|---|---|---|---|---|
| P1 | Correctness | `ConciergeOutreachService.cs:363-364` | `AsCategory()` copies only `Id`, `City`, `Category` — `ToCity` is dropped | A Tallinn→Tartu move is matched only against Tallinn. Movers at the destination — often the cheaper half of the market — are never asked. | For `moving`, run the candidate search at both endpoints and merge. | M |
| P1 | Business logic | `ConciergeOutreachService.cs:40` | `AutoRadiiKm = [25, 50, 100]` for every service | Storage is a **fixed place the customer drives to** — 100 km is a wrong answer, not a wider one. Movers, cleaners and van rental **travel to the customer** — 25 km is needlessly narrow. One ladder cannot mean both. | Per-category ladders. Document the intent; don't silently retune. | M |
| P2 | Coverage | `ProviderCandidateFinder.cs:217-224` | `MatchesListingCategory` handles only Warehouse/Moving/Trailer | Cleaning and van rental can match **only** via `ServiceTypesJson`. Correct today (imports set it), fragile as a silent assumption. | Add a regression test asserting a cleaning/van supplier with no listings is still found. | S |

**Verified correct, no action:** opt-out filtered at source; `CityMatcher` handles
"Таллинн"/"Harjumaa"/missing macrons (a 2026-08-13 production bug, fixed and commented);
`alreadyContacted` null-vs-`DateTime.MinValue` handled explicitly; `Any` never blast-fans-out;
round-robin merge so a "moving + storage" request doesn't spend all six slots on movers.

---

## Email Issues

| P | Issue | File(s) | Size |
|---|---|---|---|
| P1 | Provider replies un-routable to a lead (see Provider Journey) | `ProviderOutreachComposer.cs` | S |
| P1 | Cold mail sent from `noreply@` | `Helpers/EmailFrom.cs` (config) | XS |
| P1 | Customer ack says the generic category for multi-service asks — the "read the request back to them" purpose fails for exactly the case the intake encourages | `CustomerRequestAckComposer.cs`, `SupportController.cs:371` | S |
| P2 | Customer ack is plain text; provider cold mail is branded HTML | `SupportController.cs:374-382` | S |

**Verified correct, no action:** every transactional mail routes through `EmailTranslations`
(et/en/ru/lv/lt); provider mail language follows `Supplier.Country`, not the customer's;
email enqueue is post-commit so a failed transaction cannot double-send; every mail failure
in the intake is caught, logged with the lead id, and never 500s the customer; bounce
handling is idempotent on `svix-id`. **Historic lesson already encoded:** the daily-cap trap
that stranded 554 of 754 intro-campaign sends.

---

## SEO Issues

| P | Issue | Detail | Size |
|---|---|---|---|
| ~~P1~~ | ~~Raw HTML ships the default `<head>`~~ | **FIXED 2026-08-14.** `scripts/prerender-seo.mjs` runs after `vite build` and writes 220 per-route heads (44 routes × 5 languages) — 8 static pages, 5 verticals × 6 curated cities, 6 city hubs. Titles come from the app's own `seoMeta`/`translations` modules loaded through Vite's SSR pipeline, so the crawler's head cannot drift from the visitor's. Each route is written both as `<path>/index.html` and `<path>.html` because static hosts disagree about clean-URL resolution. | L |
| P2 | `/request` is `priority 0.9` in the sitemap | The commercial front door ranks below service×city hubs in the site's own hints, in a demand-first product. | XS |

**Verified correct, no action:** sitemap and `robots.txt` are live and served from the
backend; `/admin` disallowed; service×city hubs generated from real supply; retired-slug
hubs still resolve rather than 404.

---

## Mobile / Accessibility / Performance Issues

| P | Issue | File(s) | Size |
|---|---|---|---|
| P1 | Back/refresh destroys the funnel (mobile Back is a reflex) | `RequestPage.tsx` | S |
| P2 | Step-1 cards are `<button aria-pressed>` inside a `<fieldset>` — announced as toggle buttons, not a multi-select group | `RequestPage.tsx:292-335` | S |
| P2 | Scope chips are `<button aria-pressed>` in a `fieldset/legend` with no `role="radiogroup"`; arrow-key navigation absent | `RequestPage.tsx:385-414` | S |
| P2 | `stepError` sets `aria-invalid` on the city input even when the failure was a scope question | `RequestPage.tsx:355` | XS |
| P3 | Lint: 781 warnings (mostly `no-explicit-any`) | repo-wide | M |

Touch targets (44–48 px), focus rings, `role="alert"` on errors, and the 2-column mobile
grid are already handled correctly.

---

## Security Issues

| P | Issue | File(s) | Why it matters | Fix | Size |
|---|---|---|---|---|---|
| P1 | No bot protection on `POST /api/leads/request`, which now sends up to 6 third-party emails per call | `SupportController.cs:151`, `Program.cs:262-268` | Turns form spam into outbound-email amplification aimed at the supply base and at Ruumly's sending reputation. | Honeypot + minimum time-on-form + a per-email-address daily cap, before reaching for a CAPTCHA. Consider gating auto-fanout on a "looks human" signal. | M |
| P2 | `redactAnalyticsPath` redacts `/offer/{token}` but not `/quote/{token}` | `src/lib/analytics.ts:39-41` | The quote token is a bearer credential that lets its holder submit a price as that provider. Same harvest-from-GA risk the offer redaction exists to prevent. | Extend the regex. | XS |
| P2 | Anonymous `POST /api/contact` does not validate the submitted email | `SupportController.cs:36-55` | Not injectable (text body only), but it fills the ops inbox with unreplyable mail. | Reuse `EmailValidation.IsValid`. | XS |

**Verified correct, no action:** all 25 anonymous endpoints are intentional and individually
rate-limited; every admin controller inherits `[Authorize(Roles = "Admin")]` from
`AdminBaseController`; tokens are 256-bit and unknown tokens 404 identically to missing ones;
the quote page exposes no customer PII; outbound HTTP goes through the SSRF guard; CORS
exposes only `Retry-After`; access tokens are in memory and refresh tokens are HttpOnly.

---

## Data Model Issues

| P | Issue | Detail | Size |
|---|---|---|---|
| P1 | No attribution columns on `DemandLead` | No UTM source/medium/campaign, no referrer, no landing page. "Cost per qualified request" is not computable, so paid tests cannot be judged. | S |
| P1 | No submit-idempotency key | Nothing prevents a duplicate lead from a retried POST. | S |
| P2 | The real service selection is recoverable only by parsing `Query` | `ServiceCategories.SelectedSlugs()` is careful and well-defended, but three call sites now re-derive it and a fourth (customer ack) forgets to. | S |
| P2 | `NeedDate` nullable with no "flexible" distinction | Null conflates "hasn't decided" with "didn't ask". | S |

**Explicitly NOT a problem — do not act:** indexes. `DemandLead` is indexed on `Email`,
`CreatedAt` and `SupplierId`; `ProviderOutreach.QuoteToken` is unique-indexed. At current
volume any further index work is pure over-engineering.

---

## Bugs (real, reproducible)

1. **P1** — Past `NeedDate` accepted, then flagged **urgent** to providers.
   `ProviderOutreachComposer.cs:79` treats `needDate <= today+3` as urgent, so a typo'd
   2025 date sends a red-flagged cold email about a job that already happened.
2. **P1** — Multi-service ack regression. `SupportController.cs:371` passes
   `CategoryLabel(lead.Category)`; for a multi-service request that is `Any` → the generic
   "Service" label. The ack's stated purpose (`CustomerRequestAckComposer.cs:45-47`) is to
   read the request back so the customer spots their own typo. It fails for the case the
   intake copy actively encourages.
3. **P1** — Destination city dropped from matching (`ConciergeOutreachService.cs:363`).
4. **P2** — `/quote/{token}` leaks into GA (`analytics.ts:39`).
5. **P2** — `aria-invalid` set on the city field for a scope-question failure
   (`RequestPage.tsx:355`).

---

## Missing Tests

Backend coverage is strong (739 tests). The gaps that map to business risk:

- Intake rejects a past `NeedDate`; intake requires a date for date-driven services.
- Duplicate submit with the same idempotency key creates one lead and one fan-out.
- A cleaning/van-rental supplier with **no listings** but a correct `ServiceTypesJson` is
  still found by the candidate finder.
- Customer ack names the actual services for a multi-service lead.
- Moving fan-out reaches destination-city providers.
- Outreach subject carries a lead reference.
- Frontend: `RequestPage` step validation, draft restore, single-submit.

---

## Keep / Improve / Hide / Remove

**KEEP** — concierge intake + auto fan-out; provider quote-by-token; admin lead workspace;
provider candidate finder; Resend bounce webhook; opt-out enforcement; `EmailTranslations`;
the directory (it is the supply database the loop reads from, and the SEO surface).

**IMPROVE** — everything in the tables above.

**HIDE FOR NOW** — nothing new. The `conciergeFirst` flag already demotes the marketplace
hero, and the marketplace remains the ops layer. Correct as-is.

**REMOVE** — nothing. There is real supply-side machinery here (boosts, payouts, rebates,
contracts, disputes, Montonio) that predates the pivot, and it is *flag-gated and inert*
rather than in the customer's way. Deleting it is a week of risk for zero customer-facing
gain. Revisit only if it starts costing maintenance attention.

The honest framing: Ruumly's problem is **not** an over-built supply-side SaaS in the way
the brief feared. It is a well-built demand loop with an over-promising front door and no
instrumentation.

---

## Things That Should Be Built Next

1. Funnel analytics + UTM capture (P0).
2. Honest promise copy (P0).
3. Required, validated dates (P0).
4. Per-service scoping questions for cleaning / trailer / van (P1).
5. Lead reference in the outreach subject (P1).
6. Bot protection before buying traffic (P1).
7. Destination-city matching for moves (P1).
8. Per-service radius ladders (P1).
9. Pre-rendered `<head>` per route (P1, large — separate project).

---

## Issue Count

| Priority | Count |
|---|---|
| P0 | 4 |
| P1 | 17 |
| P2 | 13 |
| P3 | 1 |

## Product Questions Requiring Founder Input

0. ~~**The "2–3 offers" quantity claim.**~~ **RESOLVED 2026-08-14 — founder chose "hedge
   it".** All 193 instances across 39 keys × 5 languages now read "up to 3 offers"
   (`kuni 3 pakkumist` / `до 3 предложений` / `līdz 3 piedāvājumiem` / `iki 3 pasiūlymų`).
   The hedging preposition governs a different noun case than the bare numeral in RU/LV/LT,
   so each inflected form was mapped individually rather than prefixed. Unrelated copy
   sharing the digits — "2–3 days", "2–3 sentences", the moving flow's "2nd–3rd floor"
   option — was verified untouched by an automated guard. **SEO note:** this changes indexed
   meta descriptions; expect a re-crawl before Google shows the new snippets.
1. **The promise.** Drop "within 24 hours" entirely, or keep it hedged where a miss is
   recoverable? This audit assumes: drop the *guarantee*, keep an honest expectation.
2. ~~**Radius per service.**~~ **RESOLVED 2026-08-14 — "tight storage, wide travel".**
   Storage 15/25/40 km, moving+van+trailer 25/50/100 km, cleaning 20/35/50 km. Moving leads
   also now fan out at the destination city, not just the origin.
3. **Date-required per service.** Storage genuinely can be "flexible". Should trailer/van be
   hard-required, or is an explicit "I'm flexible" chip enough?
4. **Geography.** The brief says "Ruumly operates across Estonia"; the code and `CLAUDE.md`
   say the ops loop is Tallinn/Harjumaa-first with a Baltic *directory*. The copy currently
   follows the code. Confirm which is now true.
5. ~~**Operating company.**~~ **RESOLVED 2026-08-14 — Diip Solutions OÜ, reg 17527757,
   Uus-Sadama tn 15-2, 10120 Tallinn.** Matches the live footer, terms and privacy copy.
   **Still open:** the VAT number, IBAN and phone on record all belong to the previous
   entity (Valguse Kodu OÜ) and have not been re-verified — confirm each before it reaches
   an invoice, a contract or a payment instruction.

6. **A gate that was checking nothing.** `npx tsc --noEmit` — the command
   `estonia-space-hub/CLAUDE.md` mandates before any edit is complete — type-checks **zero
   files and exits 0**, because the root `tsconfig.json` is a solution file (`"files": []`
   plus `references`). Vite builds with esbuild, which does not type-check either, so a
   genuine error (an undefined identifier) built cleanly and would have crashed at runtime.
   Fixed by adding `npm run typecheck` (`tsc --noEmit -p tsconfig.app.json`). **The project
   CLAUDE.md still names the broken command and should be updated.**
