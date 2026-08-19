# 2026-08-19 — what the supplier directory actually contains

1,187 rows. 952 businesses. 728 of those can be emailed. The rest is a public
website.

This is an offline audit of a full production dump of the admin supplier DTO,
taken 2026-08-19, cross-checked against the code that reads those rows
(`ProviderCandidateFinder`, `ConciergeOutreachService`, `SitemapController`) and
against the per-service city snapshots captured from `GET /api/locations/cities`
on 2026-08-17.

## What could not be measured, and why

Three of the questions worth asking cannot be answered from this dump. Saying so
first, because a number quoted from the wrong column is worse than no number.

- **Opt-outs.** `Supplier.MarketingOptOutAt` is not in `SupplierDto`. The dump
  therefore cannot count them. At least one exists — the Kaunas cleaning company
  that replied REMOVE on 2026-08-13. Every opt-out is invisible to this audit and
  every count below is an over-count by that amount. `ProviderCandidateFinder`
  filters them at source (`s.IsActive && s.MarketingOptOutAt == null`), so they
  are already excluded from live fan-out; they are only missing from *this*
  arithmetic.
- **Addresses and coordinates.** They live on `SupplierLocation`, which the
  supplier DTO does not carry. "Coordinates that do not match the stated city"
  is not checkable offline. Neither is "how much supply is inside Harjumaa",
  which is the only geography the ops loop currently runs in.
- **Bounces.** `ContactEmailUnusable` is `false` and `ContactEmailBouncedAt` is
  null on all 1,187 rows. That is not evidence of health. The Resend webhook was
  only configured on 2026-08-18, so nothing has ever been able to set those
  columns. The first bulk send after that date is the first real reading.

Where a city appears below it was **reconstructed from the slug**, not read from
the location row. The method and its error rate are in §4.

## 1. Who can actually be contacted

| | rows | active | no email | contactable |
|---|---:|---:|---:|---:|
| EE | 247 | 244 | 8 | 237 |
| LV | 336 | 336 | 90 | 246 |
| LT | 604 | 599 | 132 | 470 |
| **total** | **1,187** | **1,179** | **230** | **953** |

"Contactable" = active, has an address, address is syntactically valid, not
flagged unusable. All 957 addresses present are syntactically valid — there are
no malformed ones to fix.

The 230 no-email rows are 19% of the directory and are almost entirely a Baltic
import problem: **3.2% of Estonian rows lack an address, against 26.8% of Latvian
and 21.9% of Lithuanian rows.** 204 of the 230 came in on 2026-08-09 alone.

Of the 230:

- 226 have a phone number. 4 do not (All OverSeas SIA, Drakono garas IĮ, Palik,
  T49 Kolimisteenus).
- 33 have a website, so an address is probably recoverable by hand.
- **197 have neither an email nor a website — a phone number and nothing else.**
  181 of those still carry a tagline and a full public partner page.

Three further reductions on the 953:

- **181 rows (19% of those with an address) sit on a free-mail inbox** — 140 on
  gmail.com, 20 on inbox.lv, the rest scattered. LT 102, LV 41, EE 38. These are
  reachable but they are a person's mailbox, not a business one, and they are the
  rows most likely to read a cold quote request as spam.
- **3 rows sit on a domain with no MX record**: Merva OÜ (`erki@dotbox.ee`),
  Rinoceras UAB (`info@rcc.lt`), Valytė MB (`valyte@valyte.lt`). Confirmed
  against the 2026-08-18 DNS pass: 562 of 565 domains resolve. Dead domains are
  not this directory's problem.
- One address contradicts its own country: **Box Storage** is `Country=EE` with a
  `+372` phone and `boxstorage.ee` as its website, but its contact address is
  `peteris.gulans@boxstorage.lv`. `Supplier.Country` picks the outreach language,
  so an Estonian letter goes to what looks like a Latvian owner. It is the only
  such row in 1,187 — country and phone dialling code agree everywhere else.

### Contactable rows by service and country

| service | EE rows / contactable | LV rows / contactable | LT rows / contactable |
|---|---|---|---|
| warehouse | 52 / 49 | 121 / 93 | 106 / 93 |
| moving | 68 / 62 | 80 / 49 | 112 / 69 |
| trailer | 43 / 43 | 69 / 68 | 116 / 106 |
| cleaning | 40 / 39 | 79 / 47 | 277 / 211 |
| vanrental | 30 / 30 | 19 / 19 | 25 / 23 |
| packing¹ | 16 / 15 | 22 / 19 | 26 / 25 |
| insurance¹ | 11 / 11 | — | — |

¹ retained-not-sold; never offered in intake, search or sitemap.

The damage is concentrated: **moving loses 39% of its Latvian rows and 38% of its
Lithuanian rows** to missing addresses, and cleaning loses 41% of Latvian rows.
Trailer barely loses anything, because trailer supply is petrol-station chains
with a head-office inbox (§5).

## 2. How many distinct businesses

**952**, on the following rule, applied in order: corporate email domain →
free-mail inbox → website host → normalised brand name + country. A free-mail
address identifies one business, not a domain full of them.

- 898 businesses are a single row.
- 54 businesses hold the remaining 289 rows.
- **235 rows are the second-or-later row of a business already in the directory.**
- 728 businesses have at least one contactable row. **224 have none.**

| | rows | businesses | contactable businesses |
|---|---:|---:|---:|
| EE | 247 | 222 | 213 |
| LV | 336 | 244 | 160 |
| LT | 604 | 487 | 356 |

**Registry code is useless for this.** 335 of 1,187 rows carry one and all 335 are
unique — there is not a single registry-code collision in the directory. The code
is absent from exactly the rows where the duplicates are, because the branch rows
imported in August never got one.

### Legitimate branches

These are one company with many real sites. The evidence that they are branches
and not redundancy is consistent across all of them: **many distinct city or
street tokens, one head-office inbox, one head-office phone.**

| rows | business | inboxes | distinct cities |
|---:|---|---:|---:|
| 35 | viada.lt | 1 | 33 |
| 27 | viadabaltija.lv | 1 | 25 |
| 25 | balticpetroleum.lt | 1 | 25 |
| 15 | kabi.lv | 1 | 13 |
| 13 | boxrent.lv | 1 | Rīga districts |
| 13 | noliktava1.lv | 1 | Rīga districts + Jūrmala |
| 10 | saurida.lt | 2 | 9 |
| 9 | boxrent.lt | 1 | 3 |
| 8 | ramirent.ee | 8 | 8 |
| 7 | mantudepo.lv | 0 | Rīga districts |
| 7 | virsi.lv | 1 | 6 |
| 6 | corpusa.lt | 1 | 6 |
| 5 | boxstorage.lv, miil.ee, safebox.lv | 5 / 2 / 1 | — |

`ConciergeOutreachService` already collapses these at fan-out: `seenEmails` is an
`OrdinalIgnoreCase` set of trimmed addresses, and a sibling row that loses the
race is skipped as `duplicate_email` rather than inheriting the slot. Viada's 35
rows cost one email per lead, which is correct.

### Where that dedupe does not hold

The dedupe key is the **inbox**, so a company that spreads its branches across
several addresses is contacted once per address. Five such clusters exist, proven
by a shared phone number:

| rows | inboxes | company | surplus emails per lead |
|---:|---:|---|---:|
| 4 | 2 | Blackline OÜ (`info@blackline.ee`, `info@konteinerladu.ee`) | 1 |
| 4 | 3 | UAB Daiktams.lt | 2 |
| 2 | 2 | Ants Viljandi OÜ | 1 |
| 2 | 2 | BOX STORAGE (Antenas / Daugavgrīvas) | 1 |
| 2 | 2 | UAB Transekspedicija / Transekspedicija Invest² | 1 |

² two separate legal entities sharing a switchboard; arguably correct as-is.

Six surplus letters per fully-fanned-out lead in the worst case. Small, but this
is exactly the failure the 2026-08-18 Viljandi note flagged as unconfirmed
("`06c6c92e` lists Lahe miniladu and Blackline twice each"). For Blackline the
mechanism is now identified. For "Lahe miniladu twice" there is a second
candidate explanation in §3.

### Genuinely redundant rows

Real duplicates are rarer than the spot checks suggested — 43 rows sit in a
same-business + same-city + same-service group, and most of those are branches
the name simply does not distinguish. The rows where redundancy is the better
reading:

| rows | evidence | verdict |
|---|---|---|
| Zebra Cargo SIA / Zebra Cargo, SIA (Friendly Movers) | same inbox, same city, identical three-service list, created one day apart, `zebra-cargo-marupe` and `-marupe-2` | near-certain duplicate |
| SIA DEPPO / SIA DEPPO — busu un vieglo auto noma | same inbox, same city, same service, `deppo-ogre` and `deppo-ogre-2` | near-certain duplicate |
| Jürgeni Kaubavedu OÜ ×2 | byte-identical name, one inbox, July row has reg 10192704, August row has none | duplicate unless Elva is a real second yard |
| KolimisExpress OÜ ×2 | byte-identical name, one inbox, same July/August pattern | same |
| Veoteenused24 ×2, Pereezd.ee ×2, Haagiseabi ×2, HEPA ×2 | same July/August pattern | needs a look each |
| Laverna Puhastustööd ×2 | already resolved — the August row is deactivated | done |
| Envio OÜ ×2 | already resolved — the July row is deactivated | done |

**The structural cause is visible.** The directory was loaded twice: 170 rows in
July 2026 (163 of them on the 9th, all Estonian) and 1,017 rows from 2026-08-08
onwards. **Nine inboxes appear in both cohorts**, and in every case the August row
is the same Estonian company re-entered with a city-suffixed slug. For Miil,
Blackline, Haagiseabi and HEPA the August row at least names a distinct site. For
Jürgeni Kaubavedu and KolimisExpress the name is byte-identical and nothing in the
row says otherwise.

The August import had no way to notice: it checks slug uniqueness only
(`AdminDirectoryController` — `batchSlugs` plus a `Suppliers.AnyAsync(s => s.Slug
== slug)`), and a city-suffixed slug is always unique.

## 3. Rows that are provably wrong

**One row is provably mis-attributed.** Slug `miniladu24-eu-viljandi` carries
name "Lahe miniladu – Koidu 13, Viljandi", website `lahekinnisvara.ee`, address
`viljandi@lahekinnisvara.ee`, registry code 14209956. It is the only row in 1,187
whose slug brand token appears in neither its name, its website host nor its email
domain — every other apparent mismatch in that test is an acronym company whose
slug is `<acronym>-<city>` (Eda UAB, KLZ SIA, SIA DTD, SIA TKS, If Kindlustus).
The slug names one business and the data names another.

There is a second row, `miniladu24` → "Miniladu 24/7" at `info@miniladu24.ee`,
which is a different company. **Two unrelated Viljandi-area storage rows whose
slugs both begin `miniladu24`** is a plausible reading of the earlier "Lahe
miniladu listed twice" observation, but the outreach rows are not in this dump so
that stays a hypothesis.

**Suspicious but not proven — email domain belongs to a different company than
the website.** 59 rows fail this test; 27 of them are VIADA Baltija, which
legitimately runs `viada.lv` as its site and `viadabaltija.lv` as its mail, so the
test's false-positive rate is high. The ones worth a human look are the ones with
the same shape as the Lahe row — a storage or rental brand whose inbox belongs to
a real-estate or unrelated firm:

| row | website | contact address |
|---|---|---|
| Boxibaas | boxibaas.ee | `…@arcovara.ee` |
| Tartu Minilaod | tartuminilaod.ee | `…@raar.ee` |
| Smuuli Laod | smuulilaod.com | `…@hiku.ee` |
| Haagisrent | haagisrent.ee | `…@estmetall.ee` |
| Kaubikuterent.ee | kaubikuterent.ee | `…@dragonetrent.ee` |
| Pärnu Autorent | parnuautorent.ee | `…@privalon.ee` |
| Espak Jõgeva | espak.ee | `…@valmeco.ee` |
| Konteinerladu OÜ | konteinerladu.eu | `rait@miil.ee` |

The last one is worth naming separately. There are **two unrelated "Konteinerladu"
brands** in the directory: `konteinerladu.ee`, which belongs to Blackline OÜ (two
rows, Keila and Rakvere), and `konteinerladu.eu` — "Konteinerladu OÜ" — whose
contact address is Miil OÜ's. Miil and Blackline are the two providers that were
emailed about Viljandi storage and did not answer. Either the `.eu` row's address
is wrong or one of these three companies owns more of this than the directory
says, and neither can be settled from the data.

**Weak signals, listed for completeness.** Eight rows name a service in their
company name that they do not declare (`AD REM TRANSPORT` → warehouse only,
`Ecobox OÜ` → packing only, `Autonoma Valmiera` → trailer only, and five more).
"Transport" in a Lithuanian company name does not mean household removals, so
this list is a prompt, not a finding.

**Content, not correctness.** 968 of the 970 rows with a long description have a
Russian text byte-identical to the English one. The Estonian text is distinct on
all of them. The RU column was never translated.

## 4. What the published coverage numbers become

First, a correction to how these get quoted. The numbers below are the output of
`GET /api/locations/cities?type=…`, captured 2026-08-17, and they are
**Baltic-wide, not Estonian**:

| service | cities | EE | LV | LT |
|---|---:|---:|---:|---:|
| trailer | 124 | 20 | 37 | 67 |
| cleaning | 80 | 13 | 28 | 39 |
| warehouse | 73 | 22 | 20 | 31 |
| moving | 64 | 19 | 29 | 16 |
| vanrental | 31 | 10 | 10 | 11 |

"124 trailer city hubs" is 20 Estonian ones. The Estonian trailer hub count is
one sixth of the figure the site advertises. No city slug collides across
countries, so the hub URL count equals the city count.

To recompute these against contactability, each row needs a city, which the dump
does not carry. Cities were reconstructed by matching a known city slug inside
`Supplier.Slug`. **974 of 1,187 rows resolved, and the reconstruction produced no
false positives on trailer, warehouse, moving or vanrental** — every rebuilt city
is a city the live endpoint also lists. It misses cities rather than inventing
them, and the misses are almost entirely Estonian, because Estonian slugs are
brand-only (`blackline`, `alevi-hoiuladu`) while the August Baltic import used
`brand-city`. Two known error modes: a street name mistaken for a city
(`viada-rezekne-daugavpils-iela` reads as Daugavpils), and a district read as its
municipality (`7-spalvos-seirijai-lazdijai`).

So the loss figures below are a **lower bound**. The cities the method could not
resolve are the Estonian ones, where email coverage is 97%, so the true loss is
unlikely to be much higher.

| service | hubs today | hubs with contactable supply only | lost |
|---|---:|---:|---:|
| trailer | 124 | ≤123 | 1 |
| cleaning | 80 | ≤60 | 20 |
| warehouse | 73 | ≤61 | 12 |
| moving | 64 | ≤48 | 16 |
| vanrental | 31 | ≤31 | 0 |

**Cleaning loses a quarter of its city hubs and moving loses a quarter of its
own.** The cities that go dark are: for cleaning — Ādaži, Baloži, Ignalina,
Jaunmārupe, Jurbarkas, Kaišiadorys, Lazdijai, Lentvaris, Malta, Molėtai, Olaine,
Pasvalys, Rēzekne, Salamiestis, Salaspils, Saulkrasti, Telšių r., Valmiera,
Vievis, Visaginas; for moving — Alūksne, Dobele, Kėdainiai, Krāslava, Ludza,
Mērsrags, Preiļi, Rēzekne, Roja, Salaspils, Saldus, Šilutė, Skrunda, Talsi, Türi,
Viļāni.

Separately, the sitemap emits `/partner/{slug}` for every active published row:
**1,179 rows × 5 languages = 5,895 URLs, of which 1,130 are for a business nobody
can contact.**

## 5. Is there enough supply to run the loop

Counting **contactable businesses**, not rows — the number that matters is how
many different companies can be asked to quote one job.

| service | EE | LV | LT |
|---|---:|---:|---:|
| warehouse | 43 | 56 | 76 |
| moving | 58 | 47 | 69 |
| trailer | 30 | 21 | 36 |
| cleaning | 39 | 47 | 184 |
| vanrental | 30 | 18 | 23 |

Blunt readings:

- **Estonia is fine everywhere, on paper.** 213 contactable businesses across
  five services, and the worst-covered service (trailer, 30) still has more
  companies than a 6-slot fan-out needs. Estonia is not a supply problem. It is a
  reply-rate problem — 18 Viljandi contacts, 0 answers, and that number is about
  the letter, not the list.
- **Trailer is thinner than 228 rows suggests.** Those rows are 97 businesses,
  and nine chains account for 135 of them (59%). Independently owned trailer
  yards number 28 in Estonia, 19 in Latvia (18 contactable) and 41 in Lithuania
  (32 contactable). A trailer request answered by Viada, Baltic Petroleum, KABI
  and Virši is four petrol-station head offices deciding whether a household
  hire is worth a reply. That is a real business model — the trailers exist and
  are hired by the hour — but it is a different sale to a different desk than a
  mover or a storage yard, and it deserves its own outreach copy before its
  silence gets read as a supply gap.
- **Latvian cleaning and Latvian/Lithuanian moving are the weak spots.** LV
  moving drops from 78 businesses to 47 on contactability; LV cleaning from 79 to
  47; LT moving from 108 to 69. Those are the three cells where the missing
  addresses actually cost coverage.
- **Lithuanian cleaning is the deepest pool in the directory** — 184 contactable
  businesses. It is also 63 active rows short of an address, the single biggest
  bucket of uncontactable rows.
- **Van rental is complete.** Contrary to the working assumption, Lithuania has
  25 van-rental rows, 23 of them contactable, across 23 distinct businesses —
  AutoVerus, Autobanga, Nuomis, TopNuoma and the rest, with taglines that
  explicitly say *krovininių mikroautobusų nuoma*. Van rental is the only service
  that loses no city hub at all. Whatever gap was believed to exist there is not
  in this data.

The honest summary: **there is enough contactable supply to run the concierge
loop in every service and every country except Latvian cleaning and Latvian
moving, where roughly half the directory is unreachable.** The binding constraint
today is not the size of the list.

## Recommendations, in order

### Safe to automate

1. **Stop publishing partner pages for rows nobody can contact.** 226 active rows
   with no address; 1,130 sitemap URLs. Flipping `IsPartnerPagePublished` to
   false on rows with an empty `ContactEmail` is mechanical, reversible, and stops
   the site advertising businesses it cannot broker. It does not touch
   `IsActive`, so the rows stay in the admin and stay importable-over. *(226 rows,
   founder's call, one bulk update.)*
2. **Publish coverage numbers per country, not Baltic-wide.** The sitemap and the
   cities endpoint already carry `Country`; the 124/80/73/64/31 figures do not.
   Anything that quotes "124 trailer hubs" as Estonian coverage is off by a factor
   of six. *(5 numbers, wherever they are quoted.)*
3. **Make the import refuse a row with no contact address, or mark it.** 204 of
   the 230 came in on one day. A required-field check, or an
   `IsDirectoryOnly`-style flag that keeps the row out of candidate results, stops
   this recurring on the next import. *(0 existing rows changed; prevents the
   next 200.)*
4. **Dedupe the fan-out by company, not by inbox.** `ConciergeOutreachService`
   already reserves addresses; adding the normalised phone number as a second key
   would collapse the five known multi-inbox companies. Six surplus letters per
   worst-case lead is small, but the fix is a second `HashSet` in code that
   already exists for the purpose. *(5 clusters, 14 rows.)*

### Needs a human to look

5. **The `miniladu24-eu-viljandi` row.** Provably mis-attributed, in the exact
   city where five customer requests went unanswered. Decide whether the row is
   Lahe Kinnisvara (rename the slug), miniladu24.eu (fix the data), or neither
   (deactivate). *(1 row.)*
6. **The Konteinerladu / Miil / Blackline tangle.** Three brands, two domains, one
   shared phone, and one row whose contact address belongs to a different
   company — all in the Viljandi storage cluster that produced zero replies.
   Worth twenty minutes before any conclusion is drawn about Viljandi supply.
   *(9 rows.)*
7. **The eight brand/inbox mismatches in §3.** Each is a storage or rental brand
   whose mail goes to an unrelated firm. Some will be a landlord's agent, which is
   fine and is who should get the mail; some will be the Lahe pattern. Only a
   person can tell them apart. *(8 rows.)*
8. **The four identical-name July/August pairs** — Jürgeni Kaubavedu,
   KolimisExpress, Veoteenused24, Pereezd.ee — plus Zebra Cargo and SIA DEPPO.
   Each is either a real second site or a duplicate; the row itself does not say.
   Deactivating the wrong one removes a real location. *(12 rows.)*
9. **Recover addresses for the 33 no-email rows that have a website.** Highest
   value per minute of any item here: they are already researched businesses with
   a live site, and Mantu Depo alone is seven Rīga storage locations reachable
   only by phone. *(33 rows, 7 of them one company.)*
10. **Decide what the remaining 197 phone-only rows are for.** They cannot be
    emailed, cannot be quoted through the platform, and cannot be reached by any
    automated path. Either they are a phone-outreach queue with someone assigned
    to it, or they are SEO inventory and should be labelled as such. Right now
    they are counted as supply. *(197 rows.)*

Nothing above proposes deleting a row. `IsActive = false` is the established
pattern and, per the two entries that already use it (Laverna, Envio), it works.

## Reproducing this

The dump and the analysis scripts are in the session scratchpad
(`suppliers-all.json`, `audit1`–`audit10`). Read the JSON with explicit UTF-8 —
the default Windows codec corrupts the Latvian and Lithuanian names, and three of
the findings above are name comparisons.
