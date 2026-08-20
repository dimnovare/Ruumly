# Claude Design brief — Ruumly

This brief was generated from the **real** Ruumly codebase (source read +
live-product walkthrough on 2026-08-20), not from imagination. Work from the
current product; do not redesign it into a different one.

---

## 0. Hard boundaries (read first)

**Do NOT change product logic, backend behaviour, business rules, provider
capabilities, matching radii, contact policy, pricing, opt-out logic, or claim
security.** Improve only: **clarity, usability, hierarchy, efficiency,
consistency, visual quality.** Product behaviour is owned by the existing
software and by the Claude Code findings in `RUUMLY_FULL_AUDIT.md`.

Specific non-negotiables from the founder brief:

- **Never present functionality that does not exist.** No "instant booking",
  "verified providers", or "guaranteed response times" unless the code enforces
  them. The one deliberately-kept quantity claim is **"up to 3 offers"** ("kuni 3
  pakkumist") — keep it, do not inflate it.
- **Services are exactly five, consumer-selectable:**
  `warehouse | moving | trailer | cleaning | vanrental`. `packing` and
  `insurance` are retained for data only — **never** surface them as selectable
  services.
- **Geography honesty:** services are run out of **Tallinn/Harjumaa**; the
  **directory** spans **Estonia, Latvia and Lithuania**. Where one sentence
  carries both, attach the country list to the *directory* noun and leave the
  *offer* promise country-free. Never let a Rīga/Vilnius visitor read full-service
  parity into it.
- **Estonian is formal (`teie`), never `sina`,** in all provider-facing surfaces.
- **Do not** build thousands of empty programmatic-SEO pages, deep partner
  self-serve features, or a giant provider SaaS portal.
- **Five languages** everywhere: et / en / ru / lv / lt. Never ship a raw
  translation key or a mixed-language screen.

---

## 1. Product context

Ruumly is an Estonia-run, Baltics-wide **demand-first concierge** for local
services (storage, moving, trailer rental, cleaning, van rental). The customer
describes a need once; Ruumly fans it out to suitable nearby providers, collects
their prices, and returns the customer up to three offers. It replaces "open ten
websites and phone around" with one request.

Core line: **"Kümnete otsingute asemel üks päring"** — one request instead of
dozens of searches.

Three connected products, one workflow:

- **Customer** — a normal person with a life event ("I'm moving"). Must
  understand it in 5 seconds, complete a short honest form, and trust it enough to
  leave contact details.
- **Supplier** — a small Baltic business, on their phone, with 30–60 seconds.
  Must recognise a real customer request and answer (price / question / no) in
  under a minute, without an account.
- **Admin** — one founder running the daily match loop. Must see what needs
  attention now and move each request to its next action with the fewest clicks.

---

## 2. Current design system (preserve this)

Extracted from `src/index.css` and `tailwind.config.ts`. It is coherent and
Nordic-clean — **keep it; refine, do not replace.**

**Colour (HSL tokens, light theme):**
- Background `#F4F6FB`, card `#FFFFFF`, foreground `#141A2E`.
- **Primary navy** `#173B8D` (`--primary`); **navy-ink** `#0E2156` (headings,
  active sidebar).
- **Accent green** `#0A9881` (`--accent`, primary CTAs, focus ring).
- **Teal** `#51CDD4` / teal-deep `#1FA6AE` (icons, secondary accents).
- Semantic: destructive `#D8453C`, success `#0F9D6E`, warning `#C97A12`, info
  (blue). **Each has a paired `-text` token** (e.g. `--destructive-text`,
  `--success-text`, `--teal-text`) tuned to ≥4.5:1 on white — **use the `-text`
  token for text, the fill token for backgrounds.** This distinction is
  deliberate and load-bearing for WCAG AA; preserve it.

**Typography:** `Plus Jakarta Sans` (400–800) for everything incl. `.font-display`
headings (800, tight tracking); `DM Sans` and `JetBrains Mono` (`--font-data`,
mono labels) available. All loaded from Google Fonts.

**Shape & spacing:** `--radius: 0.875rem` (14px cards); 44px minimum touch
targets already used on chips and service cards; shadcn/ui (Radix + Tailwind)
primitives throughout.

**Theme:** primarily a single light theme (one `.dark`/`prefers-color-scheme`
block exists but the product is light-first). You may keep it single-theme.

---

## 3. Screens to review (real routes)

Grouped. Use these exact routes.

**Customer**
- `/{lang}` — homepage (ConciergeHome hero; `conciergeFirst=true`)
- `/{lang}/request` — the 3-step concierge wizard **(highest design value)**
- `/{lang}/request-status/{token}` — the waiting/receipt page
- `/{lang}/search`, `/{lang}/storage/{city}` etc. — browse/directory
- `/{lang}/location/{id}`, `/{lang}/partner/{slug}` — detail pages

**Supplier**
- `/{lang}/quote/{token}` — the quote-by-token page **(highest design value)**
- `/{lang}/claim/{slug}` — profile claim
- `/{lang}/provider` — marketing page
- `/{lang}/provider/dashboard`, `/{lang}/provider/onboarding`

**Admin**
- `/{lang}/admin` (Today cockpit), `?tab=leads` — the lead workspace
  **(highest design value)**
- `?tab=suppliers`, `?tab=metrics`, and the AdminSidebar / Cmd+K palette

---

## 4. Screen-specific problems

### Homepage `/{lang}`
- **Problem:** above the fold offers six competing destinations (hero CTA, "browse
  yourself", four need-chips) plus the navbar's own CTA and mega-menu. The
  single-action framing is diluted at the decision moment. The `<h1>` reuses the
  `/request` heading verbatim — it opens with a question, never stating what
  Ruumly *is*.
- **Objective:** one dominant action (the request CTA), the need-chips as its
  supporting shortcuts; move "browse partners" below the service grid. Give the
  homepage its own H1 that names the service.
- **Keep:** the honest trust strip, the service grid, the "how it works" three
  steps, the geography-honest sub-copy.
- **Do not:** add a second competing CTA; add a hero carousel; enumerate LV/LT for
  services (only the directory is Baltic).

### `/{lang}/request` — the wizard (top priority)
- **Problem:** 3 screens / ~16 taps for a single-service move; step 1 is a whole
  screen for one tap; Next/Submit sit below the fold on mobile; step 3 shows 5
  inputs around 1 required field with no read-back; the same 40-word paragraph is
  reused as the form sub-head.
- **Objective:** a mobile-first wizard that feels like 2 steps, not 3 — merge the
  service pick into the details step; sticky bottom nav; a compact
  service+city+date summary above the email field; intelligent date shortcuts
  (this week / next week) beside the picker.
- **Keep:** the per-service scope chips, the photo uploader, the from/to fields,
  the "my date is flexible" affordance, the honesty of the copy.
- **Do not:** remove any scope question that a provider needs to quote; add a
  progress modal; force an account.

### `/{lang}/quote/{token}` — the quote page (top priority)
- **Problem:** three provider answers now exist (price / need-info / **decline**,
  just shipped) but their visual hierarchy must stay strictly ranked — price
  dominant, question quieter, decline quietest. The ask-summary chips are the
  3-second read and must be instantly scannable on a phone on a job site. The page
  has no legitimacy footer (the *email* carries the legal line; the page a
  suspicious cold recipient lands on does not).
- **Objective:** make the price form unmistakably the primary action; the
  need-info trigger a clear-but-quiet secondary; the decline trigger the quietest
  (muted text + outline danger confirm, never a filled button). Add the operator
  legal line to the footer for trust.
- **Keep:** the ask-summary chips (category / city→city / date), the photo
  gallery, the scope-answer list, the "we don't share your PII" line, the
  already-submitted / closed / declined states.
- **Do not:** add customer PII anywhere; make decline visually compete with price.

### `/{lang}/provider` — marketing page
- **Problem:** leads with a badge and a collapsed 28-item paid catalogue; the one
  thing that proves the proposition — a real, redacted customer enquiry — is
  nowhere. ET copy is informal (`sina`) while the emails are formal (`teie`).
- **Objective:** replace the generic trust strip with a single anonymised enquiry
  card rendered in the *same* layout the quote page uses ("Tallinn → Tartu,
  2-room flat, 12 Sept, piano, 3 photos"). Demote the catalogue further.
- **Do not:** lead with dashboards, integrations, or paid placement.

### `/{lang}/admin?tab=leads` — the lead workspace (top priority)
- **Problem:** the operator scrolls past a 50-row unvirtualized candidate list to
  reach the outreach history and the offer editor; the customer *name* isn't shown
  without opening the edit form; lead *source* and match reason aren't shown;
  there's no single "next action" line; nothing polls.
- **Objective:** a one-screen workspace — customer identity + contact, the ask +
  scope, matched providers with distance and last-contacted, outreach delivery
  status, quotes, offer state, and **one derived next-action sentence** — with the
  candidate list collapsed once outreach exists. Information-dense but calm.
- **Keep:** the deep-link-to-lead behaviour, the auto-seeded offer options, the
  provenance badges, the delivery-status panel.
- **Do not:** turn it into an enterprise RFQ tool; add charts where operational
  lists belong.

### `/{lang}/admin` — Today cockpit
- **Problem:** shows only the `needsResponse` queue; `blocked` and `stalled` are
  already in the payload, and "customer chose" / "quotes in draft" have no surface
  at all.
- **Objective:** a queue-led cockpit that answers "what needs attention now" with
  4 counters (needs-response, blocked-on-us, stalled, customer-chose), each a
  one-click deep link. Operations first, analytics second.

---

## 5. Design goals by role & density

- **Customer — low density.** Large clear selection cards, generous spacing,
  progressive disclosure, one action per screen, obvious progress, mobile-first.
- **Supplier — low-to-medium density.** The quote page is a single calm column;
  the ask is scannable in 3 seconds; the primary action is unmissable.
- **Admin — medium/high density with strong hierarchy.** An operations console,
  not a marketing dashboard. Dense lists, tight rows, strong type scale to
  separate levels — but never cluttered. One clear next action per lead.

Do not apply one density philosophy to all three.

---

## 6. Priority screens (design these first, in order)

1. `/request` wizard (customer conversion)
2. `/quote/{token}` (supplier response — the loop depends on it)
3. `/admin?tab=leads` workspace (operator throughput)
4. `/admin` Today cockpit (operator triage)
5. `/provider` marketing page (supply acquisition)
6. Homepage hero (first impression)

Do not spread equal effort across 50 low-value routes.

---

## 7. Components needing attention (real names)

- **Service cards** (`RequestPage` need-options, `HomePage` verticals)
- **Progress wizard** (`RequestPage` step header + bar)
- **Scope chip groups** (`RequestScopeSections`, `QuoteLeadScope`)
- **Lead cards / rows** (`AdminLeads` `LeadCard`, `LeadWorkspace`)
- **Provider response rows & offer editor** (`LeadOfferStage`,
  `components/offers/*`)
- **Status badges & queue chips** (`leadStatusStyles`, `kit/StatusBadge`,
  `AdminLeads` `QueueChip`) — one consistent system across customer/admin
- **The three quote-page answer surfaces** (price form, `QuoteNeedInfo`,
  `QuoteDecline`) — their relative visual weight is the design problem
- **Empty / error / loading states** (`kit/EmptyState`, `kit/SectionError`,
  skeletons) — make the error state visibly distinct from the empty state
- **Email-style quote/offer cards** (`OfferComparison`, `OfferPresentation`)
- **Admin navigation** (`AdminSidebar`, `AdminCommandPalette`)

---

## 8. Interaction & state design

Design every state, not just the happy path: hover, focus (the green ring is a
token — honour it), selected, disabled, loading, empty, **error (distinct from
empty)**, success. Animations should aid understanding (a chip confirming
selection, a panel disclosure) — never decorate. Respect
`prefers-reduced-motion`.

---

## 9. Responsiveness

Design and verify at **320 / 375 / 390 / 430 / 768 / 1024 / 1440+**. Hard rules:
no horizontal page scroll; primary CTA never below the fold on the wizard; 44px
touch targets; wide admin tables scroll inside their own container, never the
page.

---

## 10. Visual direction

Ruumly should feel **modern, premium, Baltic/Nordic, calm, trustworthy,
approachable, efficient.** Avoid: generic SaaS gradient overload, endless nested
cards, cavernous whitespace, Airbnb pastiche, childish illustration, crypto
aesthetics, gratuitous glassmorphism, dashboard chart-overload, complexity
without function.

---

## 11. Output requested

For each priority screen: the problem restated, a desktop layout, a mobile
layout, the interaction states, accessibility notes (contrast pairs using the
`-text` tokens, focus order, labels), and implementation-ready guidance keyed to
the real component names above. If you can edit the UI code directly, do so
carefully and within the boundaries in §0; otherwise produce
implementation-ready instructions. **Do not invent product functionality, change
backend behaviour, or add business rules** — improve clarity, usability,
hierarchy, efficiency, consistency, and visual quality only.
