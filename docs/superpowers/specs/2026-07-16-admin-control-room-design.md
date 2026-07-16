# Admin control room — heavy UI/UX re-organisation

Date: 2026-07-16. Founder-approved (AskUserQuestion ×3, all recommended options):
ops-first grouped sidebar + Cmd+K, "Today" ops cockpit dashboard, shell + shared kit
applied to every admin screen. Context: the admin grew marketplace-era — 21 flat sidebar
items with Leads (the daily concierge loop, the whole business per the pivot) buried 14th.
The admin must become an **ops cockpit for the concierge loop**; marketplace ops stay
fully functional but visually and navigationally secondary.

Design grounding: ui-ux-pro-max "Data-Dense Dashboard" pattern (KPI-first, minimal padding,
maximum data visibility, filtering everywhere, WCAG AA, explicitly avoid ornament) +
frontend-design skill (committed aesthetic, no generic AI slop). Aesthetic direction:
**"control room"** — dense, calm, precise. NOT a marketing surface.

## 1. Design tokens (extend, don't fork, the existing system)

- Brand stays: navy ink `#173B8D` family, green `#0A9881`, teal `#51CDD4` accent,
  Plus Jakarta Sans for display/headers. REJECT the tool-suggested blue/amber palette —
  brand consistency wins; we adopt the structural guidance only.
- **New: data font.** All numerics (counts, money, dates in tables, KPI values) render in
  a monospaced font with tabular figures — `JetBrains Mono` (fallback `Fira Code`, then
  `ui-monospace`). This is the one distinctive "control room" signature: numbers align,
  read instantly, and the admin feels like an instrument panel. Load only needed weights
  (400/600), `font-display: swap`, self-host or Google Fonts consistent with the repo's
  existing font loading. Expose as a Tailwind utility (e.g. `font-data`) + CSS var.
- Density: compact row heights (40-44px rows in tables), 4/8pt spacing rhythm, generous
  section spacing but tight intra-component padding.
- Color discipline: slate surfaces, navy primary, ONE teal accent per view; status always
  icon+text+color (never color alone); all text ≥4.5:1, glyphs ≥3:1 (the AA rule broken
  once already — permanent).
- Dark sidebar rail (navy ink) with light content area — the rail is the instrument
  panel's frame; content stays light for data legibility.

## 2. Navigation: ops-first grouped sidebar + Cmd+K

Replace the 21-item flat list in `AdminSidebar.tsx` with 4 labeled groups, priority order:

1. **OPERATE** (always expanded, top): Overview (`/admin`), Leads (`?tab=leads`) with a
   **needs-response count badge** (derive from existing endpoints: `GET /admin/leads?
   needsResponse=true&limit=1` → `total`, polled/staleTime ~60s), Inquiries.
2. **SUPPLY**: Partners (`/admin/partners`), Locations, Listings, Applications.
3. **COMMERCE** (collapsed by default, persisted per-user in localStorage): Orders,
   Payouts, Rebates, Disputes, Boosts/Feature catalog, Feature requests.
4. **PLATFORM** (collapsed by default): Users, Routing, Integrations, Blog, Settings,
   Activity log, Health/Ops.

- Group headers: small uppercase labels; chevron toggle; groups containing the ACTIVE item
  auto-expand. Active item: teal left-edge indicator + navy fill (not color alone — also
  aria-current="page").
- Mobile: same groups in the existing drawer/sheet pattern.
- **Cmd+K command palette** (shadcn Command in a Dialog): opens with Cmd/Ctrl+K and a
  visible search button in the shell header. Contents: all nav destinations (grouped as
  above) + live search of partners by name (suppliers already fully loaded client-side via
  supplierService.getAll → navigate to /admin/partners/{id}) + leads by name/email/city
  (fetch on ≥2 chars, debounced, existing admin leads list endpoint → navigate to
  ?tab=leads&lead={id}, which already auto-expands + scrolls). Keyboard-first, a11y per
  shadcn defaults. i18n ×5.

## 3. "Today" ops cockpit (AdminDashboard rebuild)

Landing screen = what needs doing, not what exists:

1. **North-star row** (4 StatCards, mono data font): qualified requests/week, supplier
   match rate (matchRate30d.rate, matched/total subtitle), quote→booking 30d, median first
   response. Contact rate secondary. Source: existing GET /admin/leads/metrics.
2. **Needs response queue**: uncontacted leads oldest-first (GET /admin/leads?
   needsResponse=true), each row: age (mono, amber >24h, red >48h), name, category chip,
   city, one-click → the lead workspace (?tab=leads&lead={id}). Empty state: "Inbox zero —
   no waiting requests" with a subtle checkmark. Cap 8 rows + "view all".
3. **Activity strip**: recent offer events derived from existing lead/offer data (offers
   sent/viewed/chosen, quotes received) — most recent 6, relative timestamps. If deriving
   cleanly from current endpoints is heavy, show the most recent leads with their
   offer/outreach status summary instead — do NOT invent new backend endpoints this round.
4. **Supply gaps**: unmatched leads 30d grouped by category+city (client-side from the
   leads list) — "2× cleaning / Pärnu" style chips linking to filtered leads view.
5. **Platform row** (secondary, collapsed-feel): the current dashboard stats (users,
   partners, listings, orders) compacted into one row of small StatCards.

## 4. Shared admin kit (`src/components/admin/kit/`)

Small, boring, reused everywhere:
- `AdminPageHeader` — eyebrow (group name), title, count subtitle, actions slot; replaces
  the ad-hoc headers on every screen.
- `StatCard` — label, mono value, delta/subtitle, optional icon; the ONLY KPI card.
- `DataTable` — thin wrapper over existing table markup: sticky header, compact rows,
  mono numeric cells (`font-data tabular-nums`), zebra-free (borders only), sortable
  column affordance where already sortable, consistent empty + loading (skeleton) states.
  NOT a new table library — a styled composition of what exists.
- `StatusBadge` — one badge component for ALL admin statuses (maps the existing
  leadStatusStyles + order/payout/dispute statuses): icon + text + AA-checked colors.
- `FilterBar` — horizontal container standardizing search input + selects/toggles layout.
- `EmptyState` — icon, one-liner, optional action.
All kit components: i18n-agnostic (labels passed in), 44px touch targets, visible focus.

## 5. Apply everywhere (wave 2)

Every `components/admin/*.tsx` screen adopts the kit: AdminSuppliers, AdminListings,
AdminOrders, AdminPayouts, AdminRebates, AdminRouting, AdminIntegrations, AdminInquiries,
AdminDisputes, AdminUsers, AdminAudit, AdminOps, AdminMetrics, AdminPaidFeatures,
AdminBoosts, AdminBlogPage, AdminAboutPage, AdminSettings, AdminDashboard(cockpit),
AdminLocations (kit-align only — it was just rebuilt), AdminLeads (kit-align the list
shell only — the 3-stage workspace shipped this week is NOT redesigned), plus
AdminPartnerListPage/AdminPartnerDetailPage.
- Heaviest screens get individually reviewed layouts: Orders, Suppliers/Applications,
  Listings, Payouts, PartnerDetail.
- Rule: behavior-preserving. No endpoint changes, no flow changes, no removed features.
  Refactors that shrink files are welcome (AdminPartnerDetailPage 1238 LOC, AdminLocations
  1139 LOC may split into sections) but must stay mechanical.

## 6. Non-goals

- No backend changes. No new endpoints. (Needs-response badge uses existing params.)
- The lead workspace internals (LeadWorkspace/stages) — shipped + reviewed this week —
  keep their layout; only their outer shell/page header aligns to the kit.
- No dark-mode content theme this round (the rail is dark; content stays light).
- Public site untouched.

## 7. Gates & rollout

Per wave: tsc app+e2e, lint, vitest (i18n parity ×5 for new keys), build, full Playwright
(update selectors where headers/labels moved; add: sidebar groups render + collapse +
active-group auto-expand, Cmd+K opens/searches/navigates, cockpit renders metrics +
needs-response rows, badge shows). Mobile 375px + desktop 1440px screenshots of the shell,
cockpit, and 3 heaviest screens. Adversarial review after both waves; deploy after review.
