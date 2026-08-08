# Latvia provider research — 2026-08-08

Greenfield build of the Latvian provider directory. Companion to `latvia.json` (182 rows).
Research was done in Latvian and Russian via web search, direct site fetches, and Latvian
business directories (firmas.lv, lursoft.lv, zl.lv, 1188.lv, ss.lv/ss.com, viss.lv,
pilseta24.lv, mm.lv) plus Facebook business pages and Google Maps.

---

## 1. Verification standard applied

The Estonian import rotted because a domain didn't resolve and a phone number was wrong when
the founder called it. Countermeasures used here:

- **Every `websiteUrl` was fetched successfully during research**, then **re-verified a second
  time at merge** with an independent HTTP client that does a DNS lookup and a **TLS-verified**
  GET. Final result: **99 distinct domains, 0 dead, 0 DNS failures, 0 invalid certificates.**
- **Nothing was pattern-completed.** No phone, email or registry code appears in the file
  unless it was read verbatim off a fetched page. That is why `contactEmail` is only 116/182
  and `registryCode` only 52/182 — the gaps are real gaps, not lazy ones.
- **Phone shape is enforced mechanically**: `+371` + 8 digits, first digit 2 (mobile),
  6 (landline), 7 (service) or 80 (freephone). Anything else was nulled rather than
  reshaped. 181/182 rows carry a phone; the one exception (All OverSeas, Ventspils) publishes
  its contacts only as images.
- **Coordinates**: 181 of 182 pairs are distinct. The single repeat is genuine — SIA Avector
  and NESTunVEST both publish the *identical* address (Kārļa Ulmaņa gatve 2, a large
  multi-tenant business park), so one pin is the truthful answer.
- **Cross-segment dedupe removed 19 rows**, including one case where two domains
  (`rs-noma.lv` and `tukumapiekabes.lv`) turned out to be the same Tukums business —
  same phone, same address, same site content.

### Domains found dead and deliberately excluded
These were candidates that failed verification and are **not** in the file. Worth keeping as a
do-not-re-add list: `tuserviss.lv`, `evisltd.lv`, `gollner.lv`, `telgaclean.lv`, `nnvt.lv`,
`tws.lv`, `lbt-port.com`, `tipark.lv`, `addstorage.lv`, `fff-motors.lv`, `kaucminde.eu`,
`libava.com` (expired cert), `armel.lv` (cert mismatch), `darent.lv` (cert serves `afax.lv`),
`sodasstrukla.lv`, `kronova.lv`, `lattrans.lv`, `rodents.lv`, `gammasp.lv`.
Two special cases: **`freibos.com` now 301-redirects to an unrelated site
(`goodmorningscience.com`)**, and **ConStorage / Conway Self-Storage (Ganību dambis 27K-3,
Rīga) has a deactivated storefront** — the page now belongs to the Stora SaaS platform.
Both would have imported as plausible-looking rot.

---

## 2. Counts per city and per service

| City | Rows | warehouse | moving | trailer | cleaning | packing | vanrental | insurance | site | phone | email | aggr |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Rīga | 92 | 61 | 23 | 6 | 4 | 14 | 7 | 7 | 91 | 92 | 67 | 1 |
| Liepāja | 15 | 5 | 3 | 4 | 4 | 1 | – | – | 13 | 15 | 10 | 2 |
| Daugavpils | 10 | 4 | 2 | 2 | 4 | – | 1 | – | 7 | 10 | 6 | 3 |
| Jelgava | 10 | 2 | 2 | 1 | 3 | 2 | 2 | – | 6 | 10 | 6 | 4 |
| Ogre | 7 | 2 | 1 | – | 2 | 1 | 2 | – | 3 | 7 | 1 | 4 |
| Jūrmala | 7 | 4 | 3 | 1 | 1 | – | – | – | 5 | 7 | 5 | 2 |
| Rēzekne | 6 | 3 | 1 | 1 | 1 | – | – | – | 2 | 6 | 2 | 4 |
| Ventspils | 6 | 2 | 2 | 1 | 1 | – | – | – | 4 | 5 | 3 | 2 |
| Valmiera | 5 | – | 2 | 2 | 1 | 1 | – | – | 4 | 5 | 2 | 1 |
| Mārupe | 4 | 2 | 3 | 1 | – | 2 | – | – | 3 | 4 | 3 | 1 |
| Jēkabpils | 3 | 1 | 1 | 1 | – | – | – | – | 1 | 3 | 1 | 2 |
| Cēsis | 3 | 1 | – | 1 | 1 | – | – | – | 2 | 3 | 2 | 1 |
| Sigulda | 3 | – | – | 3 | – | – | – | – | 3 | 3 | 2 | 0 |
| Salaspils | 3 | 2 | 1 | 1 | – | 1 | – | – | 2 | 3 | 0 | 1 |
| Tukums | 3 | – | 1 | 2 | 1 | – | 1 | – | 2 | 3 | 1 | 1 |
| Dreiliņi | 1 | 1 | – | – | – | – | – | – | 1 | 1 | 1 | 0 |
| Ķekava | 1 | – | – | – | 1 | – | – | – | 1 | 1 | 1 | 0 |
| Iecava | 1 | – | – | – | 1 | – | – | – | 1 | 1 | 1 | 0 |
| Aizkraukle | 1 | – | – | 1 | – | – | – | – | 1 | 1 | 1 | 0 |
| Ragana | 1 | – | – | 1 | – | – | – | – | 1 | 1 | 1 | 0 |
| **TOTAL** | **182** | **90** | **45** | **29** | **25** | **22** | **13** | **7** | **153** | **181** | **116** | **29** |

Rows carry multiple `serviceTypes`, so the service columns sum to more than the row count.
`Mārupe`, `Ķekava`, `Iecava`, `Aizkraukle`, `Ragana` and `Dreiliņi` were not on the target
list but are Rīga-region or fill genuine gaps; they can be folded into a catchment or dropped.

Rīga district coverage (all eight requested districts are present): Centrs, Purvciems,
Ķengarags, Imanta, Zolitūde, Mežciems/Dreiliņi, Teika, Āgenskalns, plus Pļavnieki,
Ziepniekkalns, Iļģuciems, Čiekurkalns, VEF, Klusais centrs, Krasta and Berģi.

---

## 3. Which of the seven services are *not* real standalone markets

This is the most decision-relevant finding. Counting how often a service is a row's **only**
service separates real categories from bundled attributes:

| Service | Rows carrying it | Rows where it is the ONLY service | Verdict |
|---|---|---|---|
| warehouse | 90 | 69 | Real, deep, the strongest category |
| cleaning | 25 | 25 | Real and fully standalone |
| trailer | 29 | 24 | Real, but see the caveat below |
| moving | 45 | 17 | Real; usually sold with packing/storage attached |
| vanrental | 13 | 7 | Real but thin and Rīga-concentrated |
| insurance | 7 | 5 | Real but B2B-only — wrong customer |
| **packing** | **22** | **2** | **Not a market. Do not offer as a category.** |

### packing — do not ship this category in Latvia
Of 22 rows that offer packing, only **2** sell anything packing-shaped on its own, and both
are *materials retailers*, not service firms. Every single search for
"iepakošanas pakalpojumi" / "mantu pakošana" resolved to a moving company. What actually
exists is two disconnected things:

1. **Packing as a line item inside a move** — Moving.lv (APM Expres), Zebra Cargo, CargoRiga,
   FF International Movers, CVN. CargoRiga's pitch is "15 types of packing material" as a
   *reason to book the move*, not as a product.
2. **Cardboard-box retail**, which is B2B e-commerce packaging and unrelated to moving —
   PakoShop/MARSS, Multipack, Antalis. Only **PaperSeal** keeps a "Pārvākšanās kastes"
   category, and that is one product category, not a business.

**Recommendation: model packing as an attribute/add-on of a moving supplier, not as its own
service category.** Shipping a "packing" filter in LV would return a list of movers, which
looks broken.

### insurance — real, but it is not your customer's product
There *is* a genuine layer of FKTK-registered brokers selling `kravu apdrošināšana` / CMR as a
discrete named product with its own page: ROOT, Agento, R&D apdrošināšanas brokeri,
VIS Brokerhouse, Apdrošināšanas un Finanšu Brokers. Two hard qualifications:

- **All of them are in Rīga.** There is no regional cargo-insurance market at all.
- **What they sell is CMR carrier liability and commercial freight cover for hauliers and
  forwarders** — not goods-in-transit cover for a household moving its belongings. No one
  sells consumer moving insurance as a product; movers instead self-declare "full insurance"
  as bundled reassurance.

So insurance is a **supplier-side B2B category**, not a demand-side one. Useful for vetting
movers ("are you actually covered?"), not as something to quote a family that is moving.

### trailer — real, but the geography belongs to fuel stations
There is a genuine standalone layer (KABI — SIA KABI RENT, app-unlock, ~21 cities; plus
committed independents KRS Noma, RS Noma, JE Noma, Autonoma Valmiera, MEKS). But **outside the
big cities, the fuel station is effectively the only option**: VIADA lists 91 stations with
trailers, Virši 50+, DEPO says "all stores". Note Virši has **zero rows** in this file — their
trailer page lists 50+ station *names* with no addresses, and their station-finder returned a
cookie wall, so branch addresses were not invented.

### cleaning — real and nationwide, but websiteless outside Rīga
Easily the healthiest of the secondary categories. Published per-m² pricing is standard, and
`ģenerāltīrīšana` / `uzkopšana pēc remonta` are named SKUs on nearly every site. One notable
find: **SPODRE 7 lists "uzkopšana pirms un pēc pārvākšanās"** (pre- and post-move cleaning) as
an explicit service line — the exact adjacency Ruumly wants. Caveat: outside Rīga the market is
real but has no web presence; regional cleaners live in zl.lv/1188.lv listings and Facebook,
which is why most non-Rīga cleaning rows are `sourceQuality: "aggregator"`.

---

## 4. Cities that turned out to have essentially nothing

- **Jēkabpils** — genuinely empty. One customs warehouse and one haulier. **Zero moving
  services**: the pilseta24.lv Jēkabpils moving category returns only Rīga companies.
- **Sigulda** — zero warehouse, zero moving. Only trailer rental, which is genuinely healthy
  (3 providers). Sigulda is a trailer town and nothing else.
- **Cēsis** — one warehouse company (no website) plus a KABI trailer point. No mover based in
  Cēsis; the nearest is 40 km away in Jaunpiebalga.
- **Valmiera** — **no warehouse operator at all.** Two real movers with working sites.
- **Ventspils** — has genuine port/SEZ logistics but **zero self-storage**. Every consumer
  storage brand (Boxin, NOLIKTAVA1, SAFE BOX, MyStorage, BOX STORAGE) stops at
  Rīga/Jūrmala/Liepāja.
- **Tukums** — thin. Vehicle/trailer rental plus one cleaning firm. Tukums "noliktavas"
  listings are shop-warehouses (Baltic Agro, DEPO-style wholesalers), not rentable storage.
- **Jūrmala** — no local warehousing sector; the only real storage is NOLIKTAVA1's container
  yard at Slokas iela 45. Its other rows are small freight forwarders registered in flats.
- **Daugavpils moving is a hole worth knowing about.** It has the region's best warehouse
  supply, but every "Daugavpils moving service" in the directories is a Rīga company
  advertising into the city. The only local movers found sit behind Flagma's login wall with
  no retrievable phone, so they were not recorded.

### Structural finding for the concierge match loop
**There are no locally-registered household moving firms in Liepāja, Jelgava, Jūrmala,
Cēsis or Jēkabpils.** Every mover serving those cities (Komanda24, Pārcelšanās24, D.C.S.,
CVN, Kravas taksometrs, CargoRiga) is Rīga-based and drives out. So outside Rīga the match is
a **Rīga-supplier + local van/trailer/storage hybrid**, not a local-mover market. That changes
what "supplier match rate" means for a Liepāja request and should be reflected in ops.

---

## 5. Incumbents and competitors we ran into

- **mantuglabātuve.lv** — the one that matters. A **self-storage comparison portal**, i.e. a
  direct competitor to Ruumly's warehouse vertical, already occupying the compare-and-choose
  position. It claims "12 self-storage companies, 49+ locations in Riga, from €3/week" and
  publishes a per-brand price table. Anything Ruumly builds for LV storage lands next to this.
- **zl.lv** — the backbone directory for services. Important caveat: every moving-related
  category subdomain (`parcelsanas-serviss`, `parvaksanas-serviss`, `kraveju-pakalpojumi`,
  `mebelu-parvesana`, `biroju-parvesana`) returns essentially the **same ~15 firms in the same
  order**. The directory long tail is an illusion.
- **GetaPro.lv** — the real price pressure. Mostly **individual sole traders**, not companies,
  listing at €20–50/h. This is the bottom of the moving market and it is cheaper than every
  incorporated mover.
- **ss.com / ss.lv and mm.lv** — high classified volume but they strip company names and
  phones from listing pages, so they are poor sourcing material and poor competitors.
- **1188.lv, viss.lv, pilseta24.lv, firmas.lv, lursoft.lv** — conventional directories.
  pilseta24.lv is the most useful regionally because it has real city subdomains.
- **Commercial-property portals** — domimaps.lv, rentinriga.lv, telpu-noma.lv, Colliers,
  PICHE. These own "noliktavu noma" in the B2B sense and are a different market from
  consumer self-storage.
- **Crawler note:** vervo.lv, vedam.lv, komanda24.lv and sixt.lv return 403/CAPTCHA to
  server-side fetches. A re-verification job will need a real browser for those.

---

## 6. Price points observed (for quoting)

**Self-storage, Rīga** (verified on operators' own sites)
| Size | Weekly | Monthly |
|---|---|---|
| Locker 1–1.5 m³ | €4.24–5.25 | €10–24.99 |
| Small 2–3.5 m² | €7.14–9.51 | €30–59 |
| Medium 5–7.5 m² | €13.44–18.27 | €72–102 |
| Large 10–15 m² | – | €102–160 |
| XL 30 m² / 72 m³ | €43.75 | €195+ |

Acquisition hooks are aggressive: first month €1 (SAFE BOX), 50% off two months
(SELF STORAGE), 12-month contract discounts (NOLIKTAVA1).
Regional storage is *more* expensive than Rīga: Boxin Liepāja €36.30/mo for 1–2 m²,
€150 for 9–11 m². Daugavpils self-storage from €45/mo.
Commercial warehouse space in Daugavpils runs **€1.50–2.63 /m²/month**.

**Moving, Rīga** — pricing is unusually transparent and clusters tightly on hourly rates:
- Kravu Mednieki, APM Expres: from **€35/h**
- NESTunVEST: €35/h van only, €44/h van + 1 mover, €53/h van + 2
- ProMove: €45/h standard, €65/h comfort
- Komanda24: €50/h van + 1, €60/h van + 2; €30/h movers without a vehicle
- CVN: €60/h for 2 movers + van; parcelies.lv from €50
- Gerkors advertises €10–15/h transport, far below everyone else
- GetaPro sole traders: €20–50/h
- **Whole-job anchors: ~€120 for a 1–2 room Rīga flat, ~€300 for a mid-size office.**

⚠️ **The headline hourly rate is not the quote.** FF's second brand quotes €29/h per
specialist **plus a €60 call-out fee, 3-hour minimum, +21% VAT, and +35% after 18:00.**
Any quoting model built off advertised hourly rates will underquote badly.

**Trailers** — the public anchor is brutally low. KABI: €5 first hour, €3/h after,
**€18/day cap**. RS Noma €18 (1-axle) / €28 (2-axle) / €35 (5 m). AMELA1 Liepāja €12/day,
€17/24h. Autonoma Valmiera €15, €25 for a car transporter. Higher end: PATA €75/day for a
4.8 m covered trailer. **Typical: €15–25/day; €30–50 for a big platform.** Deposits €30–100.

**Cargo vans** — €45–70/day for a Sprinter/Crafter class. Bus4rent €60 cargo / €70 crew /
€80 refrigerated. Alvi Car Rent €45–50. DEPPO €20–120 across the fleet. Van supply is the
genuinely scarce, quotable side of the vehicle segment — Jelgava and Ogre were the only
non-Rīga van renters that could be verified.

**Cleaning** — regular 1.5 €/m², deep 2.5 €/m², general 3.5 €/m², **post-renovation 7 €/m²**.
Flat rates: 1-room flat from €50, 2-room €60, 3-room €70. Window washing €6–10/window.
Kitchen deep clean €180, bathroom €115. Liepāja: regular from €50, post-renovation from €70.

**Boxes/materials** — €0.25–5.93 per carton ex-factory, ~€1–3 typical retail.

---

## 7. Latvia vs Estonia — headline observations

1. **Latvia is Rīga plus a long, thin tail.** 92 of 182 rows are in Rīga, and the
   concentration is understated by the row count — most regional "coverage" is a Rīga company
   driving out. Estonia's Tallinn/Tartu split has no real analogue here; Daugavpils is the
   second city by population but has almost no moving sector.
2. **Self-storage is more consolidated than it looks.** The market advertises ~12 brands and
   49 locations, but several brands publish *identical street addresses* — Jūrkalnes iela 6
   is claimed by NOLIKTAVA1, Mantu Depo **and** Boxrent; Krustabaznīcas iela 16 by NOLIKTAVA1
   and Mantu Depo. Either these are white-label brands over a shared estate, or operators
   sublet inside the same complexes. Exact-address collisions were dropped, keeping the
   best-verified operator per address. **Treat brand count as an overestimate of real
   operators**, and expect some suppliers to answer for several brands.
3. **The format differs.** Rīga self-storage skews to container yards and drive-up units
   ("piebraukšana pie pašas glabātuves") where Tallinn skews indoor. Heated indoor space is
   sold explicitly at a premium.
4. **Russian is not optional.** For Rīga, Daugavpils and Rēzekne a large part of the market
   advertises primarily in Russian. Several operators' Russian pages carry contact details
   their Latvian pages omit. Ruumly's LV surface needs RU as a first-class language, not a
   translation afterthought — which fits the existing 5-language EmailTranslations setup.
5. **Registry discipline is weak.** Only 52 of 182 publish a reģ. nr. on their own site.
   Notably, most self-storage brands (Boxrent, BOX STORAGE, SAFE BOX, Mantu Depo, MyStorage,
   Next Storage, Kladovka) publish none at all — so supplier vetting in LV cannot rely on
   scraping a registry code from the website and will need a Lursoft/UR lookup step.
6. **Price transparency is higher than Estonia.** Latvian movers publish hourly rates openly
   and self-storage publishes full size/price tables. Good for automated quoting; bad for
   margin, because the customer can already compare — and mantuglabātuve.lv is already
   comparing for them.

---

## 8. Known gaps / suggested follow-ups

- The session's web-search budget (200 calls) was exhausted partway through. **Ventspils
  (Free Port terminal operators), Rēzekne, Salaspils and Tukums would each likely yield
  2–4 more rows** with fresh search budget.
- Four Rīga self-storage brands appear in the comparison portal's table but could not be
  reached on their own sites before the budget ran out: **EASYSTORAGE, Ask Storage, KEEPP,
  Demo Noliktava**. They were left out rather than guessed — worth a short follow-up pass.
- **Virši** (50+ trailer stations) is missing entirely because they publish no branch
  addresses. If they matter, the addresses need to come from Virši directly.
- Two Salaspils warehouses (Vollers-Rīga, Kuehne+Nagel) were dropped because their addresses
  are rural named properties ("Lindes", "Rudeņi 2") that could not be honestly geocoded.
- `sourceQuality: "aggregator"` on 29 rows means the contact came from a directory, not the
  business's own site. **Those 29 are the ones most likely to be stale — call them first.**
  All 29 have `websiteUrl: null` by construction.
