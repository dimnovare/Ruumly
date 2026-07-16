# Lead-ops v3: fast sending, provider quote form, restrained UI — design spec

Date: 2026-07-16. Approved by founder (AskUserQuestion): instant-alert + 1-click send;
tokenized provider quote page that auto-seeds the offer (migration OK); restrained ops UI.
Builds on the v2 guided workspace (2026-07-15 spec, shipped). Context: Allar's real request
sat ~1 day before outreach; the workspace reads too colourful; offer-building is manual.
Keeps Sergei's manual quality gate — outreach is faster but never fully automatic.

## Feature A — Fast sending (human-gated)

**A1. Instant actionable alert.** The concierge intake already emails the ops inbox on lead
create (`OpsInbox.ResolveAsync`, SupportController). Enrich that email: (a) a deep link that
opens the workspace on this lead — `https://ruumly.eu/{et}/admin?tab=leads&lead={id}`; (b) the
lead facts (category, city/route, date, details) already present; (c) a matched-provider count
("N providers within 25 km"). No new channel now; the email is the instant phone alert.
Frontend: `AdminLeads` must honor `?lead={id}` — auto-expand that lead's workspace on load
(add the query-param → expanded-row wiring if absent).

**A2. One-click quick-send.** On a New / uncontacted lead, a primary "Send outreach to N
matched providers" action that: fetches `provider-candidates?scope=nearby` (with email),
pre-selects them, and opens the EXISTING outreach review sheet (exact message shown) in one
step. Human-gated (the review sheet still confirms). This is a shortcut over the current
multi-step select — no new backend. SMS/push is an explicit later add-on, not this round.

## Feature B — Tokenized provider quote form → auto-seeds the offer (MIGRATION)

### Data model (EF migration — additive, nullable)
`ProviderOutreach` gains: `QuoteToken` (string, 32-byte url-safe base64, unique index,
generated per row on send), `QuotedAmount` (decimal?), `QuotedUnit` (string?),
`QuotedAvailability` (string?), `QuotedNote` (string?), `QuotedAt` (DateTime?). `Status`
already has `Replied`. Backfill: none (existing rows get null token; only new outreach has a
link). Deploy backend-first.

### Endpoints (public, anonymous, rate-limited like /leads/request)
- `GET /api/quote/{token}` → `{ provider:{ name }, lead:{ category, city, toCity?, needDate?,
  details? }, currency:"EUR", alreadySubmitted:bool, existing:{ amount, unit, availability,
  note }? }`. **NO customer name/email/phone** — the provider only sees what they're quoting.
  404 for unknown/expired token.
- `POST /api/quote/{token}` `{ priceAmount, priceUnit?, availability?, note? }` (validated:
  amount ≥ 0, strings clamped, no `<>`): sets outreach `Status=Replied`, stores the quote
  fields + `QuotedAt`; **find-or-create the lead's newest Draft offer** and **add-or-update an
  OfferOption keyed by the outreach SupplierId** (re-submit updates the same option, never
  duplicates), Title = supplier/location + city, `PriceAmount/PriceUnit/Notes` from the
  submission (sortOrder appended); alert the ops inbox ("Provider {name} quoted {amount}
  {unit} for lead in {city}"); idempotent; returns a thank-you DTO. Creates NO customer email,
  NO Booking/Order. The admin later reviews the pre-seeded draft (Stage 2/3) and sends.

### Outreach email
`outreach_to_provider` (EmailTranslations ×5) primary CTA becomes **"Submit your price →
{https://ruumly.eu/{lang}/quote/{token}}"** (replaces "reply to this email" as the lead
action; Reply-To stays the ops inbox as the fallback). Token per recipient row.

### Frontend
- Public page `/{lang}/quote/{token}` (5 langs, **noindex**, minimal chrome): shows "Quote for
  Ruumly", the lead ask (category/city/date/size — no PII), a form (price, unit select,
  availability, note), submit → thank-you; already-submitted → prefilled + "update your quote";
  invalid/expired token → clean state. Mobile-first.
- Admin: outreach-history rows show **"Quoted {amount} {unit}"** with `QuotedAt` when present;
  the offer draft shows the auto-seeded options (badge "from provider quote"). The existing
  Stage-2 "Add to offer" stays for manual/other-channel quotes.

## Feature C — Restrained ops UI

Restyle the admin lead workspace + list (NOT the public marketing site). Per ui-ux-pro-max
"Data-Dense Dashboard" + the v2 spec §7:
- Neutral **slate/gray** base surfaces; brand **navy** for primary actions only; teal used
  sparingly (accents/links). Status via compact **icon+color badges**, never color alone —
  green=replied/booked, amber=waiting/sent, red=lost, slate=new/neutral.
- Compact data-dense rows; consistent 8px spacing scale; **no cards nested in cards**; dividers
  and full-width bands between stages. Metrics cards toned to neutral with one accent figure.
- Keep 44px touch targets, visible focus, WCAG AA contrast (≥4.5:1). Reduce the count of
  simultaneous accent colors on screen to ≤2 + semantic status.
- Scope: `src/components/admin/leads/*`, `AdminLeads.tsx` metrics/list, and the new quote page.
  Use existing design tokens; adjust *usage*, don't invent a new palette.

## Testing & release
- Backend: migration applies locally (port 5433); quote GET exposes no PII; POST seeds/updates
  one option idempotently + marks Replied + alerts ops + creates no customer email/Booking;
  unknown token 404; rate-limited. Full suite green.
- Frontend: tsc/lint/vitest(parity)/build/Playwright; quote page happy-path + already-submitted
  + invalid token; quick-send pre-selects + opens review; `?lead=` auto-expands; restyle keeps
  e2e green; 375px + 1440px no overflow/clipping.
- Deploy backend-first (migration), then frontend. Canary with a dedicated test lead + a test
  outreach token (submit a quote, verify it seeds the draft) — **no real provider/customer
  email sent** without action-time confirmation.

## Non-goals (unchanged)
- Fully automatic outreach without admin review (A stays human-gated).
- Inbound-email parsing / webhooks (quotes come via the tokenized page only).
- Auto-sending the customer offer (admin still reviews + sends the seeded draft).
- Payment/Booking/Order/contract creation on quote submit.
- Restyling the public marketing site.
