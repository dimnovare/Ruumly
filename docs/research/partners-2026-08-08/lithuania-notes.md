# Lithuania provider research — notes

Companion to `lithuania.json`. Research date **2026-08-08**. Greenfield: no pre-existing
Lithuanian data, so nothing to deduplicate against.

**93 rows.** 77 official-sourced (contact details read on the company's own site), 16
aggregator-sourced (marked `"sourceQuality": "aggregator"`).

---

## 1. Counts

### Per city

| City | Rows |
|---|---|
| Vilnius | 55 |
| Kaunas | 29 |
| Klaipėda | 3 |
| Panevėžys | 2 |
| Klaipėdos r. | 1 |
| Vilniaus r. | 1 |
| Šiauliai | 1 |
| Visaginas | 1 |

### Per service (rows can carry several)

| Service | Rows |
|---|---|
| warehouse | 44 |
| moving | 31 |
| packing | 17 |
| cleaning | 17 |
| vanrental | 10 |
| trailer | 6 |
| insurance | 1 |

### City × service

| City | Breakdown |
|---|---|
| Vilnius | warehouse 33, moving 21, packing 13, cleaning 7, vanrental 3, insurance 1 |
| Kaunas | warehouse 10, moving 10, cleaning 6, trailer 5, packing 4, vanrental 4 |
| Klaipėda | cleaning 2, vanrental 1 |
| Panevėžys | cleaning 1, vanrental 1 |
| Klaipėdos r. | trailer 1 |
| Vilniaus r. | warehouse 1 |
| Šiauliai | cleaning 1 |
| Visaginas | vanrental 1 |

### Field completeness

| Field | Filled |
|---|---|
| address | 93 / 93 |
| lat/lng | 93 / 93 |
| contactPhone | 90 / 93 |
| websiteUrl (fetched OK this session) | 82 / 93 |
| contactEmail | 74 / 93 |
| registryCode | 31 / 93 |

Only 3 rows have neither phone nor email. 71 rows are single-service; 22 carry 2–3 services.

---

## 2. IMPORTANT — read the regional numbers as a coverage gap, not a market verdict

**Vilnius + Kaunas are 84 of 93 rows (90%).** Do not read the thin regional counts as proof
those markets are empty. Two hard tool limits truncated the regional sweep:

1. **The session-wide WebSearch budget (200 calls) was exhausted early**, shared with a
   concurrent Latvia research task running in the same session. Everything after that point was
   done by fetching directory category pages and by probing candidate domains directly — a
   method that works well for national brands and for cities with a dense web presence, and
   badly for one-van operators in Tauragė who exist only on Facebook.
2. The planned dedicated researchers for **Klaipėda/Palanga/Šiauliai/Panevėžys**, the **nine
   small cities**, **trailer/van rental**, and **cleaning/insurance** could not be launched — a
   concurrent-subagent cap (shared with the Latvia task) rejected them. Those areas were
   covered by the main thread by hand instead, at lower depth.

So: **Vilnius and Kaunas are genuinely well-covered and ready to use. The regions are
under-researched and should get a second pass**, ideally with search available. Where I do have
positive evidence of genuine market thinness, I say so explicitly below.

### Cities with zero rows

Alytus, Marijampolė, Mažeikiai, Jonava, Utena, Kėdainiai, Telšiai, Tauragė, Palanga, Grigiškės.

Evidence I *do* have about these:

- **Genuinely thin (positive evidence).** Every obvious small-city trailer-rental domain I
  probed is unregistered: `priekabunuomaalytuje.lt`, `priekabunuomautenoje.lt`,
  `priekabunuomamarijampoleje.lt`, `priekabunuomajonavoje.lt`, `priekabunuomataurageje.lt`,
  `priekabunuomatelsiuose.lt`, `priekabunuomamazeikiuose.lt`, `priekabunuomapalangoje.lt`,
  `priekabunuomakedainiuose.lt`, `priekabunuomavisagine.lt`, plus
  `perkraustymassiauliuose.lt` and `perkraustymaskaune.lt`. In these towns trailer rental is a
  side business of a petrol station, `autoservisas` or builders' merchant and has no web
  presence of its own — it will have to be found by phone, on Facebook, or on `skelbiu.lt`,
  not by crawling.
- **Known supply I could not convert into rows.** `daiktams.lt` (self-storage) states branches
  in Vilnius ×2, Kaunas **and Alytus** — an Alytus storage site exists; I did not have a
  verified street address for it. `RBII, UAB` (Mažeikiai, Gamyklos g. 44B) and
  `Kęstučio Tuziko IĮ`, `EISTURAS`, `AVIBUS`, `SEKNIJA` came off the minibus-rental directory
  page; Mažeikiai's RBII turned out to be an international freight carrier rather than a
  rental firm, so I dropped it rather than mis-tag it.
- **Palanga** produced nothing beyond seasonal vehicle rental, consistent with a small resort
  town — but this was not researched to exhaustion.

---

## 3. Which of the seven services barely exist as a distinct market

This is the commercially important section.

### `insurance` — does NOT exist as a consumer category. Recommend not launching it in LT.

Only **1 row** in the whole file, and that is a B2B underwriting agency. Consistent, independent
findings from two research threads:

- What exists is **`krovinių draudimas` / CMR draudimas** — commercial cargo and carrier
  liability cover, sold to hauliers and freight forwarders. `cmrdraudimas.lt` is explicit that
  it covers international carriers' CMR liability only; no household goods.
  `bunda.eu` (Baltic Underwriting Agency) sells cargo plus freight-forwarder and
  warehouse-keeper liability, and lists no residential product at all.
- For a **private house move**, insurance is never a separately bookable service. It appears as
  a line inside the mover's own quote: movers advertise their own civil liability cover as a
  trust signal (`nuvezam.lt` €60,000 civil liability; AMVISTA €29,000; Litkrausta and
  Kraustukai both advertise liability insurance).
- The remainder is generic national insurers (Lietuvos draudimas, BTA, Compensa, ERGO,
  Gjensidige, Balcia) selling home-contents policies — not a moving service, and they have no
  move-specific product.

**Implication:** offering an `insurance` category in Lithuania would present an empty shelf.
Better to model insurance as an *attribute* of a moving provider ("has civil liability cover,
€X") which is what Lithuanian customers actually compare on.

### `packing` — real work, but never sold standalone. Recommend attribute, not category.

17 rows carry `packing`, but **every single one bundles it inside a moving quote.** Direct
evidence:

- `visalietuva.lt` has a dedicated `pakavimo paslaugos` sub-category. It contains **2 companies
  nationally** — and both are ordinary moving firms already listed under moving.
- `Sandėliukų Centras` sells packing **materials** (boxes, tape) but explicitly does not sell a
  packing **service**, and refers customers out to unnamed moving partners.
- Kraustina and Pelikanų transportas both rent/sell packing materials as an add-on to a move.
- The only "pure packing" businesses findable are e-commerce packaging-supply shops selling
  cardboard and tape — a goods business, not a labour service, and a poor fit for the
  marketplace.

**Implication:** keep `packing` as a checkbox on a moving provider rather than a category a
customer browses. A standalone "packing" tab would return the same firms as "moving".

### `trailer` — real and useful, but structurally offline

Only 6 rows, and that understates real supply. Trailer rental in Lithuania is highly fragmented
and overwhelmingly a **side business** of petrol stations, car-service garages and builders'
merchants. It is also the category most prone to mis-tagging: `tauriga.lt` (found via
`priekabos.lt`) is a substantial trailer business but **manufactures, sells and repairs
trailers — it does not rent them**, so it was excluded. Expect to build this category by phone
and Facebook rather than by crawling.

### `vanrental` — exists, but the LT market means *minibuses*, not cargo vans

The Lithuanian category `mikroautobusų nuoma` is dominated by **passenger** minibus rental
(6–30 seats, weddings, group travel, airport runs) rather than the cargo van a mover needs.
`visalietuva.lt`'s minibus-rental category has 21 companies and most are passenger operators.
Genuine cargo/`krovininiai` van rental is a smaller sub-segment (BlackBus is explicitly
"keleivinių, krovininių ir bortinių"). Worth keeping, but the customer intent differs from
Estonia's kaubik rental.

### `warehouse`, `moving`, `cleaning` — all healthy, real categories

These three carry the file (44 / 31 / 17) and are the only ones with enough independent supply
to justify a browsable category on day one.

---

## 4. Incumbent aggregators and competitors

| Player | What it is | Threat / usefulness |
|---|---|---|
| **visalietuva.lt** | The best-structured LT business directory. Paginated categories, per-company profile pages, city locatives (`/perkraustymo-paslaugos/vilniuje`). | Most useful source we found. But **thin where it matters**: 13 companies nationally under moving, 1 in Klaipėda, 2 under packing. Its cleaning category (142 companies) is its strongest. Not a booking product — pure listings. |
| **rekvizitai.vz.lt** | The canonical LT company-registry directory. | Blocked to our fetcher, but it is clearly the reference: `asaura.lt` pulls its customer testimonials from rekvizitai.lt. Whoever does the second pass should try to get at this. |
| **skelbiu.lt / alio.lt / kampas.lt / aruodas.lt** | Classifieds. Dominate warehouse-space and small-operator listings. | Where the small one-van movers and small-town trailer owners actually advertise. Not operators themselves. |
| **paslaugos.lt** and similar | Gig marketplaces of individual freelance movers/cleaners. | **Closest competitor to a concierge model.** Supply is individuals, not registered businesses. |
| **perkraustymopaslaugos.lt** | SEO affiliate content site — "why moving services are great" articles funnelling to `perkraustymai.lt`. | Notable: the incumbent "aggregator" layer in LT moving is largely **SEO doorway/affiliate sites, not real marketplaces**. That is an opportunity — the demand-capture layer is low quality. |
| **Boxrent** | Self-storage chain, 24 locations across LT/LV/PL (7 in Vilnius alone). | The one genuinely scaled, multi-country operator in the file. Regional consolidator. |
| **Box Storage** | Claims to be the largest self-storage provider in the Baltics. | Direct competitor for storage demand; also an obvious partner. |

---

## 5. Price points actually observed

Useful for quoting. All read off the providers' own pages.

**Self-storage** (the best-documented category — LT operators publish rates openly, unlike
movers):
- Box Storage — from **€3.05/week** for 1 m² (promo; standard €7.15/week), up to €39.29/week for 10 m². Minimum term 1 month.
- Boxrent — from **€4.24/week** incl. 21% VAT; large XL units **€180+/month**.
- City Storage — from **€9.99 per 4 weeks**.
- Box Inn (Kaunas) — **€17/month per 1 m³**, **€29/month per 1 m²**, €43 for 1.5 m², €57 for 2 m², €83 for 3 m².
- Daiktams.lt / Bobino — roughly **€15.50–30/month** at the small end.
- Sandėliukų Centras and Space24 are quote-only.

**Moving** (usually hourly, crew + van):
- Low end **€25–40/hour** for a 2-person crew with van.
- Asaura — **€40/hour** for minibus + 1 mover, 1-hour minimum, with a public price calculator.
- Movesta — from **€45/hour**.
- High end **€60–90/hour** for larger crews / specialist work.
- Piano and safe moves are priced separately by most firms.

**Trailer rental:**
- Priekabos24 — **€4/hour** uncovered cargo trailer, **€6/hour** with tarpaulin, **€8/hour** moto trailer, **€18/hour** car-transporter platform.
- Geros Priekabos (Kaunas) — 3 m uncovered from **€6/hour**; covered €8/hour, €12/3 hours, €16/day; small tow trailer €14/hour, €22–25/day.
- MB TOLEMA — **€16–25/day** depending on size (tarpaulin €20/day, 6 m €25/day, car transporter €25/day).

**Van / minibus rental:** roughly **€16–28/hour** with driver.

**Cleaning:** almost entirely **quote-only** — no LT cleaning firm in this set publishes a €/m²
or €/hour rate, which is a notable contrast with storage. The only hard numbers found were
adjacent waste services (Ecoservice: construction waste removal **€217.80–450**, green waste
**€99–129**, bulk-bag removal **€40–169.40**).

---

## 6. Data-quality findings (the anti-rot notes)

The previous Estonian import rotted because unverified rows were imported. Every `websiteUrl`
in `lithuania.json` was fetched successfully during this session; anything that failed is
`null`. What that discipline caught, and what a future import must keep doing:

**Directory-listed websites that are DEAD.** These are all listed as live company websites by
Lithuanian directories, and none of them resolve:
`skanerlita.lt`, `vilniauskroviniai.lt`, `krovimas.lt` (returns HTTP 404 — listed for UAB
KRAUSTVA across five separate visalietuva categories), `sandeliukunuoma.lt` (ranked in organic
search results for the main storage query), `cleanexperts.lt`, `garliavosmikroautobusai.lt`,
`busnuoma.eu`. **A directory listing a website is not evidence the website exists.**

**Domains that return HTTP 200 but are registrar parking pages, not businesses:**
`mikroautobusunuoma.lt`, `perkraustome.lt`, `perkraustymasvilniuje.lt`, `kraustau.lt`,
`sandeliukaivilniuje.lt`, `sandeliaikaune.lt`, `valymasvilniuje.lt`, `valymopaslaugoskaune.lt`,
`sandeliukai24.lt`, `perkraustymai24.lt`, `greitasperkraustymas.lt`, `svarosekspertai.lt`,
`pervezimai.lt`. `svaruma.lt` is a "domenas parduodamas" for-sale page. **Status 200 is not
proof of a business — the page content must be read.**

**Mis-categorisation traps.** `visalietuva.lt`'s "Sandėliavimo paslaugos ir įranga" mixes real
storage-space renters with **shelving and racking equipment sellers** (Sandala, Baltexim,
Montvega, Adopto, and Denzis — a bathroom-fixtures shop) — all excluded after reading their own
sites. `tauriga.lt` makes and sells trailers but does not rent them. `svara.lt` is UAB Kauno
švara, a municipal waste/territory contractor, not a move-out cleaner. `valyklaklaipeda.lt`
(Linartika) is clothing dry-cleaning, not premises cleaning. `relokon.lt` is corporate
relocation *management* (visas, tax, settling) with no physical moving — excluded rather than
mis-tagged as `moving`. `storent.lt` is construction equipment rental.

**Redirects that change the brand.** `priekabos.lt` actually serves `tauriga.lt`;
`valymopaslaugos.lt` serves `vitaresta.lt`; `kraustome.lt`, `kraustyk.lt`, `kraustykis.lt`,
`perkraustom.lt` and `perkraustyk.lt` all serve the single company **Kraustymėlis.lt**. One
company can own a dozen SEO domains — dedupe by address, not by domain. Per the Kaunas
researcher, **Bobino komanda also runs `sandelionuoma.lt`** as a storage sub-brand on the same
phone and address; it would look like a separate company by name alone.

**Geocoding was verified, not trusted.** Coordinates are per-address, not city centres — there
are no duplicate pins in the file. Nominatim was used first, then abandoned when it began
returning HTTP 429; the final pass used Photon. Both produced confidently wrong answers that had
to be caught by cross-checking the returned district/postcode against the postcode printed on
the company's own site:
- `Mozūriškių g. 21` (City Storage) resolved to Žvėrynas 08119; correct is Justiniškės 05213.
- `Galinės g. 1` (Transekspedicija) resolved to Naujoji Vilnia 11320; correct is Galinė 14247.
- `Taikos g. 88, Kaunas` resolved to a point 40 km north of Kaunas entirely.
- MB TOLEMA advertises "priekabų nuoma Klaipėdoje" but its pickup point is **Aisėnų k.,
  Klaipėdos r.** — 25 km from the city. Pinning it in Klaipėda would have been wrong, so it is
  recorded under `Klaipėdos r.`.

A strong confirmation trick worth reusing: Photon frequently returns a POI whose `name` **is**
the company (`City Storage`, `Vilniaus tranzitas`, `Transekspedicija`) at the queried address.

**Two city values fall outside the target list**, deliberately, because the businesses really
are outside the city boundary: `Klaipėdos r.` (MB TOLEMA) and `Vilniaus r.` (Transekspedicija
Invest, in Galinė). Decide how the importer should treat these before loading.

---

## 7. Recommended next pass

1. **Regions first.** Klaipėda, Šiauliai, Panevėžys and the nine small cities need a proper
   sweep with WebSearch available. Vilnius/Kaunas do not need more work.
2. **Warm leads already identified but not converted:** Daiktams.lt's Alytus branch;
   Kęstučio Tuziko IĮ, Nik exspres, EVITRA, Eurorenta, Smartrent, mikroautobusnuoma.lt
   (Vilnius van rental); Kautra, ARTRANSA, G. Babensko įmonė, Transbus (Kaunas);
   AVIBUS (Klaipėda), EISTURAS (Panevėžys), SEKNIJA (Visaginas), RIMVITTRANS (Rokiškis).
3. **Do not build `insurance` as a category in Lithuania**; model it as a provider attribute.
4. **Do not build `packing` as a browsable category**; make it a moving add-on.
5. For small-town `trailer` supply, plan for phone/Facebook sourcing — the web layer is absent.
6. Re-verify every `websiteUrl` before import, and again periodically. The dead-link rate among
   directory-listed Lithuanian sites in this research was roughly **1 in 8**.
