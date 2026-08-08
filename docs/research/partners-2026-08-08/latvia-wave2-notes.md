# Latvia provider research — wave 2 (2026-08-08)

Companion to `latvia-wave2.json` (**159 new rows**). This is an *additive* pass on top of
`latvia.json` (182 rows). Every row here was checked against all 182 existing rows by slug,
by normalised company name and by normalised street address before being kept.

**Combined LV directory after import: 341 rows across 60 cities.**

---

## 1. Headline result

| | wave 1 (`latvia.json`) | wave 2 (this file) | combined |
|---|---|---|---|
| Rows | 182 | **159** | 341 |
| Cities | 20 | **51** | 60 |
| Cities *new* to the directory | – | **35** | – |
| Rows outside Rīga | 90 | **139** | 229 |

Wave 1 was Rīga plus a thin tail (92 of 182 rows in Rīga). Wave 2 deliberately inverts that:
**139 of 159 rows are outside Rīga**, and 35 towns that had zero coverage now have some.

---

## 2. Counts per city and per service

Rows can carry more than one service, so service columns sum to more than the row count.
`site` = has a `websiteUrl` that was fetched successfully **and** independently re-verified
(DNS + TLS GET) at merge. `aggr` = `sourceQuality: "aggregator"`.

| City | Rows | warehouse | moving | trailer | cleaning | vanrental | site | phone | email | regcode | aggr |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Rīga | 20 | 5 | 1 | – | 15 | – | 7 | 20 | 4 | 4 | 13 |
| Mārupe | 11 | 6 | – | – | 6 | – | 5 | 11 | 5 | 7 | 6 |
| Kuldīga | 8 | 1 | 4 | 2 | – | 2 | 2 | 8 | 2 | 0 | 6 |
| Daugavpils | 8 | 6 | – | 1 | 1 | – | 1 | 7 | 0 | 3 | 7 |
| Salaspils | 7 | 4 | – | – | 3 | – | 2 | 7 | 1 | 5 | 6 |
| Ventspils | 7 | – | 1 | 2 | 3 | 1 | 3 | 7 | 3 | 1 | 4 |
| Madona | 5 | – | 2 | 3 | – | – | 4 | 5 | 2 | 0 | 2 |
| Jūrmala | 5 | – | – | – | 5 | – | 0 | 5 | 0 | 0 | 5 |
| Jēkabpils | 5 | – | 1 | 4 | – | 1 | 4 | 4 | 1 | 0 | 3 |
| Liepāja | 5 | 2 | 1 | – | 3 | – | 1 | 5 | 1 | 2 | 4 |
| Ķekava | 5 | 1 | – | 1 | 3 | – | 4 | 5 | 3 | 4 | 1 |
| Saldus | 5 | – | 3 | – | 1 | 1 | 0 | 5 | 0 | 0 | 5 |
| Dobele | 4 | – | 3 | 1 | – | – | 1 | 4 | 0 | 0 | 3 |
| Grobiņa | 4 | – | 2 | 1 | 1 | – | 2 | 4 | 2 | 1 | 2 |
| Alūksne | 4 | – | 2 | 2 | – | – | 2 | 4 | 1 | 0 | 3 |
| Ādaži | 4 | 1 | – | 2 | 1 | – | 2 | 4 | 1 | 2 | 2 |
| Olaine | 3 | 1 | – | – | 2 | – | 0 | 3 | 0 | 3 | 3 |
| Rēzekne | 3 | – | 2 | 1 | – | – | 1 | 3 | 0 | 0 | 3 |
| Talsi | 3 | – | 2 | 1 | – | 1 | 1 | 3 | 0 | 0 | 2 |
| Krāslava | 3 | – | 2 | 1 | – | – | 1 | 3 | 1 | 0 | 2 |
| Ludza | 2 | – | 1 | 1 | – | – | 1 | 2 | 1 | 1 | 1 |
| Preiļi | 2 | – | 2 | – | – | – | 0 | 2 | 0 | 0 | 2 |
| Limbaži | 2 | 1 | – | 1 | – | – | 2 | 2 | 2 | 1 | 0 |
| Viļāni | 2 | – | 1 | 1 | – | – | 1 | 2 | 1 | 0 | 1 |
| Gulbene | 2 | – | – | 2 | – | – | 2 | 2 | 1 | 0 | 1 |
| Aizkraukle | 2 | – | 1 | 1 | – | – | 1 | 2 | 1 | 0 | 1 |
| Carnikava | 2 | – | – | – | 2 | – | 0 | 2 | 0 | 2 | 2 |
| Bauska | 2 | – | – | 1 | 1 | – | 2 | 2 | 2 | 0 | 0 |
| Balvi | 2 | – | – | 2 | – | – | 2 | 2 | 1 | 0 | 1 |
| Tukums | 1 | – | – | 1 | – | – | 1 | 1 | 1 | 0 | 0 |
| Stopiņi | 1 | 1 | 1 | – | – | – | 1 | 1 | 1 | 1 | 0 |
| Skrunda | 1 | – | 1 | – | – | – | 0 | 1 | 0 | 0 | 1 |
| Saulkrasti | 1 | – | – | – | 1 | – | 0 | 1 | 0 | 1 | 1 |
| Aizpute | 1 | – | – | – | 1 | – | 0 | 1 | 0 | 1 | 1 |
| Babīte | 1 | – | – | 1 | – | – | 1 | 1 | 0 | 0 | 0 |
| Salacgrīva | 1 | – | – | 1 | – | – | 1 | 1 | 1 | 0 | 0 |
| Ropaži | 1 | 1 | – | – | – | – | 0 | 1 | 0 | 1 | 1 |
| Roja | 1 | – | 1 | – | – | – | 0 | 1 | 0 | 0 | 1 |
| Ragana | 1 | – | – | 1 | – | – | 1 | 1 | 0 | 0 | 0 |
| Baloži | 1 | – | – | – | 1 | – | 0 | 1 | 0 | 1 | 1 |
| Kārsava | 1 | – | – | 1 | – | – | 1 | 1 | 1 | 0 | 0 |
| Priekule | 1 | – | – | – | 1 | – | 0 | 1 | 0 | 1 | 1 |
| Ogre | 1 | – | – | 1 | – | – | 1 | 1 | 1 | 0 | 0 |
| Mērsrags | 1 | – | 1 | – | – | – | 0 | 1 | 0 | 0 | 1 |
| Malta | 1 | – | – | – | 1 | – | 0 | 1 | 0 | 0 | 1 |
| Cēsis | 1 | – | – | 1 | – | – | 1 | 1 | 1 | 0 | 0 |
| Dreiliņi | 1 | 1 | – | – | – | – | 1 | 1 | 0 | 0 | 1 |
| Līvāni | 1 | – | – | 1 | – | – | 1 | 1 | 1 | 1 | 0 |
| Dundaga | 1 | – | – | 1 | – | – | 1 | 1 | 1 | 0 | 0 |
| Baldone | 1 | – | – | – | 1 | – | 0 | 1 | 0 | 0 | 1 |
| Jaunmārupe | 1 | – | – | – | 1 | – | 0 | 1 | 0 | 1 | 1 |
| **TOTAL** | **159** | **31** | **35** | **40** | **54** | **6** | **65** | **157** | **44** | **44** | **103** |

### Service split, and how standalone each service is

| Service | Rows carrying it | Rows where it is the ONLY service |
|---|---|---|
| cleaning | 54 | 53 |
| trailer | 40 | 38 |
| moving | 35 | 31 |
| warehouse | 31 | 27 |
| vanrental | 6 | 3 |
| **packing** | **0** | **0** |

`packing` was **not collected as a standalone business**, per instruction. No row in this file
carries it even as a secondary tag — nothing encountered sold packing in a way that made the
tag meaningful on its own. `insurance` was likewise not collected.

**35 new cities** (no rows at all in `latvia.json`): Ādaži, Aizpute, Alūksne, Babīte, Baldone,
Baloži, Balvi, Bauska, Carnikava, Dobele, Dundaga, Grobiņa, Gulbene, Jaunmārupe, Kārsava,
Krāslava, Kuldīga, Limbaži, Līvāni, Ludza, Madona, Malta, Mērsrags, Olaine, Preiļi, Priekule,
Roja, Ropaži, Salacgrīva, Saldus, Saulkrasti, Skrunda, Stopiņi, Talsi, Viļāni.

---

## 3. Verification standard applied

- **Every `websiteUrl` was fetched successfully during research, then independently
  re-verified at merge** with a DNS lookup plus a TLS-verified GET from a separate HTTP
  client. Result: **30 distinct domains, 0 dead, 0 DNS failures, 0 certificate errors.**
  Two (`powerwash.lv`, `rewico.lv`) return 429 to repeat automated requests but resolve and
  were fetched successfully in-session — they are alive, not rot.
- **Nothing was pattern-completed.** No phone, email or registry code appears unless it was
  read verbatim off a fetched page. That is why `contactEmail` is only 44/159 and
  `registryCode` only 44/159 — those are real gaps, not lazy ones.
- **Phone shape enforced mechanically**: `+371` + 8 digits, first digit 2 / 6 / 7 / 80.
  Anything else was nulled rather than reshaped. 157/159 rows carry a phone.
- **Coordinates: 159 of 159 are distinct**, and every one was produced by a real geocoder
  (OpenStreetMap via Nominatim, then Photon after Nominatim began rate-limiting), not
  estimated and never a repeated city-centre point. Each result was validated against the
  geocoder's returned street / house number / postcode before acceptance.
- **A geographic cross-check** was run over the whole file: every row's coordinate was
  compared to an independently geocoded centroid of its stated city. All 159 sit within
  25 km of their city. (One apparent outlier, `latlog-stopini`, was investigated and is
  correct — the bare place name "Stopiņi" geocodes to unrelated hamlets in Kurzeme; the
  row's own Getliņu iela 7A / Rumbula coordinate is right.)
- **Duplicate control:** 0 collisions against `latvia.json` and 0 within this file, on slug,
  normalised name and normalised address. Candidates that collided were dropped, not renamed.

### Repeated phone numbers — checked, all legitimate
Three phone numbers repeat across rows. All are chain switchboards, matching the convention
already used in `latvia.json`: KABI RENT `+371 20393905` (7 rows), VIADA Baltija
`+371 80000208` (21 rows), VIATEK 1 `+371 67221604` (2 rows). No shell-company pattern
survived into the file (see §5 for one that was caught and removed).

---

## 4. The four unverified Rīga self-storage brands — resolved

Wave 1 flagged four brands it could not reach. All four are now settled:

| Brand | Verdict |
|---|---|
| **EASYSTORAGE** | **Real, included.** Prūšu iela 46, Rīga (Mežciems). Own site fetched, phone published. |
| **Ask Storage** | **Real, included.** Uriekstes iela 3, Rīga (Sarkandaugava). Trading since 2013, own site fetched. |
| **KEEPP** | **Real, included.** Ganību dambis 19, Rīga. Own site, phone and email; also runs cargo transport from €30/trip, so tagged `warehouse` + `moving`. |
| **Demo Noliktava** | **NOT A BUSINESS — excluded.** It is a placeholder record inside the mantuglabātuve.lv directory. Its profile says it is "a demonstration profile used to check the owner cabinet", and its published phone is `+371 20000000`. Its three "locations" (Brīvības 100, Dzelzavas 50, Kurzemes prospekts 5) are fictitious. **Importing it would have created three fake Rīga pins and a fake phone number.** |

A second EASYSTORAGE location (Aptiekas iela 3, Sarkandaugava) is claimed by the comparison
portal but **does not appear on EASYSTORAGE's own site**, which lists one location only.
It was **dropped** rather than shipped on a single aggregator's word.

Likewise **Easy Box**: the portal claims 3 locations, but easybox.lv itself publishes only
Bauskas iela 33 — already in `latvia.json`. No new rows taken. One of the portal's claimed
Easy Box addresses is Prūšu iela 46, which is EASYSTORAGE's address — good evidence the
portal conflates operators, and a reason to trust operator sites over it.

---

## 5. Businesses excluded, and why

**Fake / placeholder**
- **Demo Noliktava** (3 Rīga "locations", phone `+371 20000000`) — directory test record.

**Same operator behind multiple shells (would have stacked pins on one real business)**
- **AcmeLightBaltic, KapnesD, RRServiss, Uzkopts.lv** (Daugavpils) — all four share
  Enerģētiķu šķērsiela 6-13 *and* phone `+371 28924020` with **CleanGuard**, which is already
  in `latvia.json`. Four rejected, one existing row is the truth.
- **AutoRentRiga** — identical address and phone to Alvi Car Rent (Zolitūdes 71B), already
  covered. **Alpik Cargo** — same address as PIK. **NORDO SIA** — Noliktavu iela 5, Dreiliņi,
  the address already held by 3PL Services.

**Near-duplicate addresses, dropped to avoid a second pin on one business**
- **Belmast**, Višķu iela 21**Z**, vs existing LatLiga at Višķu iela 21**Ž**.
- **Pārvadājumi24** at Dzirnieku 16, Mārupe — same brand as the existing Rīga row.

**Wrong category after checking (the biggest single source of rejections)**
- Trailer searches in Pierīga are heavily mis-categorised: **Baltictex** (clothing wholesale),
  **Kalnakrogs / DOJUS / Agrotrac** (agricultural machinery), **Alwark** (forklifts),
  **"Dzīvojamo treileru nomas birojs"** (caravans), **Oversize Transportation Services**
  (35 t+ abnormal loads), **Krone ScanBalt** (semi-trailer dealer).
- **MINT Textile Management** and the Madona/Gulbene/Sigulda `ķīmiskā tīrītava` entries —
  garment dry-cleaners and drop-off points, not premises cleaning.
- **Rīgas Slotas** and **Antigraffiti** — landscaping/groundskeeping and graffiti removal.
  Real firms, but not move-relevant cleaning. Rīgas Slotas also publishes a registered
  address in Balvu novads that contradicts its Rīga directory listing.
- **AJ Produkti** (shelving retail), **WSP** (shelving supplier), **Storent** (equipment
  rental), **Linas Agro "Iecavas bāze"** (grain elevator), **Konteineru serviss** (waste
  containers), **SwiftClean** (arborist), **Voka** (grain/metal fabrication),
  **Domos serviss** (water filters), **SPL Project** (metal fabrication),
  **Vincent RA** (windows), **Zemgales namu apsaimniekotājs** (building administration),
  **AKM Trans** / **BB Skudras** (car-carrier and forestry haulage, not household moving),
  **Mimoza** (listed under "Auto noma" in pilseta24 — actually a florist).
- **VM autonoma, Aidava, EVIS LTD rent, RentalPark** — passenger cars only; tagging them
  `vanrental` would have been a false claim.

**Could not be honestly geocoded — dropped rather than guessed**
- **VIADA DUS Preiļi** (Rīgas iela 8) — would not resolve to a building; the fallback landed
  on a school on a different street, so the row was dropped.
- **AMD transports** (Talsi) — "Vīksnes iela 5" silently matched *Talsciema iela 5*. Caught by
  post-geocode street validation and dropped.
- **Taka Termināls** ("Lubānas šoseja 9. km"), **Dalars** (Ludza), **APETrans** (Aizkraukle),
  and the Pāvilosta candidates (rural named properties only).
- **Balodis Cargo** (Engure) — real listing but no phone and no website anywhere; a
  contactless pin is not useful to the concierge loop.

**Dead domains confirmed this session** — add to the wave-1 do-not-re-add list:
`nomatpiekabes.lv` (DNS does not resolve). Wave 1's `evisltd.lv` and `darent.lv` were
re-confirmed dead.

**Chains deliberately left out**
- **Virši** (50+ trailer stations) is still absent, for the same reason as wave 1: the trailer
  page lists station *names* only and the station finder sits behind a cookie wall. Station
  addresses were not invented. Two Virši points in Jēkabpils *were* recorded because that
  city's own directory published their street addresses.
- **DEPO** trailer rental says "available at all stores" but the service page publishes no
  store addresses; only the Jēkabpils store was recorded, from a source that gave its address.

---

## 6. Cities: genuinely empty vs. merely unsearched

**Answered directly, because wave 1 asked these three:**

- **Jēkabpils — NOT empty. Wave 1 was wrong.** It now has 5 rows, including the strongest
  regional find in this pass: **SIA MARTEKS NOMA** (Brīvības iela 2E-1), a genuine
  multi-category rental firm — economy cars, passenger *and cargo* vans, trailers and car
  transporters, with its own live site and email. Plus two Virši trailer points and a DEPO
  store. Wave 1's "nothing at all" was a search-method artifact: Latvian regional directories
  are indexed poorly by US-facing web search but respond well to direct URL fetches.
- **Sigulda — confirmed genuinely thin, wave 1 was right.** Re-checked this pass: the only
  Siguldas-novads cleaning entry is a MINT dry-cleaning *drop-off point*, not a cleaner.
  Sigulda remains a trailer town.
- **Valmiera — confirmed, wave 1 was right.** Re-checked: the moving category for Valmiera
  returns Vestnest (already recorded) plus three Rīga companies advertising into the city.
  No new local operator, and still no warehouse.
- **Tukums — still thin.** One new row (a VIADA trailer point). The other candidates were a
  Rīga firm listing into Tukums and a waste-container company.

**Genuinely empty after searching (nothing honest to record):**
- **Cesvaine** — town of ~1,000; no provider in any of the five categories in any directory.
- **Iecava** — only a grain elevator, an oversize haulier and a pool-servicing firm.
- **Auce** — nothing in any category in the Dobele-region directory.
- **Ulbroka proper** — the Ropažu-novads industrial rows all sit in Rumbula / Dreiliņi /
  Saurieši, not Ulbroka itself.
- **Piņķi / Babīte** — no clean row survived; the warehouse hits were a shelving supplier and
  a car dealer. (The one Babīte row in this file is a KABI trailer point at Spilve.)
- **Engure, Pāvilosta** — one contactless listing and rural-named-property addresses
  respectively.

**Structurally empty categories, believed real rather than a search gap:**
- **No consumer self-storage brand exists anywhere in Latgale.** NOLIKTAVA1, Boxrent,
  BOX STORAGE, Mantu Depo and SAFE BOX all stop at Rīga/Jūrmala/Liepāja. Latgale's storage is
  industrial and customs warehousing only — that is what the 6 warehouse rows there are.
- **Trailer and van rental in Pierīga are entirely chain-operated** (KABI, VIADA, Virši, DEPO).
  Independent providers essentially do not exist in the Rīga commuter belt.
- **Ventspils still has no rentable consumer storage operator** — confirming wave 1. Its
  warehouse listings remain bulk/port terminals.

**Thin because budget ran out, not because they are empty** — worth a wave 3:
Balvi, Gulbene, Ludza (freight SIAs exist but at rural named properties), Talsi and Kuldīga
warehouse, Ventspils trailer, Dobele cleaning, Smiltene (never reached), and the
Jūrmala/Tukums/Sigulda/Valmiera/Cēsis/Ogre cluster, which was only partially covered
(see §8).

---

## 7. Notable finds

- **SIA MARTEKS NOMA** (Jēkabpils) — the multi-category rental firm described above.
- **SIA VIATEK 1** — publishes a **third warehouse in Limbaži** (Dzegužu iela 1) alongside its
  two Rīga sites. Northern Vidzeme has almost no warehouse supply, so this matters.
- **Vollers Riga** ("Lindes") and **Kuehne+Nagel** ("Rudeņi 2"), the two Salaspils warehouses
  wave 1 abandoned as un-geocodable, are both **recovered and cleanly geocoded**.
- **19 local movers in Kurzeme** (Kuldīga, Saldus, Dobele, Talsi, Grobiņa, Roja, Mērsrags,
  Skrunda). This **partially contradicts wave 1's structural claim** that there are no locally
  registered household movers outside Rīga. It is true for Liepāja and Jelgava; it is not true
  for inland Kurzeme, where small local movers do exist — they are just invisible to search
  engines and only appear in regional directories.
- **Jūrmala is now covered as distinct points**, as requested: Majori (Jomas iela),
  Kauguri (Skolas iela), Dubulti/Dzintari (Trikātas iela), Melluži (Kāpu iela) and Lielupe
  (29. līnija) all have separate coordinates spread across the resort's full 25 km.

---

## 8. Budget and coverage caveats — read before trusting the gaps

- **The session WebSearch budget (200/200) was exhausted** partway through, across the whole
  session. Everything after that point was done with WebFetch against directly constructed
  directory URLs, which worked well. The highest-yield sources, for reuse in a wave 3:
  - `https://meklesanas-rezultats.zl.lv/<query+with+plus>/` — name + full address + phone
  - `https://<category>.zl.lv/<City or Region>` — the single best regional source
  - `https://<city>.pilseta24.lv/uznemumi?pro=<category>` — real regional long tail, with
    per-company detail pages carrying reg. nr. and email
- **Nominatim began returning HTTP 429** partway through. A wall of `NO_RESULT` from it is the
  rate limit, not missing data. **Photon** (`photon.komoot.io`, coordinates returned as
  `[lon, lat]`) carried the rest of the geocoding and tolerates parallel requests.
- **The northern-Vidzeme / Jūrmala / Tukums cluster is only partially covered.** The research
  pass assigned to Jūrmala, Tukums, Sigulda, Limbaži, Smiltene, Valmiera, Cēsis, Ogre,
  Ikšķile, Lielvārde and Salacgrīva did not deliver its file before the session ended. What
  those cities do have here came from the chain layer (VIADA/KABI/VIATEK) plus a direct
  top-up pass on Jūrmala cleaning, Valmiera moving and Sigulda cleaning. **Smiltene, Ikšķile
  and Lielvārde were never searched at all** and should not be read as empty.
  A wave 3 should start there.
- `sourceQuality: "aggregator"` on 103 of 159 rows means the contact came from a directory,
  not the business's own site. **Those are the ones most likely to be stale — call them
  first.** The 56 `official` rows had their contacts read off the operator's own site.
