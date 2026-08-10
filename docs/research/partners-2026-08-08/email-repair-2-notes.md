# Email repair — wave 2 (2026-08-09)

Companion to `email-repair-2.json`. Wave 1 (`email-repair.json`) is untouched; the two files
merge cleanly — **0 slug overlap**, 174 + 354 = 528 rows covered in total.

## Headline

| | rows |
|---|---|
| Targets in this wave | **354** |
| Resolved with an address | **176** |
| …of which MX-verified deliverable | **160** |
| …of which the domain cannot receive mail (reported, do **not** mail) | 16 |
| Confirmed defunct / struck off | **3** |
| No findable e-mail — phone-only supplier | **175** |

**Not reached: 175 of 354 (49%).** This wave is partial coverage, not completion. Do not read
"pass finished" as "every provider now has an address". After merging both waves the directory
still holds roughly **175 unreachable rows out of the original 384**, i.e. the no-email
population drops from 384 to about 208 (175 unreached here + 26 already-confirmed-unreachable
in wave 1 + the 7 remaining Estonian rows never in scope).

## Target list — how it was built

Rows with null/empty `contactEmail` across `payloads/import-LV.json`, `import-LV-wave2.json`,
`import-LT.json`, `import-LT-wave2.json`, `import-LT-wave3.json`, `import-EE.json`
= 528 unique slugs, minus the 174 slugs already in `email-repair.json` = **354 targets**
(LT 237, LV 117, EE 0 — Estonia was already finished in wave 1).

**Priority 1 in the brief — "rows that have a websiteUrl but no email" — was already
exhausted.** All 162 such rows in the payloads are in `email-repair.json`; that is what wave 1
spent itself on. Every one of the 354 rows here has **no website**, and only 38 carry a
`registryCode`. That changed the whole method: there was no cheap page to fetch, so the work
became *finding* each company in a national registry-backed directory first.

## Per country

| | LT | LV | total |
|---|---|---|---|
| Targets | 237 | 117 | 354 |
| Found | 133 | 43 | 176 |
| Defunct | 3 | 0 | 3 |
| No e-mail found | 101 | 74 | 175 |
| Hit rate | 56% | 37% | 50% |

Lithuania converts far better because VisaLietuva.lt republishes VĮ Registrų Centras data
*including* the registered e-mail. Latvia has no equivalent: ZL.LV and Firmas.lv publish a
phone and paywall the contact block, so LV depended on the much thinner Viss.lv advertiser
subset plus the companies' own websites.

## Where the addresses came from

| Source | rows |
|---|---|
| VisaLietuva.lt company records (Registrų Centras data) | 114 |
| The company's own website, discovered and phone-verified | 36 |
| Viss.lv (LV directory) | 17 |
| infolapa.zl.lv (ZL.LV company records) | 9 |

Search engines were unusable from this environment — DuckDuckGo, Bing, Brave, Startpage,
Ecosia, Yandex, Mojeek and every public SearXNG instance returned challenge pages, 403 or 429,
and rekvizitai.lt blocks the host outright. Everything above was reached by direct URL fetch,
which costs no search quota. `WebSearch` was used only 12 times, purely to *discover* which
directories exist; the WebSearch ceiling was never approached.

## Evidence standard — no address was guessed

Every address in the file was read off a page that was actually fetched, and its source URL is
recorded in `sources`. Identity was pinned before the address was accepted:

- **116 high confidence** — the directory record carries the *exact phone number* already in
  our row, or the *exact registration number*.
- **41 medium** — exact registered name plus matching locality, or the company's own national
  domain carrying the trading name.
- **19 low** — flagged individually in `notes`: dead MX, a different registered city, or the
  phone line listed under a different trading name. Each says what to check before sending.

Two integrity passes were run and both are worth knowing about:

1. **Every one of the 176 addresses was re-fetched from its own source URL and confirmed
   present.** This caught a parser bug where two adjacent directory listings merged into one
   block and leaked the neighbour's address onto the wrong company — 5 rows affected, all
   corrected against the company's own profile page, 2 of them cleared back to
   `no_email_found`. Without that pass, five providers would have been mailed at a competitor's
   inbox.
2. **MX was checked on every domain** and cross-checked against Cloudflare DNS-over-HTTPS
   rather than trusting the local resolver alone.

One row (`ecs-eco-baltic-vilnius`) publishes `info@@ecsecobaltic.lt` — a malformed address with
a doubled `@`. It is recorded as `no_email_found` with the raw string in the note. The obvious
"correction" was deliberately **not** applied: guessing it is exactly the failure mode that
loses a customer request silently.

## The 16 addresses that must stay out of the campaign

Real, published addresses on domains with **no MX record** — mail to them bounces or vanishes.
They are in the file with `mxOk: false`, `confidence: low` and an explicit warning, so the
campaign can filter on `mxOk === true`.

Four of these domains are outright NXDOMAIN (`sandeliukaiklaipedoje.lt`, `magista.lt`,
`cleanexperts.lt`, `baltransgroup.lv`) — the business let the domain lapse, which usually means
the business itself is winding down. Worth reviewing those rows for deactivation.

`balt-trans-group-rezekne` is instructive: the company's own contact page prints
`info@baltransgroup.lv` (single "t") while the site lives on `balttransgroup.lv` (double "t").
It is a typo on their own website. `lenerts@inbox.lv` also appears in that page's source and
does have working MX — but the note records this rather than silently substituting it.

LT: blizgesio-namai-kaisiadorys, muilo-burbulas-kaunas, nib-valymas-kaunas, svari-diena-kaunas,
rapolita-klaipeda, sandeliukai-klaipedoje-klaipeda, blizginta-nimanta-siauliai, magista-telsiu-r,
clean-experts-vilnius, double-pro-vilnius, rasrama-vilnius, trailine-vilnius, tukompa-vilnius,
visos-valymo-paslaugos-vilnius, m-consulting-group-visaginas.
LV: balt-trans-group-rezekne.

## Defunct — deactivate these rows

Struck off or in liquidation per VĮ Registrų Centras (via VisaLietuva.lt):

| slug | company | status |
|---|---|---|
| `svaros-pajegos-kaunas` | Švaros pajėgos, UAB | Išregistruotas (struck off) 2025-08-27 |
| `transpona-palepsio-panevezys` | Transpona, E. Palepšio įmonė | Išregistruotas 2025-07-11 |
| `rbvan-siauliai` | RBVAN, IĮ | Likviduojamas (in liquidation) 2026-02-11 |

Only Lithuania exposes this flag in the listing, so the true defunct count is certainly higher —
Latvia has no equivalent public marker on the free tier. Treat 3 as a floor, not a total.

## Which categories and cities gained

| Category | targets | resolved | deliverable |
|---|---|---|---|
| cleaning | 194 | 117 (60%) | 105 |
| moving | 96 | 31 (32%) | 28 |
| warehouse | 58 | 26 (45%) | 25 |
| vanrental | 5 | 4 | 4 |
| trailer | 5 | 1 | 1 |
| packing | 2 | 1 | 1 |

**The categories with real customer demand converted worst.** Movers resolved at 32% against
cleaners at 60%. That is not a prioritisation failure — moving and warehouse rows were worked
first and hardest. It is a structural fact: Baltic movers are overwhelmingly one-van MB/IĮ
sole traders who advertise a mobile number and nothing else, while cleaning companies are more
often registered service businesses with a directory presence. Expect the same ratio in any
future wave.

Cities gaining most reachable supply: Vilnius +50, Kaunas +15, Klaipėda +11, Rīga +10,
Šiauliai +9, Alytus +5, Panevėžys +5, Liepāja +4, Mārupe +4, Ventspils +4.

**Sole-supplier cities** (the row is the only provider of its service in that city): 57 in
scope, **22 resolved**. Wins that matter disproportionately — Ventspils (Noord Natie Ventspils
Terminals, `nnvt@nnvt.lv`), Salaspils warehousing (Kuehne+Nagel), Rēzekne (Lankorf),
Aizkraukle, Saldus, Grobiņa, Kuldīga, Ogre.

**35 sole-supplier cities remain unreachable.** These are the most expensive gaps in the file —
a request from Mērsrags, Roja, Skrunda, Viļāni, Ludza, Pagėgiai, Šalčininkai or Adutiškis has
nowhere to go:

LT — Adutiškis (warehouse), Anykščiai (warehouse), Ignalina, Jurbarkas, Kėdainiai (moving),
Lentvaris, Molėtai, Pagėgiai (warehouse), Pasvalys, Salamiestis, Seirijai, Šalčininkai
(warehouse), Šilalė (warehouse), Šilutė (moving), Trakai (warehouse), Vievis.
LV — Ādaži ×2, Baloži, Jaunmārupe, Jēkabpils (warehouse), Ķekava (warehouse), Kuldīga
(warehouse), Ludza (moving), Malta, Mērsrags (moving), Olaine (warehouse), Rēzekne, Roja
(moving), Ropaži (warehouse), Salaspils (moving), Saulkrasti, Skrunda (moving), Valmiera,
Viļāni (moving).

## What "no_email_found" actually means here

It is a researched result, not a shrug. Of the 175:

- **127 were positively identified** in a registry-backed directory — the company exists, we
  have its record URL, and that record publishes a phone and no e-mail. Stop looking online;
  these are phone-only businesses. The `sources` field points at the record that proves it.
- **48 could not be located at all** — no directory record, no resolving domain under the
  trading name. Many are unregistered trade names or one-person operations advertising only by
  phone.

Checks that were run and came back empty, so nobody repeats them: VisaLietuva.lt profile pages
were re-fetched for the 67 LT rows whose listing showed no address — the "El. paštas" label on
those pages is a contact-form placeholder, not a hidden value. Firmas.lv paywalls contacts at
€5/company. ZL.LV, 1182.lv, 1189.lv and kontakti.lv publish no e-mail at all.
Rekvizitai.lt returns 403 to this host. Kompass and zo.lv return 403.

## The single change that would unlock the most reachable supply

**Capture the e-mail during the first phone call and write it back to the directory row.**

127 of the 175 unreachable rows are confirmed live businesses whose address exists — it simply
is not published anywhere online. No amount of further scraping reaches them; the address only
exists inside a phone conversation. Ops is already calling suppliers in the manual match loop,
so the marginal cost is one field. A one-line "e-mail for quotes?" prompt plus a save-back in
admin converts the largest single block of dead rows in the directory, and it compounds — every
call from then on either confirms or repairs a contact.

Second-best, and cheap: filter the introduction campaign on `mxOk === true` rather than "has an
address". 16 of the 176 addresses in this file are real but undeliverable; sending to them
costs sender reputation on a domain that is about to run its only introduction campaign.
