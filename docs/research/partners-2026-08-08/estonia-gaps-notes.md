# Estonia coverage-gap research — 2026-08-08

Companion notes to `estonia-gaps.json` (89 rows, all outside the four already-covered
cities of Tallinn, Tartu, Kuressaare and Pärnu).

## Headline

| | |
|---|---|
| Businesses found | **89** |
| Cities covered | **19** of the 24 target cities |
| With a website that was **fetched OK in this session** | 51 |
| With an email | 74 |
| With a phone | 88 (only one row has none — see Koidu 13 below) |
| With an Estonian registry code | 56 |
| `sourceQuality: official` (contacts read on the business's own site) | 49 |
| `sourceQuality: aggregator` (contacts only from a directory) | 40 |

## Per city

| City | Rows | | City | Rows |
|---|---|---|---|---|
| Viljandi | 11 | | Jõhvi | 4 |
| Haapsalu | 10 | | Valga | 3 |
| Narva | 10 | | Kohtla-Järve | 3 |
| Võru | 9 | | Elva | 2 |
| Rapla | 7 | | Sillamäe | 2 |
| Kärdla | 5 | | Põlva | 1 |
| Paide | 5 | | Jõgeva | 1 |
| Türi | 5 | | Põltsamaa | 1 |
| Rakvere | 5 | | Kiviõli | 1 |
| Keila | 4 | | | |

## Per service

| Service | Rows | Cities reached |
|---|---|---|
| `moving` | 30 | Elva, Haapsalu, Jõhvi, Kohtla-Järve, Kärdla, Narva, Paide, Põlva, Rakvere, Rapla, Sillamäe, Türi, Valga, Viljandi, Võru |
| `trailer` | 18 | Haapsalu, Jõgeva, Jõhvi, Keila, Narva, Paide, Põltsamaa, Rakvere, Rapla, Türi, Viljandi, Võru |
| `vanrental` | 12 | Haapsalu, Kärdla, Narva, Türi, Viljandi, Võru |
| `cleaning` | 12 | Haapsalu, Keila, Narva, Rakvere, Rapla, Türi, Valga, Viljandi |
| `insurance` | 12 | Haapsalu, Jõhvi, Kiviõli, Kärdla, Narva, Paide, Rapla, Sillamäe, Võru |
| `warehouse` | 10 | Haapsalu, Keila, Kohtla-Järve, Kärdla, Narva, Rakvere, Viljandi |
| `packing` | 4 | Jõhvi, Narva, Viljandi |

## The Viljandi Koidu 13 mystery — the rotted row, explained

The bad row in production is **"Miniladu24.eu Viljandi"**, and everything about it checks out
as dead:

- `miniladu24.eu` → **ENOTFOUND**, the domain does not resolve.
- Its soov.ee listing is gone ("Kuulutust ei leitud").
- Its Facebook page could not be fetched.
- **`miniladu24.ee` is a different company** at Lepa tee 4, Loo, Harjumaa — not Viljandi. Do
  not merge the two; that confusion is easy and expensive.

The storage site at **Koidu 13, Viljandi is real** and still advertised (10 m² boxes at
90 €/kuu, 9 m² at 45 €, 4.4 m² at 30 €, indoor, ramp, cameras, 24/7). The business register
shows **BVT Projekt OÜ, registry code 14209956** at that exact address, listed under
warehousing for Viljandi county.

That is the row we emitted — deliberately with **`contactPhone: null`**. The only phone
published anywhere for this site is **+372 527 7638** (with `info@miniladu24.eu`), which is
almost certainly the number the founder already called and found wrong. Rather than
re-import a number with a known failure history, it is recorded here in prose so ops can dial
it *knowing* the provenance, and the row itself carries the verified legal entity and address.

**Another near-miss worth knowing:** a search for "Koidu miniladu" surfaces
`stipend.ee/objektid/koidu-minilaod/` — but that is **Koidu tn 20 in Rapla**, not Viljandi,
and the project is still "in planning phase". It is not in the data.

## Cities with genuinely no providers (a finding, not a failure)

These five target cities returned **nothing importable** after Estonian- and
Russian-language searching plus directory and registry mining:

- **Saue** — genuinely empty. A commuter town whose residents use Tallinn suppliers. The one
  insurance lead (Minu Kindlustusmaakler OÜ) turns out to be an IT company by registered
  activity; the "cleaning" lead is a garment dry-cleaner.
- **Maardu** — nothing survived. Both cleaning leads are **struck off the register**, and the
  only trailer lead (A24 Grupp OÜ) is registered for vehicle *retail*, with trailer rental
  unconfirmed.
- **Tapa** — zero. Only the municipality website surfaces.
- **Kunda** — zero usable. The single warehousing hit, Baltic Tank AS, is a liquid-bulk port
  terminal, not storage a household could rent.
- **Otepää** — zero. Valga-county rental firms all sit in outlying villages (Mäha, Järvekalda,
  Meegaste, Nüpli), never Otepää itself.

Near-empty, worth stating plainly:

- **Põlva, Jõgeva, Põltsamaa, Kiviõli** each yielded exactly one row, and in Jõgeva/Põltsamaa
  that one row is an Espak builders' merchant renting trailers. There is no mover based in
  Põlva town, Jõgeva town or Põltsamaa — the Põlvamaa firms all sit in surrounding villages
  (Himma, Mammaste, Saverna, Kanepi).
- **Self-storage barely exists outside Tallinn/Tartu/Pärnu.** Confirmed `warehouse` supply in
  the whole target list is: Viljandi (Blackline + BVT Projekt), Rakvere (Blackline + Miil),
  Keila (Blackline), Haapsalu (Miil), Kärdla (Hiiu Autotrans), Narva (Kesk Log, NTK Balt),
  Kohtla-Järve (Sipelgas Veod). **Paide, Türi, Rapla, Võru, Valga, Põlva and Jõgeva counties
  returned no self-storage operator at all.**
- **Packing** is essentially not a standalone business regionally — only Pakendikeskus Jõhvi
  sells moving boxes over the counter; everywhere else it is bundled by a mover.

## Competitor aggregators and lead-gen noticed

- **vaikelaod.ee** — "Väikelaod ja miniladud Eestis, võrdle hindu ja asukohti". A direct
  price-comparison competitor for the storage side. It **403s** against automated fetches.
- **spacehubstore.com** — mini-storage booking, Tallinn only. Note the name collision with our
  own `estonia-space-hub` frontend repo.
- **konteinerladu.ee / blackline.ee / miil.ee** — the same commercial group appears under
  several brands; Blackline OÜ (12549033) and Miil OÜ share physical sites (both list
  Vaksali 36 Viljandi and Paldiski mnt 35 Keila). We kept one row per physical address.
- **kolimisfirmad.ee** (`viljandi.`, `rakvere.`, `polva.` subdomains), **klin.ee/linnad/…**,
  **Puhastus24**, **PocketPro**, **Ehituseabi** — SEO city-landing lead-gen networks with no
  local entity behind them. Excluded on sight.
- **16366.ee / infotelefon.ee, 1182.ee, 118finder.ee, e-krediidiinfo.ee, inforegister.ee,
  narva-online.ee** — the useful directories. Note the trap: **16366's county pages carry a
  geographic heading but list Tallinn companies underneath**. That single pattern is what
  makes naive directory scraping manufacture fake local coverage.

## Deliberately excluded, and why

**Struck off the register or being deleted** (these would have rotted the import):
Keila Buss OÜ (deleted 2023), KJ Koristusteenused OÜ Narva (deleted 2022), Tamara Ivanova
koristusteenused FIE Maardu (deleted 2026), **Raikma Teenused / Raikma Capital OÜ, Paide**
(deletion notice published — it was a candidate row and was pulled), MAHV OÜ (deleted 2025),
Ladu NF OÜ Rakvere (in liquidation), **Stairway OÜ Narva** (registered for moving services but
zero revenue, zero employees — a dormant shell).

**Right town, wrong business** — would poison a moving marketplace:
Estvarad OÜ (Paide — advertises kolimisteenused on 16366, but its own site `estvarad.ee` is a
kindergarten- and school-supplies e-shop with no moving content anywhere); Keila Kallur OÜ and
Väljataguse Veod OÜ (tipper/road-construction haulage); Tarkal Trans OÜ Viljandi (invatakso /
medical transport); Vesset Transport Kärdla (forestry); Virumaa Puhastus OÜ (textile dry
cleaning, not premises); A&O Holding Rakvere (grease traps and street sweeping); Auto & Service
Haapsalu (*sells* Respo trailers, does not rent); Maardu Hooldus OÜ (car repair shop); Minu
Kindlustusmaakler OÜ Saue (IT services); MGT-Baas OÜ Sillamäe and Euro Broker Service OÜ
(bonded/customs warehousing — no consumer can store a sofa there).

**Right service, wrong town** (rule: must be based in the target city, not merely "also serve"
it): Tallinn/Tartu national movers (AVA-Ekspress, Tellikolimine, Fids Trans, Multifix,
Kolimisveod, Fastmove, Uksest Ukseni, Move24, Kolimistransport), plus dozens of parish-village
firms counted against their county but not their town — Grupp KaHa (Tõrva), Kolija24 (Kanepi),
KV Korrashoid (Käina), Raplamaarent (Märjamaa), NVTransport (Narva-Jõesuu), RENT-24 (Kaavere).

**Chain branches that do NOT offer the service** — checked individually rather than assumed:
Espak **Rapla, Haapsalu, Keila, Rakvere, Jõhvi and Narva** do not rent trailers (Keila's
`/teenused/haagise-rent/` 404s; the others list paint tinting and delivery only). Only Espak
Paide, Türi, Jõgeva and Põltsamaa do, and only those four are in the data.

**No contact at all** — a row nobody can call is a ghost: Formeteks OÜ (Võru), Cramo Narva
depot (site is JS-rendered, no phone obtainable), Promaxicar (Haapsalu; also would have stacked
a map pin exactly on Ramirent Haapsalu), Edu Autorent and Artsicar (Haapsalu).

**Dormancy / risk signals**, dropped: MRent OÜ Viljandi (`mrent.ee` is now a parked
domain-for-sale page).

## Verify before first contact

These rows are in the file but carry a known caveat — worth a qualifying call before they get
routed real demand:

| Row | Caveat |
|---|---|
| `bvt-projekt-miniladu-viljandi` | Phone deliberately null; only published number is the suspect +372 527 7638 |
| `real-west-haapsalu` | Inforegister marks the company **passive since 2025** |
| `envio-narva` | Flagged as a debtor, ~€46,700 in tax arrears |
| `kominsur-sillamae` | The broker's own site does not list a Sillamäe branch; address is directory-only |
| `jarva-rehvid-turi`, `carrent-hiiumaa-kardla`, `aikerauto-viljandi`, `river-rent-voru`, `merva-voru`, `motorsport-voru`, `raiester-voru` | Listed under generic *autorent*; **cargo-van availability is unconfirmed** |
| `transporter-paide`, `esvo-turi`, `aivil-rapla` | Directory gives no service detail — whether they take household moves is unconfirmed |
| `smart-fleet-voru` | Own site says +372 588 600 88, 1182.ee says 5884 1882 — we kept the own-site number |

## Method and data-quality learnings

- **Every `websiteUrl` in the file was fetched successfully during this session.** Where a fetch
  failed the field is `null` and the business was still kept if it had a phone.
- **Domains that directories confidently publish but are broken** — all nulled, none carried
  through: `miniladu24.eu`, `rk-trans.ee`, `veosiil.ee`, `kolija24.ee`, `bvtprojekt.ee`,
  `promaxicar.ee`, `raes.ee`, `hiiumaarent.ee`, `marvveod.ee`, `dbwauto.ee` (all ENOTFOUND);
  `vistekel.ee` (TLS handshake failure); `markitransport.ee`, `riverrent.ee`, `kesklog.ee`,
  `www.smartfleet.ee`, `puhasroom.ee`, `morobell.ee`, `melamu.ee` (wrong/shared TLS certificate);
  `ekspert.ee` (self-signed); `vedaja.ee` (**expired certificate**); `mrent.ee` (parked).
  That is ~20 dead assets in one pass — a good measure of how fast this data rots.
- **`ariregister.rik.ee/est/company/<regcode>` is fetchable and was the single highest-value
  check.** It caught three struck-off companies and four activity mismatches that every
  directory still lists as live. **Recommend running any future import list through it before
  ingest**, and re-checking the existing 170 the same way.
- **Directory addresses drift.** Two were overridden with current registry data: Sipelgas Veod
  (Sõpruse 3-11 → **Ahtme põik 19a**) and Euro Broker Service (Kangelaste 30-68 →
  Kangelaste 42-67). Assume the same drift elsewhere.
- **Coordinates** were geocoded individually against OpenStreetMap Nominatim at street-address
  level, not repeated from city centres. The build script rejects duplicate lat/lng pairs, so
  no two rows stack on the map. Exactly one row (`ansvil-viljandi`) resolves to the town
  centroid because the business publishes no street address anywhere on its own site — left
  honest rather than invented.
- **Search coverage caveat:** the session's 200-call web-search budget was exhausted partway
  through, and DuckDuckGo began serving CAPTCHAs. The back half of discovery ran on direct
  directory and registry fetches. A further pass with search restored would most likely add
  small owner-operator movers advertising on Facebook groups and okidoki.ee (both 403'd),
  rather than change the picture for Saue, Maardu, Tapa, Kunda and Otepää.
