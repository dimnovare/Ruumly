# Lithuania provider research — wave 2

Companion to `lithuania-wave2.json`. Research date **2026-08-08**. This is the **second** pass;
it deduplicates against `lithuania.json` (93 rows) and contains **new providers only**.

**204 new rows.** 106 official-sourced (contacts read on the company's own site or its own
Facebook page), 98 aggregator-sourced (`"sourceQuality": "aggregator"`).

Combined LT dataset after import: **297 rows**.

---

## 1. Counts

### Per city (wave 2 = this file)

| City | Wave 2 | warehouse | moving | trailer | cleaning | vanrental | Wave 1 | Total |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| Vilnius | 46 | 3 | 0 | 0 | 43 | 0 | 55 | 101 |
| Klaipėda | 27 | 6 | 7 | 3 | 8 | 4 | 3 | 30 |
| Panevėžys | 23 | 5 | 6 | 4 | 8 | 2 | 2 | 25 |
| Šiauliai | 22 | 2 | 7 | 5 | 7 | 1 | 1 | 23 |
| Kaunas | 19 | 2 | 0 | 0 | 17 | 0 | 29 | 48 |
| Alytus | 11 | 3 | 2 | 3 | 3 | 2 | 0 | 11 |
| Mažeikiai | 4 | 1 | 1 | 2 | 1 | 0 | 0 | 4 |
| Marijampolė | 3 | 1 | 0 | 0 | 2 | 0 | 0 | 3 |
| Utena | 3 | 0 | 1 | 1 | 1 | 0 | 0 | 3 |
| Klaipėdos r. | 3 | 1 | 0 | 2 | 0 | 1 | 1 | 4 |
| Telšiai | 3 | 0 | 0 | 1 | 2 | 0 | 0 | 3 |
| Tauragė | 3 | 1 | 0 | 0 | 2 | 0 | 0 | 3 |
| Kėdainiai | 2 | 0 | 1 | 0 | 1 | 0 | 0 | 2 |
| Druskininkai | 2 | 0 | 0 | 1 | 1 | 0 | 0 | 2 |
| Radviliškis | 2 | 0 | 1 | 1 | 0 | 0 | 0 | 2 |
| Lentvaris | 2 | 1 | 0 | 0 | 1 | 0 | 0 | 2 |
| Joniškis | 2 | 0 | 0 | 2 | 0 | 0 | 0 | 2 |
| Pabradė | 2 | 0 | 1 | 0 | 2 | 0 | 0 | 2 |
| Gargždai | 2 | 0 | 0 | 1 | 1 | 0 | 0 | 2 |
| Plungė | 2 | 0 | 0 | 0 | 1 | 1 | 0 | 2 |
| Kretinga | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 2 |
| Šilutė | 2 | 0 | 1 | 0 | 1 | 0 | 0 | 2 |
| Palanga | 2 | 0 | 0 | 0 | 2 | 0 | 0 | 2 |
| Jonava | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 1 |
| Ukmergė | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 1 |
| Biržai | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1 |
| Rokiškis | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1 |
| Garliava | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1 |
| Raseiniai | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 1 |
| Jurbarkas | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 1 |
| Ignalina | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1 |
| Šakiai | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 1 |
| Seirijai (Lazdijų r.) | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 1 |
| Pasvalys | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 1 |
| Salamiestis (Kupiškio r.) | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 1 |
| Vilkaviškis | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1 |
| Vilniaus r. | 1 | 0 | 0 | 0 | 1 | 0 | 1 | 2 |
| Kauno r. | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 1 |
| **Total** | **204** | **27** | **31** | **31** | **112** | **11** | **93** | **297** |

### Per service

| Service | Wave 2 rows |
|---|--:|
| cleaning | 112 |
| moving | 31 |
| trailer | 31 |
| warehouse | 27 |
| vanrental | 11 |
| *packing (extra tag only)* | *1* |

`packing` appears on exactly one row (`kraustymas-eu-klaipeda`), always alongside `moving`,
never as a sole service. No `insurance` rows — the category was not collected.

### Field completeness

| Field | Filled |
|---|--:|
| address | 204 / 204 |
| lat/lng | 204 / 204 |
| contactPhone | 202 / 204 |
| websiteUrl (fetched OK this session) | 117 / 204 |
| contactEmail | 105 / 204 |
| registryCode | 42 / 204 |

Only 2 rows have neither phone nor email (`drakono-garas-salamiestis-kupiskis`,
`palangos-svara-palanga`).

---

## 2. The regional gap is closed; Vilnius/Kaunas were topped up in one category only

Wave 1 was Vilnius 55 / Kaunas 29 against Klaipėda 3, Panevėžys 2, Šiauliai 1. Wave 2 inverts
that: **Klaipėda +27, Panevėžys +23, Šiauliai +22, Alytus +11**, and 30 further towns now have
at least one provider. The four big regional cities now look like real markets rather than
rounding errors.

Vilnius and Kaunas additions are **almost entirely `cleaning` (43 and 17)**, deliberately.
Wave 1 already covered their moving and storage supply well, but only recorded 7 Vilnius and 6
Kaunas cleaners against a national directory category holding 62 in Vilnius and 35 in Kaunas
alone. That was the single largest untapped seam in the two big cities, so it is the one that
was mined. No new Vilnius/Kaunas movers or storage operators were added except five genuine
warehousing firms and one storage branch — the rest of those directory pages were already in
wave 1 or failed verification.

---

## 3. The trailer-rental theory — CONFIRMED, and now converted into rows

Wave 1 hypothesised that every small-city trailer-rental domain being unregistered meant
trailer rental is a **side business of petrol stations and garages**, not a dedicated trade.
Four independent researchers tested this. **It held, in every region.** `trailer` went from
6 rows to 31, and the great majority are sidelines with no trailer-specific website:

**Petrol-station chains renting trailers from the forecourt:**
- **Saurida** — Šiauliai, Vilniaus g. 373A and Pramonės g. 7C
- **Jozita, UAB** (code 176618114) — Lithuanian-owned, 26 filling stations; rents at Architektų g. 80
  and Girulių g. 1 in Šiauliai, S. Kerbedžio g. 7F in Panevėžys, and in Klaipėda; 05:00–23:00
- **ALAUŠA, UAB** — Utena chain; its own site states it rents trailers at *almost all* stations
  (500 kg, open or canvas, driving licence required)
- **Vildega** — Vilkaviškis fuel retailer (see caveat below)

**Garages, tool-hire shops and builders' merchants:**
- **Baltrevis / Regimanto autoservisas** — Šiauliai garage, trailers from €10/day
- **UAB Grinuoma** — Gargždai tool and scaffolding hire since 2008; trailers are one line in the catalogue
- **UAB Sienuva** — Mažeikiai builders' hire; one Tauras b708s trailer, plus 6 m storage containers
- **UAB Enima** — Mažeikiai garage / roadside assistance
- **UAB Margynė** — Radviliškis tool hire; trailers listed beside perforators
- **Gedita, IĮ** — Rokiškis builders' merchant with a tool-rental arm
- **ŽVAKĖ, B. Senkevič įmonė** — Druskininkai roadside assistance
- **V. Undzėno įmonė** — Biržai; cars, minibuses and trailers
- **Autoros, UAB** (Ignalina), **Autobitės / UAB "Narbona"** (Joniškis), **M. Špokausko, MB**
  (Satkūnai) — garages and a general rental yard

**The one real counter-example:** `priekabu-nuoma-panevezys.lt` (Pušaloto g. 188, Panevėžys) is a
live, priced, dedicated trailer-rental operator — the first live dedicated small-city trailer
domain found in either wave. **Taurata** (Rainiai, Telšių r., `priekabostau.lt`) is a second.
So the dedicated model does survive in the larger regional cities; below ~50k population it does not.

**Two caveats to carry into any calling script.**
1. **Vildega (Vilkaviškis)** — the trailer sideline is attested only by an info.lt keyword tag;
   the company's own live site describes fuel retail, customs brokerage and a gravel quarry with
   no mention of trailers. Row kept, marked `aggregator`, and the doubt is written into its
   description. Ask before assuming.
2. **Directory trailer tags lie constantly.** Businesses that directories advertised as
   `priekabų nuoma` but whose own sites contradict it, all rejected: A. Arys ir Ko (tool
   *retailer*), Kretingos smagratis (Bosch garage), Jomamotors, S. Laurinavičiaus IĮ
   (timber haulage), Tomtrak (towing + power tools), BALTMETAS (lends trailers only so customers
   can *bring scrap in*), Bjoras (agricultural machinery repair), Hermio priekabos
   (manufacture/repair/sales, "nuoma" appears nowhere), Daugstumda, Savas tralas LT, Klatentas.
   **Read the company's own page before trusting a trailer tag.**

**Not recorded, but real supply:** Baltic Petroleum and Viada both rent trailers nationally from
their forecourts, and Hovus runs 60+ pickup points. None publishes a fetchable per-station
address list (403 to our fetcher), so no rows were invented for them. That is probably 100+
additional trailer pickup points available by partnership rather than by crawling.

---

## 4. The minibus / cargo-van trap

Wave 1 warned that `mikroautobusų nuoma` in Lithuania usually means **passenger** minibus hire.
Every researcher was briefed on it and every one hit it. `vanrental` was only tagged after a
cargo vehicle was seen on the fleet page:

- **Accepted** — DonAuto (Renault Master L3H2 confirmed on the fleet page; its homepage's
  "krovininių mikroautobusų nuoma" claim alone was *not* accepted), TopNuoma (Ford Transit
  9.3 m³, Fiat Ducato 15 m³ and 17 m³), Befora and Alrodis (Sprinter / Transit / Master).
- **Rejected** — EURORENTA / UAB Autoblikas (8–20-seat minibuses and 25–50-seat coaches only),
  SMARTUKAS (passenger cars and limousines), Jomamotors (Opel Vivaro 9-seat, Renault Trafic
  8-seat, zero cargo), Naika ir Ko and R. Adomavičienės (licensed passenger carriers), BUSAUTA,
  ZIMBRAVOS/skybus.lt, and all three Visaginas renters (Tiko automobiliai, 7sky, Romirlita).

Net result: only **11 `vanrental` rows** across the whole country. That is the honest size of
the cargo-van rental market as distinct from bus hire, and it argues for treating `vanrental` as
a thin category in LT.

---

## 5. Cities genuinely empty vs merely unsearched

**Genuinely empty — searched properly, nothing exists:**
- **Elektrėnai** — probed movers, trailer, warehousing and cleaning. Every directory redirected
  to Kaišiadorys/Vilnius/Trakai. The only Elektrėnai-addressed hit was a veterinary clinic.
- **Visaginas beyond SEKNIJA** (already in wave 1) — movers resolve to Ignalina, storage hits are
  a shed *seller*, vehicle rental is passenger-only. SEKNIJA really is the town's one provider.
- **Gargždai** has no moving company and no warehousing — directories return literally
  "Įmonių nerasta". Its two rows are all that exists.
- **Tauragė** has no moving company of its own; the nearest are Šilalė and Jurbarkas.
- **Šilutė and Palanga have zero trailer rental** — every apparent hit is physically in Klaipėda
  or Klaipėdos r.
- **Grigiškės** returns nothing under any category; it is absorbed into Vilnius city listings.
- Nothing found, properly searched: Trakai town, Anykščiai, Prienai, Varėna, Zarasai, Kupiškis,
  Molėtai, Širvintos, Naujoji Akmenė, Nemenčinė.

**Structurally absent nationwide:** **self-storage / sandėliukų nuoma does not exist outside
Vilnius, Kaunas, Klaipėda and Alytus.** Every "sandėliukų nuoma" search in the eight western
towns resolved back to the big three. Regional `warehouse` supply is B2B industrial space only
(e.g. UAB Talga, Gaurės g. 32, Tauragė, ~24,000 m², lets from 1,000 m²). The single genuinely new
consumer self-storage location found in the regions is **Boxrent Klaipėda, Svajonės g. 17** —
the chain's only site outside the two biggest cities — plus **Daiktams.lt Alytus, Naujoji g. 3**,
which wave 1 flagged as a warm lead and this pass converted.

**Thin but real (1–3 verified providers, matching town size):** Marijampolė (thinner than its
35k population suggests — no trailer rental with a city address, no self-storage), Jonava (27k
with zero locally-addressed movers or cleaners; Kaunas 30 km away absorbs the demand), Rokiškis,
Biržai, Ukmergė, Kėdainiai, Plungė.

**Merely under-searched — worth a third pass:** Vievis, Birštonas, Kazlų Rūda, Domeikava,
Skuodas and Pakruojis (cleaning), Šalčininkai (cleaning), Lentvaris/Trakai (trailer), and
Grigiškės + Nemenčinė via Vilniaus r. sources rather than town-scoped ones.

---

## 6. Businesses excluded, and why

**Category rules.** Racking/forklift/equipment sellers mis-filed under "warehousing":
NIRLITA, ALWARK, BALTEXIM, KROVIMO TECHNIKA, AKI SPRENDIMAI, LAIDRA, PRENETA, KONECRANES,
Cosma Metal, TRUCKMAINT, VILDIKA, Montvega, SANDALA, STOKKER, MONO KRAVU LIFTS, RENTEKSA
(hangars), VERTONAS, LOGVITUS (cranes), BAKRA (used forklifts), FROSTERA (refrigeration
equipment), PARADIS (lifts and loading equipment), Transrifus (scaffolding and site containers),
Vekstrus (sells prefab sheds), Ramirent and Storent (construction equipment), SINC/RENTALIS
(tool hire), ADOPTO.

**Not the business the directory says.** CITVA, VILNIAUS CITMA and PANEVĖŽIO CITMA are
fruit-and-vegetable wholesalers, not storage-for-hire. DAILA is a construction company.
AGROEKSPEDICIJA is bulk haulage with no storage. Powermotors is a garden-machinery parts
retailer tagged as moving. Vioveta is a veterinary clinic filed under cleaning. Resmila Vilnius
is a Kaunas sandblasting shop. Baltjet and Ocean cargo services could not be shown to rent space
rather than sell kit.

**Dry cleaning is not premises cleaning.** LINARTIKA (`valyklaklaipeda.lt`), SKAISTUVA and its
Kėdainiai branch, ELASTIKAS, JOGLĖ — all `valykla` laundry businesses, excluded. Municipal
waste and territory contractors likewise (UAB Valrem does industrial tank cleaning only).

**Couriers and freight forwarders are not movers.** FedEx Express (the *only* entry in
visalietuva's Klaipėda removals category), Ainetra, Viprotekas (customs brokerage), Avibusas
(long-haul truck tractors), UAB Sponsa and UAB Patrauka (construction dump trucks).

**Dropped for no usable street address** (would have produced a dishonest map pin):
`vezukraustau.lt`, `kraustom123.lt`, Kraustomobilis, Ornic MB, SOSVAN, ŠVARI PRADŽIA (Plungė),
Ponas švara (Veisiejai), Gintarinė švara (Kupiškis), LANGŲ VALYMAS, LITPROFIL and NOMUS
(Vilnius — geocoder could not confirm the street), plus Rasrama's Klaipėda branch (Šilutės pl. 79
has no house-number entry and interpolating one was declined).

**Dropped as phantom branches.** **UAB Sivita** was listed with a distinct street address in
eight towns; its own site shows a single Klaipėda HQ serving "Klaipėda and its surroundings".
Only the Klaipėda row was kept; the Raseiniai and Kelmė rows were removed at merge, and six more
were never emitted. Same reasoning killed the whole **VALYMO KOMANDA** regional chain
(six branches behind a domain that does not resolve) — only its Vilnius head office survives,
website-less and marked `aggregator`.

**Conflicting evidence, dropped rather than guessed:** UAB Sutura (a directory advertises priced
pallet storage, the company's own site says sewing), BŪSTKAITA (filed under removals by two
directories, serves cleaning content with a third phone), Eldesa (its listed site resolves to a
Klaipėda company and its address duplicated an already-covered row), Talinta (listed as Alytus
with a Utena postcode), Inrija (listed as Jonava with an Ignalina postcode), MB Aisidos švara,
ŠVARUS LANGAS, Eivintra. **BMCO group** was dropped from the Vilnius cleaning set: its site is
branded `turiuzala.lt` and sells insurance-claim damage restoration, so the identity could not be
pinned to the directory listing.

---

## 7. Data-quality findings

**Dead domains found this session** (DNS does not resolve — all were listed as live company
websites by Lithuanian directories):
`cleanexperts.lt` (confirms wave 1), `idealisvara.lt`, `perfektas.lt`, `rizolta.lt`,
`valymokomanda.lt`, `vvpaslaugos.lt`, `kristolinissaltinis.com`, `nibvalymas.lt`,
`svaridiena.lt`, `sandeliukaiklaipedoje.lt`, `besandelio.lt`, `azigroup.lt`, `btlogistika.lt`,
`valranda.lt`, `artikasvalymopaslaugos.lt`, `dulkius.lt`, `svarostarnyba.lt`, `rasrama.lt`,
`koyarelo.lt`, `rekota.lt`, `etvarka.lt`, `magista.lt`, `litvala.com`,
`mvgrouplogistics.lt` (connection refused).

**HTTP 200 but not a business** — parking pages, placeholders and errors:
`pedante.lt` ("Svetainė neegzistuoja"), `valyk.lt` ("Užregistruotas domenas — Interneto vizija"),
`irmas.lt` (parking.domenai.lt), `rcc.lt` (redirects to a domain-for-sale page), `realservice.lt`
("Serverių nuoma" hosting placeholder), `nikeja.lt` (empty body behind a wildcard cert),
`danrasta.lt` (WordPress fatal error, no content), `kpb.lt` and `svarosdeive.lt` (empty).
**Status 200 is not proof of a business — the body must be read.** This pass detected them by
fetching every candidate domain and pattern-matching the body, not by trusting the status code.

**Unverifiable, so no URL recorded** (site may well be fine): 403 bot-walls on `ancitra.lt`,
`blizginta.lt`, `deimena.lt`, `skanerlita.lt`, `valymodiena.lt`, `paslaugos.lt`,
`sandeliukunuoma.lt`, `transnest.lt`; TLS certificate mismatches on `ekovala.lt`,
`jurasta.lt`, `nomus.eu`, and `sienuva.lt`; unsupported TLS on `busauto.lt`.
**`sienuva.lt` is recorded as `http://` because its HTTPS cert is `*.hostingas.lt` and fails in
normal clients** — if the importer forces HTTPS, that one row's website will break.

**Dead-link rate.** Of the domains probed this session, roughly **1 in 6** was dead or a parking
page — worse than wave 1's 1 in 8. Re-verify before import and periodically after.

**Shared registry codes across branches** (the importer's unique index would reject the second):
handled at merge by keeping the code on one row and nulling the rest — Greenas (Vilnius/Klaipėda),
Radvydė (Kaunas/Klaipėda), Jozita (Šiauliai ×2, Panevėžys, Klaipėda), Talga (Jonava/Tauragė/Alytus),
Valstapas (3 Kaunas rows), Top Clean (Vilnius/Kaunas), Ainava (4 cities), Vitaresta,
Daiktams.lt Alytus (code already used by wave 1 rows).

> **Pre-existing defect worth fixing before import:** `lithuania.json` itself already carries
> registry code **303083043 on three rows** (`daiktams-lt-vilnius-vilkpede`,
> `daiktams-lt-vilnius-fabijoniskes`, `daiktams-lt-kaunas`). That file will fail a unique index
> on `registryCode` on its own, independently of this wave. Null two of the three.

**Geocoding was verified, not trusted.** Every coordinate was geocoded per street address via
Photon and cross-checked against the postcode the company publishes; a validator then rejected
any pin outside Lithuania and flagged any pin more than 8–12 km from its own town centre.
Confirmed wrong answers that were caught and fixed: `Vilniaus g. 31, Vilnius` resolved to
`Didžioji g. 31`; `Jano Bulhako g. 6` resolved to Buivydiškės (row dropped); a Sivita Raseiniai
latitude of 54.38 would have landed ~110 km south; Naujoji g. 3 in Alytus returns postcode 62119
against the 63246 daiktams.lt prints (house-number geocode used, worst case ~0.5 km within the
same district).

**Three pins sit far from their nominal town centre and are correct, not errors:**
Ginmeksa (Bugeniai village, 8.3 km from Mažeikiai), Marinesa (Mokyklos g. in **Šventoji**, 11 km
up the coast but administratively Palanga city municipality, postcode 00303), and Drakono garas
(Salamiestis, Kupiškio r.).

**Four pairs of rows legitimately share a coordinate** because the businesses are in the same
building — Kalvarijų g. 125 Vilnius (Euroservis Plius + Švaros desantas), Neries krantinė 16
Kaunas (Eurovalymas + LogiKor), Savanorių pr. 66 Kaunas (Valstapas + Švari diena), and
Laisvės pr. 60 Vilnius (Švaros akcentas, which shares a building with wave 1's UAB Maniteka).
These are real, not duplicates, but they will overlap on a map at high zoom.

**Multi-domain SEO operators — dedupe by address, not domain.** `kraustomeklaipeda.com` shares a
phone with `kraustymas.eu`; `vezukraustau.lt` shares one with `kroviniupervezimasklaipeda.lt`;
`tvarkaubiurus.lt` now serves the `svarosekspresas.lt` brand; `valymopaslaugos.lt` serves
Vitaresta. Each operator is represented at most once.

**Five city values fall outside the target city list**, deliberately, because the businesses
really are outside the city boundary: `Klaipėdos r.`, `Vilniaus r.`, `Kauno r.`, plus the
settlements `Seirijai` (Lazdijų r.), `Salamiestis` (Kupiškio r.), `Pabradė` (Švenčionių r.),
`Lentvaris`, `Garliava`. They are recorded under the real settlement so the map pins stay honest;
remapping them to the parent district town is an import-time decision, not a re-research.

---

## 8. Search budget

**The 200-call session WebSearch quota was exhausted**, as in wave 1 — three of the five
researchers hit the cap before finishing. Everything after that point was done by fetching
directory category pages and probing candidate domains directly, which works well for firms with
a web presence and badly for one-van operators who exist only on Facebook.

What that means for the numbers: the **regional cities are now genuinely well covered**, and the
towns marked "genuinely empty" above were searched properly before the cap. The list under
"merely under-searched" is where the remaining supply is.

A method note for the next pass: `visalietuva.lt` city-scoped category URLs are fetchable and
paginated, and the **English** variants
(`/en/companies/premises-cleaning-services/vilniuje/2`) return **postcodes** alongside addresses,
which makes geocode cross-checking far more reliable than the Lithuanian pages. The most
productive category slugs are `patalpu-valymo-paslaugos`, `sandeliavimo-paslaugos-ir-iranga` and
`perkraustymo-paslaugos`. `rekvizitai.vz.lt` remained blocked to our fetcher in both waves.
