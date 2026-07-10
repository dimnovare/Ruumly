# Overhaul: event-first UX + full concierge offer loop — design spec

Date: 2026-07-10. Approved by founder (AskUserQuestion): full provider-outreach loop,
Services mega-menu, full homepage redesign. Context: Sergei Anikin pivot (docs/ROADMAP.md,
memory pivot-concierge-direction). GSC 3-mo data: storage city hubs are the only ranking
pages (tartu pos 7–12 wins clicks; **tallinn 116 impressions at pos 49 = biggest SEO
opportunity**); queries = EN "storage near me" cluster + ET "laopindade rent"; mobile CTR
9× desktop. New categories have zero search visibility yet.

## 1. Map: clustering + per-category pins (replaces circle spread)

The 5-decimal circle spread shipped in b989fd6 reads as an artificial ring around Tallinn
(founder screenshot). Replace with **Leaflet.markercluster**:

- Clusters show count bubbles (brand navy, white count); click zooms in; at max zoom
  **spiderfy** fans out stacked pins — the standard solution for identical coords.
  Remove the circle-spread code path (`computeSpreadCoords`) entirely.
- **Per-category pin icons** (7): small teardrop/round divIcon, distinct fill per category
  + white Lucide glyph: warehouse=Warehouse, moving=Truck, trailer=Caravan/Car,
  cleaning=Sparkles, packing=Package, vanrental=Bus, insurance=Shield. Colors from an
  accessible 7-hue set anchored on brand (navy/teal family + distinct hues for the rest);
  ≥3:1 against the light map. Listing pins (marketplace) keep current look.
- Legend swatches must use the same icon+color per category (fixes "same dot for all").
- Applies to both homepage and search maps (shared InteractiveMap).
- Dep: leaflet.markercluster (+types). Popup/link behavior unchanged (true coords).

## 2. Navbar: Services mega-menu

Desktop top level: **Services ▾ · How it works · Blog · [auth/lang] · CTA "Get offers"**
(→ /request). Storage/Moving/Trailer top-level links removed.

- Services panel: 7 rows/tiles (category icon + name + one-liner) linking to
  /search?type={slug}; footer row in panel: "Not sure? Tell us what you need →" → /request.
- A11y: button with aria-expanded/aria-controls, opens on click (hover optional), Escape
  closes, focus returns to trigger; panel links are plain anchors (deep-linkable).
- Mobile drawer: "Services" accordion with the same 7 + CTA. Bottom sticky CTA unchanged.
- Provider link stays where it is today (footer + provider page reachable).

## 3. Homepage: full redesign (conciergeFirst=true branch only)

Event-first narrative, sections in order:
1. **Hero**: headline (existing request.hero.* keys), sub, primary CTA → /request,
   secondary "Browse services"; **popular-need chips** (Moving home, Storage while
   renovating, Moving to Tallinn, …) prefilling /request?category=…&city=….
2. **7-service grid**: one card per category — icon, name, one-liner (unified copy),
   "Browse" → search?type= + "Get offers" → request?category=. Replaces 3-vertical cards.
3. **Map** (clustered category pins) + count badge + legend.
4. **How the concierge works**: 3 steps (tell us → we match & call → you choose), reuse
   hiw keys where possible.
5. **Trust strip**: live numbers (X providers, 7 services, Y cities) from /locations data
   + "free for customers" + response-time promise.
6. **FAQ** (existing) + closing CTA.
Marketplace branch (conciergeFirst=false) stays as is. Mobile-first; no new heavy deps;
sections lazy where below fold. SEO: h1 = event framing; meta from seo.* keys.

## 4. Copy/SEO unification (all pages, 5 langs)

- One canonical name + one-liner per service, used verbatim in navbar panel, home grid,
  search chips, request step-1 cards, FAQ, footer (i18n keys, not copies).
- Apply remaining page-copy-audit NEW keys that fit this structure (trustChips, vertical
  cards, popular chips, request.need.hint, search.empty.request*, search.requestBanner).
- GSC-driven meta: EN pages work "storage near me/self storage {city}" phrasing into
  seo.* titles/descriptions naturally; ET uses "laopind / laopindade rent / kolimine";
  city-hub pages (esp. **Tallinn**) get an SEO content block: provider count, category
  links, 2-3 city-specific FAQ lines, internal links home↔hubs↔categories.
- Keep geography honesty rule (directory = all Estonia, ops = Tallinn/Harjumaa first).

## 5. Admin: full offer loop (backend + admin UI + public offer page)

### Data model (EF migration)
- **Offer**: Id, DemandLeadId FK, Token (32-byte url-safe, unique index), Status enum
  Draft|Sent|Viewed|Chosen|Expired, Language, CustomerNote (optional, shown on page),
  CreatedAt, SentAt?, ViewedAt?, ChosenAt?, ChosenOptionId?, CreatedBy (admin email).
- **OfferOption**: Id, OfferId FK, SupplierId?, SupplierLocationId?, Title, PriceAmount
  decimal?, PriceUnit string?, Notes, SortOrder. (Supplier optional → free-form option OK.)
- **ProviderOutreach**: Id, DemandLeadId FK, SupplierId FK, SentTo (email snapshot),
  SentAt, Status enum Sent|Replied|Declined|NoAnswer (manually updated), Note.

### Endpoints
Admin (existing admin auth):
- POST /api/admin/leads/{id}/offers → create draft (optionally seeded from match ids)
- GET /api/admin/leads/{id}/offers · GET /api/admin/offers/{id}
- PATCH /api/admin/offers/{id} → options CRUD (replace-set semantics), customerNote
- POST /api/admin/offers/{id}/send → validates ≥1 option + lead email; sends offer email
  (EmailTranslations, lead's language); Status→Sent, SentAt; lead auto→Quoted.
- POST /api/admin/leads/{id}/outreach {supplierIds[]} → per supplier with a contactEmail:
  availability-request email; creates ProviderOutreach rows; skips+reports suppliers
  without email. Lead auto→Contacted (if New/Received).
- PATCH /api/admin/outreach/{id} {status, note}
Public (rate-limited, anonymous):
- GET /api/offers/{token} → sanitized offer (options, note, lead summary: category, city,
  size, date — NO customer PII beyond what they submitted themselves; it's their page);
  first hit sets Viewed/ViewedAt.
- POST /api/offers/{token}/choose {optionId} → sets Chosen/ChosenAt/ChosenOptionId,
  notifies info@ruumly.eu, idempotent (second choose = 409 or same-option 200).

### Emails (EmailTranslations ×5)
1. offer_to_customer: intro, options table (title, price, notes), big CTA → offer page.
2. outreach_to_provider: "A customer near {city} needs {category} ({size}, {date}) — can
   you take it? Reply to this email." Reply-To info@ruumly.eu. **No customer name/email/
   phone** — admin brokers the intro (also = the paywall for later).
3. offer_chosen_admin_notification (to info@ruumly.eu, ET only is fine).

### Admin UI (?tab=leads)
Lead detail becomes a **workspace** (page or wide drawer):
- Header: status pipeline chips (click to move), lead facts, contact shortcuts
  (tel:/mailto:), notes (existing).
- **Outreach panel**: matches list (existing endpoint) with checkboxes → "Ask availability"
  (shows which have emails); sent outreach rows with status dropdowns.
- **Offer builder**: add option from match (prefills supplier) or blank; fields title/
  price/unit/notes; drag or arrows to order; live email+page preview; Send button with
  confirm; after send show Sent/Viewed/Chosen timestamps.
- **Activity timeline**: derived from timestamps (created, contacted, outreach sent ×N,
  offer sent/viewed/chosen, status changes).

### Public offer page (frontend /offer/{token}, all 5 langs)
Clean, no-nav-noise page: "Your options for {category} in {city}", option cards (title,
price, notes, provider name if set), "Choose this option" → confirm → success state
("We'll confirm with the provider and get back to you"); expired/invalid token state.
Mobile-first. noindex.

## 6. Non-goals / kept as-is
- No brand color change (skill tool suggested orange — rejected; navy/teal is the brand).
- Marketplace infra untouched; conciergeFirst=false branch untouched.
- ruumly-next parity ports later (phase 4+); Vite app remains source of truth.
- Provider reply tracking (webhooks/links) later — outreach status is manual this round.

## Delivery
Two parallel writers: backend agent (Ruumly repo: model+endpoints+emails+tests) and
frontend agent (estonia-space-hub: map/navbar/home/copy first, then admin workspace +
offer page against the endpoint contract above). Gates per repo (dotnet test; tsc/vitest/
build/playwright), adversarial review workflow, then push (backend first).
