# GSC re-indexing queue — 2026-08-14

Every URL below now serves a **different `<title>` and `<meta description>` than Google
last crawled**, so the snippet it shows today is stale. Manual requests only accelerate
what the sitemap will do anyway — but for pages already ranking at positions 6–12, the
snippet *is* the click-through, so these are worth the quota.

**Quota:** roughly 10 URL inspections → "Request indexing" per day.
**Flow:** Search Console → URL Inspection → paste URL → Request Indexing.

**Do NOT submit** `/terms`, `/privacy`, `/cookies` in any language — they are now
`noindex` in the served HTML (previously the tag only appeared after React hydrated, which
is why they may currently be indexed as homepage-titled duplicates). Leave them to drop
out naturally.

---

## Day 1 — pages that already rank, or that we most want ranked

| # | URL | Why now |
|---|---|---|
| 1 | `https://ruumly.eu/et` | Title + description both changed; also had a canonical-duplicate resolution pending |
| 2 | `https://ruumly.eu/et/request` | The commercial front door. Its title was `… \| Ruumly — Ruumly` — double-branded in every snippet — and is now clean |
| 3 | `https://ruumly.eu/et/storage/tartu` | One of only two pages currently earning clicks |
| 4 | `https://ruumly.eu/en/storage/tartu` | The other one |
| 5 | `https://ruumly.eu/et/storage/tallinn` | Biggest market, biggest supply |
| 6 | `https://ruumly.eu/en` | English homepage, same changes as ET |
| 7 | `https://ruumly.eu/et/moving/tallinn` | Highest-intent service × biggest city |
| 8 | `https://ruumly.eu/et/cleaning/tallinn` | Largest newer category by supply; hub copy is new |
| 9 | `https://ruumly.eu/et/how-it-works` | Step 3 copy rewritten (the 24-hour promise is gone) |
| 10 | `https://ruumly.eu/et/faq` | Both offer-speed answers rewritten |

## Day 2 — breadth across services and languages

| # | URL |
|---|---|
| 1 | `https://ruumly.eu/en/request` |
| 2 | `https://ruumly.eu/et/vanrental/tallinn` |
| 3 | `https://ruumly.eu/et/trailer/tallinn` |
| 4 | `https://ruumly.eu/et/moving/tartu` |
| 5 | `https://ruumly.eu/et/locations/tallinn` |
| 6 | `https://ruumly.eu/et/storage/parnu` |
| 7 | `https://ruumly.eu/ru/storage/tallinn` |
| 8 | `https://ruumly.eu/en/moving/tallinn` |
| 9 | `https://ruumly.eu/et/locations` |
| 10 | `https://ruumly.eu/et/about` |

## Day 3 — the long tail, only if days 1–2 show movement

Remaining curated city × service combinations (`narva`, `riga`, `vilnius` across all five
verticals), plus the `lv` / `lt` language roots. Low expected volume; the sitemap will
reach them regardless.

---

## What to watch, and when

Give it **7–14 days** before judging anything — a re-crawl does not immediately change a
displayed snippet.

The metric that matters is **CTR at unchanged position**. The whole thesis of this change
is that pages ranked 6–12 were showing the wrong snippet. If position holds and CTR rises,
the fix worked. If position moves too, that is a separate story and not evidence either way.

Do **not** read the first 48 hours as signal.
