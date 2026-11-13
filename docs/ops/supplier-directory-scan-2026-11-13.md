# 2026-11-13: supplier directory scan (health + discovery)

This is a **report for approval, nothing has been applied.** No row was edited,
deactivated or emailed. Every proposed change below has an empty `[ ]` box: tick
the ones you approve and the change is made by hand through the admin API (see
"Applying approved rows" at the end).

The tool that produced this is `tools/supplier_scan/` (see its README). It reads a
READ ONLY production snapshot taken 2026-11-13, the three Baltic company registers'
free open data downloaded the same day, each supplier's own website, and the
outreach history in `ProviderOutreaches`. The 2026-08-19 audit
(`supplier-directory-audit-2026-08-19.md`) is the prior art. That audit counted
rows; this scan checks whether each business still exists and whether we hold its
current contact details.

## Update, same day: founder decisions and second pass

The founder approved **all Part 1 fixes and removals**. Calls are dropped: silent and unclear
providers get a reply-coded e-mail instead. That mail is drafted in
`silent-providers-reengagement-2026-11-13.md` and has not been sent. Demand analytics are in
`demand-analytics-2026-11-13.md`.

**Classifier fixes after the first run.** The tables below are from the first run. The current
counts are: keep 1,094, fix_contact 45, call 21, remove 3, opt-out 17, inactive 9.

- A bounce or "silence" counts only when it went to the address we hold *now*. One row was flagged
  for an e-mail that had already been corrected.
- When the website redirects to the e-mail's own domain, the redirect target is proposed as the
  website.

**Apply plan** (`scan.py plan`, then `out/apply-plan.json`):

- 45 contact fixes (PATCH)
- 3 soft deletes: Motorsport OÜ, RK Trans OÜ, SIA Rīgas saldētava
- 427 registry codes found by unique name match

14 codes were held back because two rows share them (chain branches) or another row already uses
them; RegistryCode is unique. **Not applied yet:** the admin API needs a logged-in ruumly.eu
session.

**Opt-outs (17: LV 15, all KABI branches; LT 2).** All of them are `IsActive=false`, so they are
hidden from every public surface: the partner page, sitemap and search all require `IsActive`. No
outreach was sent after any opt-out date. The rows are left untouched.

**Inactive rows (9 besides the opt-outs)** are already soft-deleted and invisible. A hard purge is
irreversible, and it would also remove the dedupe key that stops discovery or import from
re-adding the same company. It is not done without an explicit yes. Opt-out rows must never be
purged.

**Discovery second pass.**

- Viljandi was added as a zone. It sent 8 of the 40 requests and was not covered before.
- The shortlist is now 94 rows across 4 zones.
- The review CSV has 3 new columns:
  - `site_brand`: og:site_name, or the brand part of the title. It is a hint only, because some
    sites put a description there.
  - `household_hits`: household-facing words on the homepage, in 5 languages.
  - `b2b_hits`: B2B-only phrasing.
- **The register cannot fill the cells that matter.** Warehouse and van rental are 70% of demand,
  yet the register finds 0 to 3 candidates per zone, because storage and van hire hide under
  generic real-estate and rental EMTAK codes. Places Text Search is the right source for exactly
  those cells: warehouse, trailer and van rental × 4 zones × 3 pages = 36 calls. The worst case
  is **$1.26**, and it costs $0 inside the free cap. It still needs your OK.

## The answer in six lines

1. **Dead businesses are rare.** Across 1,163 active rows, only **3** are provably
   gone (registry struck off, no live website). The directory is not full of
   closed companies.
2. **The real problem is silence.** 150 rows have been sent a request and **18 ever
   answered**. In the Viljandi/Türi cluster alone, six providers got 4–9
   requests each and never replied. Those rows are listed under `call`, not `remove`.
   The address works and nobody reads it, and only a phone call will tell us why.
3. **41 rows have a better contact on their own website.** 7 of them are Estonian,
   including the Kaubikurent example: `info@kaubikurentparnus.ee` hard-bounced, and
   the site now redirects to kaubikurenttallinnas.ee and prints
   `info@kaubikurenttallinnas.ee`.
4. **Open tracking records nothing.** `ProviderOutreach.OpenedAt` is null on all 270
   sends, including the 21 that quoted. The "delivered → opened → quoted" funnel that
   the 2026-08-18 work added has no middle step. See follow-ups.
5. **The EE register is a free, clean discovery source.** It lists every company's
   declared activity (EMTAK), its registered e-mail and website, and its last annual
   report. In Harjumaa, Pärnu and Tartu that gives 1,252 contactable candidates we
   don't list. A shortlist of 79 is below.
6. **Warehouse, trailer and van rental are where the register runs thin**, because
   those firms register as "property rental" or "car rental" under generic names.
   Google Places is the right tool for those three services, but it is paid, so it
   was **not run** (pricing below).

## What was checked, per row

| signal | source | what counts |
|---|---|---|
| registry status | EE lihtandmed, LV UR `register.csv`, LT JAR live + struck-off files | struck off / liquidation / bankruptcy, and whether the code even belongs to this company |
| website | one GET of the homepage plus at most two contact pages, robots.txt honoured | DNS, HTTP status, redirect to another domain, parked page, and the e-mails and phones the site prints |
| mailbox | supplier bounce flags, `ProviderOutreaches.Status = Bounced`, MX/A lookup | hard bounce or a domain that cannot receive mail. A soft bounce does not count as dead |
| responsiveness | `ProviderOutreaches` | sent, delivered, quoted, replied, declined. Opens are ignored because nothing records them |
| opt-out | `MarketingOptOutAt` | **17 rows (LV 15, LT 2) were skipped entirely**: no website fetch, no proposal |

How the decisions are made (full rules in `tools/supplier_scan/health.py`):

- **remove** needs the registry to say the company is gone **and** no live website,
  or a dead website **and** a dead mailbox with no reply on record.
- An **EE code that is missing from the open data is not proof** the company is gone.
  The simple-data file leaves out struck-off entries, but also some branch types
  (Gjensidige's filiaal, for example) and typos. Where the website is live, the row
  goes to `call`.
- **A brand that differs from the legal name is normal** (Kaubikud.ee is run by DST
  Auto OÜ, CoralClean by Puhastus KORAL OÜ). There are 29 such rows. They are noted
  but not acted on, unless the company behind the code is dead. Then the code on our
  row is probably wrong. That happened once: Esvo Transport's code belongs to a
  bankrupt "JPPP Spedition OÜ".
- **A proposed e-mail must be deliverable.** It needs a domain with MX or A records
  and must not be a form placeholder. This check dropped a ROT13-obfuscated address
  (`…@nyybirefrnf.arg`, which is alloverseas.net), a typo (`inbox.lv.lv`) and seven
  Mantu Depo rows whose site only prints the placeholder `epasts@epasts.lv`.
- A **404 on a deep link with a live homepage** means the page moved, not that the
  business closed. All 27 VIADA trailer rows point at one deleted page, and the fix
  is a URL change.
- **401/403/429** is a bot wall and **5xx** (after one retry) is a degraded site.
  Neither counts as evidence against the business.

## Part 1: existing rows

Estonian priority zones first, as requested:

| EE zone | rows | keep | fix_contact | remove | call | inactive |
|---|---:|---:|---:|---:|---:|---:|
| Harjumaa | 113 | 105 | 6 | 0 | 1 | 1 |
| Pärnu | 8 | 7 | 1 | 0 | 0 | 0 |
| Tartu | 27 | 24 | 0 | 0 | 3 | 0 |
| rest of EE | 101 | 85 | 0 | 2 | 9 | 5 |

The ops loop's home turf is in good shape. Harjumaa has nothing to remove, and five
of its six fixes add an e-mail to a row that has none.

Zones: Harjumaa is an approximate county bounding box, and Pärnu and Tartu are 25 km
radii, all taken from `SupplierLocations`. A row with no real location falls back to
its slug and city.


### Reading the `call` list

Eleven of the 21 are **"N requests, never answered"**, and six of those are the
Viljandi/Türi storage and trailer cluster: Ansvil, Pointrent, Aikerauto, Järva Rehvid,
Blackline Viljandi and Miil Viljandi. They got 4–9 requests each, mostly delivered,
and gave zero answers. A seventh Viljandi row, AK Rent, had 8 requests go unanswered,
and its website now times out. Removing them would hide the problem rather than solve it.
A call settles which of these it is: wrong inbox, wrong desk (a petrol-station head
office), or "we don't do households". The answer to the last one goes into
`ServesConsumers`.

Two rows have **no slug** (`Miniladu24 Viiratsi`, `Marianna Skorina`), so they cannot
have a partner page. They are listed by id.

### Not in the tables, but in the CSV

- **444 rows have no registry code, and a unique name match gives them one**
  (LT 272, LV 112, EE 60). That is the `proposed_registry_code` column of
  `health-ee-lt-lv.csv`. A spot check of 15 at random found all 15 correct, because
  the match only counts when the normalised name is unique among live companies.
  Applying it would raise registry-code coverage from 335 to 779 rows, and the next
  scan would then check those rows' registry status too. It is one bulk PATCH and
  worth doing, but it is a separate approval from the rows above.
- **74 rows' phone number does not appear on their own website**, where the site
  prints a different one. They stay `keep` because a phone mismatch alone is weak.
  They are listed in the CSV `reasons` column for whoever makes the `call` round.

## Part 2: providers we don't list

### Source

The **Estonian e-Business Register general-data file** (`yldandmed`, free, open
licence, about 230 MB) covers every company. For each one it gives the declared
EMTAK activities, the registered e-mail, website and phone, and the latest annual
report (period and headcount). 1182.ee, teatmik.ee and inforegister.ee all republish
this same data. Reading it at the source avoids scraping directories that forbid it:
1182.ee's robots.txt blocks harvesters, and inforegister.ee answers 403.

Service mapping (`discover.py`):

| service | rule |
|---|---|
| moving | EMTAK 49421 *Kolimisteenused*, or "kolimi-/movers/pereezd" in the name or website |
| cleaning | main activity 81211/81231/81291 (or 81221 plus a cleaning word). Excludes chimney sweeps, industrial cleaning, municipal maintenance and waste firms |
| warehouse | a self-storage word (miniladu, hoiuruum, laoboks, self storage…) in the name or website, with a storage/rental/moving code |
| trailer | "haagis/trailer/järelkäru" in the name or website, with a rental code. Excludes caravans and sauna trailers |
| vanrental | code 77111 (cars and vans ≤3.5 t) or 77121 **plus** a van word. 77121 alone is heavy-truck rental (Scania, Wirtgen, GoBus all matched it) |

Filters: the company is active, has filed an annual report for 2024 or later, sits in
Harju, Pärnu or Tartu county, and has an e-mail or a website. **The scan dropped
78 companies that shared one of 19 "mass mailboxes"**, inboxes registered on three
or more companies. For example, one protonmail address sat on ten shell "warehouse"
OÜs. It also dropped `@eesti.ee` addresses. Those are the state-issued personal
mailboxes keyed on an ID code, so they are personal data and never a business
contact.

Dedupe against **all** 1,189 rows, including inactive and opted-out ones, so a
company that opted out cannot come back in under a new slug. A candidate matches an
existing row on any of: registry code, country-qualified phone, e-mail, e-mail domain
(free-mail excluded), website domain (social sites excluded), or name within the same
country. **44 candidates were already listed.** They include storage operators such as
Minilaod, Vahi Minilaod and Konteinerladu. Few self-storage firms carry a storage
word in their legal name at all, which is why warehouse only adds three.

### Coverage the register finds, per zone

| | moving | cleaning | warehouse | trailer | vanrental |
|---|---:|---:|---:|---:|---:|
| Harjumaa | 11 | 12 | 3 | 5 | 3 |
| Pärnu | 7 | 12 | 0 | 0 | 0 |
| Tartu | 12 | 12 | 0 | 1 | 1 |

Moving and cleaning hit the cap of 12 per cell. The shortlist is ranked by: has a
website, e-mail on its own domain, then 1–60 staff. Ranking by headcount alone put
1,200-person facility-management firms first. The concierge sells to households, so
small and mid-size firms rank higher. The empty and thin cells are the register's
blind spot, not proof that no supply exists.

### Before you approve a discovery row

- **The register name is the legal name, not the brand.** "Groundmark Holding" is
  Home Storage (homestorage.ee) and "Juhiga rent" is vaikebussirent.ee. Edit the
  `name` column of the review CSV to the brand before approving, because the name
  becomes the public partner-page title.
- **The coordinates are the registered office** (geocoded by the official Maa-amet
  In-ADS service), which is often an accountant's address. For a warehouse, check the
  actual site.
- **Many tiny moving and cleaning firms only have a gmail/hot.ee address.** That is
  how the trade works here, but such a firm is also the most likely to read a cold
  quote request as spam (see the 2026-08-19 audit).
- **The tagline is empty.** The import accepts that, but the partner page looks bare
  without one.


## Google Places: priced, not run

Prices checked 2026-11-13 on Google's pricing page, in USD per 1,000 requests at the
first tier, with the free monthly cap in brackets:

| SKU | price | free / month | needed for |
|---|---:|---:|---|
| Place Details Pro | $17 | 5,000 | `businessStatus` (CLOSED_PERMANENTLY) of existing rows |
| Text Search Pro | $32 | 5,000 | name, address, location, status |
| Text Search Enterprise | $35 | 1,000 | adds website and phone, which dedupe needs |

- **Status check of existing rows: not worth it.** Only **1** of 1,189 rows has a
  `GooglePlaceId`. A full Place-ID lookup for every row would be Text Search, about
  1,200 calls, and the registers already answer "is it closed" for free.
- **Discovery: worth it for the thin cells only.** Warehouse, trailer and van rental
  across 3 zones, at 3 pages per query, comes to 27 calls. The worst case is
  **$0.95**, and it is $0 inside the free cap. The cap is shared with the backend,
  which already calls Places for reviews, and we cannot see that usage from here, so
  the tool treats every call as billable. It refuses to run without
  `--i-accept-cost`.

If you approve, run this:

```bash
railway run --service Ruumly -- python -X utf8 tools/supplier_scan/scan.py discover --source places --towns harjumaa,parnu,tartu,viljandi --services warehouse,trailer,vanrental --i-accept-cost --max-calls 36
```

## What this scan does not cover

- **Latvian and Lithuanian discovery.** The LV and LT open-data registers carry
  status but no activity codes and no contact details, so they can confirm a company
  but cannot find one. That matches the direction in CLAUDE.md: the LV/LT directory
  is listings, not a live ops loop. Places would be the source for those countries.
- **Whether a provider takes household jobs.** Only a reply or a call answers that,
  and `ServesConsumers` is where the answer goes.
- **Phone numbers.** They were compared with the website but never dialled.

## Applying approved rows

Nothing below is automated. Each step needs an admin token, and you run it yourself.

| approved action | how |
|---|---|
| fix_contact | `PATCH /api/admin/suppliers/{id}` with only the changed fields (`contactEmail`, `contactPhone`, `websiteUrl`, `registryCode`). A new `contactEmail` also clears the bounce flags, so the next fan-out uses it. |
| remove | `DELETE /api/admin/suppliers/{id}`. This is a soft delete: `IsActive=false`, the partner page is unpublished, locations are deactivated, and it can be undone. **Never use `/purge`.** |
| call | Phone the provider and record the outcome as a `fix_contact`, a `remove`, or `ServesConsumers=false`. |
| discovery | Tick `approve=y` in `discovery-review-registry-2026-11-13.csv` and fix the name and coordinates. Then run `scan.py emit-import`, which validates slug, country bounding box, URLs and service slugs locally, and `scripts/import-directory.ps1 -DataDir tools/supplier_scan/out/import -WhatIf` first. |

The ids for every row are in `tools/supplier_scan/out/health-ee-lt-lv.csv`. That
folder is gitignored because it holds the production snapshot. To regenerate it, run
the stages in `tools/supplier_scan/README.md`. After the approved changes are applied,
run `dump` and `health` again, and the rows should come back as `keep`.

## Follow-ups this scan surfaced

1. **Open tracking is off or not wired.** `OpenedAt` is never set. Either Resend
   open tracking is disabled for the sending domain, or the webhook is not subscribed
   to `email.opened`. Until it is fixed, "provider silence" cannot be split into
   "never saw it" and "saw it and ignored it", and that split was the whole point of
   the 2026-08-18 funnel.
2. **Bounce data is thin.** One hard bounce in the whole directory, on 270 sends.
   That is plausible, but it is worth confirming that the webhook still records
   `email.bounced` (it did for Kaubikurent on 2026-10-25).
3. **Run a phone round on the silent Viljandi cluster** before drawing any further
   conclusions about Viljandi supply. The 2026-08-19 audit made the same point, and
   the silence has grown since.

## Appendix: approval tables

<!-- generated by tools/supplier_scan/scan.py report from health-ee-lt-lv.csv, discovery-review-registry-2026-11-13.csv -->

### Counts

| country | keep | fix_contact | remove | call | skip_opt_out | skip_inactive |
|---|---:|---:|---:|---:|---:|---:|
| EE | 221 | 7 | 2 | 13 | 0 | 6 |
| LV | 285 | 30 | 1 | 5 | 15 | 0 |
| LT | 592 | 4 | 0 | 3 | 2 | 3 |

#### remove — 3 rows

| ok | cc | zone | row | conf | evidence |
|---|---|---|---|---|---|
| [ ] | EE |  | Motorsport OÜ (`motorsport-voru`) | medium | registry 11538760: deleted (code not present in the open-data file); outreach sent=1 delivered=0 quoted=0 replied=0 declined=0; EE code absent from open data and no live website |
| [ ] | EE |  | RK Trans OÜ (`rk-trans-viljandi`) | medium | registry 10187560: deleted (code not present in the open-data file); outreach sent=1 delivered=0 quoted=0 replied=0 declined=0; EE code absent from open data and no live website |
| [ ] | LV |  | SIA Rīgas saldētava (`rigas-saldetava-marupe`) | high | registry 47101007260: deleted (closed=L terminated=2026-09-03) |

#### fix_contact — 41 rows

| ok | cc | zone | row | change (current → proposed) | conf | evidence |
|---|---|---|---|---|---|---|
| [ ] | EE | harjumaa | ClearSYS (`clearsys`) | email: clearsys123@gmail.com → **clearsyspuhastus@gmail.com** | low | operated by 'Peakcredit OÜ' (12233293); outreach sent=2 delivered=2 quoted=0 replied=0 declined=0; our email not on site; site prints clearsyspuhastus@gmail.com; never answered at our address |
| [ ] | EE | harjumaa | KolimisPartner (`kolimispartner`) | email: ∅ → **info@kolimispartner.ee** | high | https://kolimispartner.ee/ prints info@kolimispartner.ee |
| [ ] | EE | harjumaa | Ladu32 (`ladu32`) | email: ∅ → **igo.sagri@1partner.ee** | medium | https://ladu32.ee/ prints igo.sagri@1partner.ee |
| [ ] | EE | harjumaa | Moving24 OÜ (`moving24`) | email: info@moving24.ee → **moving24ee@gmail.com** | low | outreach sent=2 delivered=0 quoted=0 replied=0 declined=0; our email not on site; site prints moving24ee@gmail.com; never answered at our address |
| [ ] | EE | harjumaa | Noortegija (`noortegija`) | email: ∅ → **info@noortegija.ee** | high | https://noortegija.ee/ prints info@noortegija.ee |
| [ ] | EE | harjumaa | T49 Kolimisteenus (`t49`) | email: ∅ → **info@t49.ee** | high | https://t49.ee/ prints info@t49.ee |
| [ ] | EE | parnu | Kaubikurent Pärnus (`kaubikurent-parnus`) | email: info@kaubikurentparnus.ee → **info@kaubikurenttallinnas.ee**<br>website: https://kaubikurentparnus.ee → **https://kaubikurenttallinnas.ee/** | high | email bounced (hard); website redirects to kaubikurenttallinnas.ee; operated by 'HPH Invest OÜ' (16146970); outreach sent=3 delivered=1 quoted=0 replied=0 declined=0; https://kaubikurenttallinnas.ee/ prints info@kaubikurenttallinnas.ee |
| [ ] | LV |  | AMELA1 (`amela1-liepaja`) | email: ∅ → **t-style@inbox.lv** | medium | https://amela.lv/piekabju-noma-iznoma-piekabes-liepaja/ prints t-style@inbox.lv |
| [ ] | LV |  | SIA Eltoro (`eltoro-salaspils`) | email: ∅ → **info@eltoro.lv** | high | https://www.eltoro.lv/ prints info@eltoro.lv |
| [ ] | LV |  | Uzkopšana Liepāja (`uzkopsana-liepaja`) | email: ∅ → **info@uzkopsanaliepaja.lv** | high | https://www.uzkopsanaliepaja.lv/ prints info@uzkopsanaliepaja.lv |
| [ ] | LV |  | VIADA Baltija AS — 6 rows (`viada-daugavpils-nometnu` …) | website: https://www.viada.lv/degvielasuzpildesstacijas/produkti-un-pakalpojumi/piekabju-noma/ → **https://www.viada.lv/** | medium | website page 404; site root https://www.viada.lv/ is live; outreach sent=1 delivered=1 quoted=0 replied=0 declined=0 |
| [ ] | LV |  | VIADA Baltija — 21 rows (`viada-aizkraukle` …) | website: https://www.viada.lv/degvielasuzpildesstacijas/produkti-un-pakalpojumi/piekabju-noma/ → **https://www.viada.lv/** | medium | website page 404; site root https://www.viada.lv/ is live |
| [ ] | LT |  | Eirena (`eirena-vilnius`) | email: ∅ → **info@eirena.lt** | medium | https://transportopaslaugos.com/ prints info@eirena.lt |
| [ ] | LT |  | Kirobox, MB (`kirobox-kaunas`) | email: ∅ → **info@kirobox.lt**<br>reg code: ∅ → **307616268** | high | registry code by unique name match: 307616268 (active); https://kirobox.lt/ prints info@kirobox.lt |
| [ ] | LT |  | LIŪTUKAI LT, UAB (`liutukai-lt-vilnius`) | email: ∅ → **matsoveled@gmail.com**<br>phone: +370 65670712 → **+37067120481**<br>website: https://tvarkaubiurus.lt → **https://www.sepetys.lt/**<br>reg code: ∅ → **303430362** | medium | website redirects to sepetys.lt; registry code by unique name match: 303430362 (active); https://www.sepetys.lt/ prints matsoveled@gmail.com |
| [ ] | LT |  | Veverent (Stits, MB) (`veverent-pagiriai`) | email: ∅ → **infoveverent@gmail.com** | medium | https://veverent.lt/ prints infoveverent@gmail.com |

#### call — 21 rows

| ok | cc | zone | row | conf | evidence |
|---|---|---|---|---|---|
| [ ] | EE | harjumaa | ADB Gjensidige Eesti filiaal (`gjensidige`) | medium | registry 11193232: deleted (code not present in the open-data file); EE code absent from open data (struck off, a branch, or a typo) |
| [ ] | EE | tartu | Kapsel Minilaod (`kapsel-minilaod`) | low | website dead: dns_fail; website dead; mailbox not known-bad |
| [ ] | EE | tartu | Panicom Miniladu (`panicom-miniladu`) | low | outreach sent=3 delivered=2 quoted=0 replied=0 declined=0; 3 requests, never answered |
| [ ] | EE | tartu | Tartu Minilaod (`tartu-minilaod`) | low | outreach sent=4 delivered=2 quoted=0 replied=0 declined=0; 4 requests, never answered |
| [ ] | EE |  | AK Rent (`ak-rent`) | low | website dead: conn_error ConnectTimeout; registry code by unique name match: 16552523 (active); outreach sent=8 delivered=7 quoted=0 replied=0 declined=0; website dead; mailbox not known-bad |
| [ ] | EE |  | Aikerauto OÜ (`aikerauto-viljandi`) | low | outreach sent=8 delivered=7 quoted=0 replied=0 declined=0; 8 requests, never answered |
| [ ] | EE |  | Ants Viljandi OÜ (Ansvil) (`ansvil-viljandi`) | low | outreach sent=9 delivered=7 quoted=0 replied=0 declined=0; 9 requests, never answered |
| [ ] | EE |  | Blackline OÜ – Konteinerladu Viljandi (`blackline-konteinerladu-viljandi`) | low | outreach sent=6 delivered=3 quoted=0 replied=0 declined=0; 6 requests, never answered |
| [ ] | EE |  | Esvo Transport OÜ (`esvo-transport-turi`) | medium | registry 10169177: bankruptcy (Pankrotis), registered as 'JPPP Spedition OÜ'; code on our row belongs to another company |
| [ ] | EE |  | Järva Rehvid OÜ (`jarva-rehvid-turi`) | low | outreach sent=8 delivered=7 quoted=0 replied=0 declined=0; 8 requests, never answered |
| [ ] | EE |  | Miil OÜ – Smart Warehouse Viljandi (`miil-smart-warehouse-viljandi`) | low | outreach sent=4 delivered=3 quoted=0 replied=0 declined=0; 4 requests, never answered |
| [ ] | EE |  | Miniladu24 Viiratsi (id `7fdbeda3-fd26-4eb3-9b46-5fb640fddbe8` (no slug)) | medium | email bounced (outreach status Bounced); outreach sent=4 delivered=3 quoted=0 replied=0 declined=0; mailbox dead and no alternative address on the website |
| [ ] | EE |  | Pointrent OÜ (`pointrent-viljandi`) | low | outreach sent=9 delivered=8 quoted=0 replied=0 declined=0; 9 requests, never answered |
| [ ] | LV |  | Alvi Car Rent (`alvi-car-rent-riga`) | low | outreach sent=4 delivered=2 quoted=0 replied=0 declined=0; 4 requests, never answered |
| [ ] | LV |  | Busu Noma — Krimuldas iela, Rīga (`busu-noma-riga`) | low | outreach sent=3 delivered=1 quoted=0 replied=0 declined=0; 3 requests, never answered |
| [ ] | LV |  | ProMove (promove.lv) (`promove-riga`) | low | website dead: conn_error ConnectionError; website dead; mailbox not known-bad |
| [ ] | LV |  | SIA ADROL (Boxin) (`boxin-liepaja`) | low | website dead: conn_error ConnectionError; website dead; mailbox not known-bad |
| [ ] | LV |  | SIA MEKS GROUP (`meks-group-riga`) | low | registry code by unique name match: 44103148869 (active); outreach sent=4 delivered=2 quoted=0 replied=0 declined=0; 4 requests, never answered |
| [ ] | LT |  | BALTAMA, UAB (`baltama-klaipeda`) | low | website dead: conn_error ConnectionError; registry code by unique name match: 303211475 (active); website dead; mailbox not known-bad |
| [ ] | LT |  | GREENAS, UAB (`greenas-vilnius`) | low | website dead: conn_error ConnectTimeout; registry code by unique name match: 300636325 (active); website dead; mailbox not known-bad |
| [ ] | LT |  | IDEALI ŠVARA, UAB (`ideali-svara-vilnius`) | medium | email domain idealisvara.lt cannot receive mail (no); registry code by unique name match: 300128405 (active); mailbox dead and no alternative address on the website |

### Discovery shortlist

| ok | zone | service | name | email | phone | website | reg code | town | staff | EMTAK |
|---|---|---|---|---|---|---|---|---|---|---|
| [ ] | harjumaa | warehouse | Groundmark Holding | info@efexon.ee |  | https://www.homestorage.ee/tallinn/ | [12176566](https://ariregister.rik.ee/est/company/12176566) | Tallinn | 0 | 68201 Enda või renditud kinnisvara üürileandmine ja käitus |
| [ ] | harjumaa | warehouse | OÜ Miniladu | info@alptom.ee |  |  | [14854065](https://ariregister.rik.ee/est/company/14854065) | Peetri | 2 | 68201 Enda või renditud kinnisvara üürileandmine ja käitus |
| [ ] | harjumaa | warehouse | Miniladu24 | stas_mb@mail.ru |  |  | [16960995](https://ariregister.rik.ee/est/company/16960995) | Loo | 0 | 68201 Enda või renditud kinnisvara üürileandmine ja käitus |
| [ ] | harjumaa | moving | Tellikolimine Consulting | info@tellikolimine.ee | +372 56987980 | https://tellikolimine.ee/ | [11623342](https://ariregister.rik.ee/est/company/11623342) | Tallinn | 2 | 49421 Kolimisteenused, 52101 Kaubaladude töö |
| [ ] | harjumaa | moving | Transportations24 | info@transportations24.ee | +372 53243094 | https://www.transportations24.ee/ | [16899474](https://ariregister.rik.ee/est/company/16899474) | Tallinn | 1 | 49421 Kolimisteenused |
| [ ] | harjumaa | moving | Arex Logistics | info@arex.ee | +372 56666518 | https://www.arex.ee/ | [14094826](https://ariregister.rik.ee/est/company/14094826) | Tallinn | 0 | 49421 Kolimisteenused |
| [ ] | harjumaa | moving | OK Movers | info@okmovers.ee | +372 5047187 | https://okmovers.ee/ | [11428959](https://ariregister.rik.ee/est/company/11428959) | Iru | 0 | 49421 Kolimisteenused |
| [ ] | harjumaa | moving | SJsignature | info@privatetransfer.ee | +372 56919083 | https://privatetransfer.ee/ | [16191769](https://ariregister.rik.ee/est/company/16191769) | Tallinn | 0 | 49421 Kolimisteenused, 49391 Sõitjate muu mujal liigitamata mais |
| [ ] | harjumaa | moving | Special Service Group | info@ssg.ee | +372 5036450 | https://ssg.ee/ | [10521214](https://ariregister.rik.ee/est/company/10521214) | Tallinn | 0 | 49421 Kolimisteenused |
| [ ] | harjumaa | moving | Veotex | veotex.info@gmail.com | +372 53768624 | https://www.facebook.com/VeotexEU/ | [14759438](https://ariregister.rik.ee/est/company/14759438) | Tallinn | 0 | 49421 Kolimisteenused |
| [ ] | harjumaa | moving | abmk kaubaveod | abmkkv@gmail.com | +372 56254251 |  | [16091022](https://ariregister.rik.ee/est/company/16091022) | Tallinn | 10 | 78209 Muud tööjõuteenused, 52101 Kaubaladude töö, 49421 Kolimist |
| [ ] | harjumaa | moving | MERCURY TRANSPORTATIONS | mercuryveod@gmail.com | +372 56855138 |  | [14063837](https://ariregister.rik.ee/est/company/14063837) | Tallinn | 9 | 49421 Kolimisteenused, 49411 Kaubavedu maanteel |
| [ ] | harjumaa | moving | Aret veod ja kolimine | aret.veod@gmail.com |  |  | [12929480](https://ariregister.rik.ee/est/company/12929480) | Tallinn | 8 | 49411 Kaubavedu maanteel |
| [ ] | harjumaa | moving | Box Moving | info@boxmoving.ee |  |  | [16722250](https://ariregister.rik.ee/est/company/16722250) | Tallinn | 7 | 95241 Mööbli ja sisustusesemete parandus ning hooldus, 49411 Kau |
| [ ] | harjumaa | trailer | OÜ Protrailer | info@protrailer.ee | +372 5017788 |  | [10866953](https://ariregister.rik.ee/est/company/10866953) | Loo | 1 | 46711 Mootorsõidukite hulgimüük, 77111 Sõiduautode ja väikebussi |
| [ ] | harjumaa | trailer | R.E.D. TRAILER RENTAL | red@redrent.ee | +372 55530085 |  | [11163656](https://ariregister.rik.ee/est/company/11163656) | Tallinn | 1 | 77399 Muu mujal liigitamata masinate, seadmete jm materiaalse va |
| [ ] | harjumaa | trailer | Osaühing AUTOHAAGIS BALTI | arne@koegel.ee | +372 6119751 |  | [10669690](https://ariregister.rik.ee/est/company/10669690) | Tallinn | 0 | 77399 Muu mujal liigitamata masinate, seadmete jm materiaalse va |
| [ ] | harjumaa | trailer | SinuHaagis | andri.oras@gmail.com |  |  | [17199626](https://ariregister.rik.ee/est/company/17199626) | Saku | 0 | 47811 Mootorsõidukite jaemüük, 47121 Muu spetsialiseerimata jaem |
| [ ] | harjumaa | trailer | United Trailers | unitedtrailers@gmail.com | +372 6553570 |  | [11729939](https://ariregister.rik.ee/est/company/11729939) | Lagedi | 0 | 77121 Veokite rentimine ja kasutusrent |
| [ ] | harjumaa | cleaning | Osaühing LAIGOVER | reigo.roos@laigover.ee | +372 6396690 | https://www.laigover.ee/ | [10920003](https://ariregister.rik.ee/est/company/10920003) | Tallinn | 20 | 81211 Hoonete üldpuhastus |
| [ ] | harjumaa | cleaning | SPS Grupp | info@spsgrupp.ee | +372 6623328 | https://spsgrupp.ee/ | [11394806](https://ariregister.rik.ee/est/company/11394806) | Tallinn | 16 | 81211 Hoonete üldpuhastus |
| [ ] | harjumaa | cleaning | osaühing Holgersson | asko@holgersson.ee | +372 53333314 | https://holgersson.ee/ | [10825960](https://ariregister.rik.ee/est/company/10825960) | Randvere | 12 | 81211 Hoonete üldpuhastus, 81221 Hoonete muu puhastus ja tööstus |
| [ ] | harjumaa | cleaning | OÜ PC Heakord | info@pcheakord.ee | +372 5111653 | https://pcheakord.ee/ | [16730151](https://ariregister.rik.ee/est/company/16730151) | Kiili | 11 | 81211 Hoonete üldpuhastus, 81301 Maastiku hooldus ja korrashoid |
| [ ] | harjumaa | cleaning | PELIAS BALTIC | info@peliasbaltic.ee |  | https://www.peliasbaltic.ee/ | [16057723](https://ariregister.rik.ee/est/company/16057723) | Kiili | 10 | 81231 Muu puhastustegevus |
| [ ] | harjumaa | cleaning | OÜ KODU HALDUS | haldus@koduhaldus.ee | +372 6605101 | https://koduhaldus.ee/ | [10510914](https://ariregister.rik.ee/est/company/10510914) | Tallinn | 8 | 69202 Raamatupidamine, maksualane nõustamine, 81211 Hoonete üldp |
| [ ] | harjumaa | cleaning | OÜ LIFA | info@ventpuhastus.ee | +372 5082888 | https://ventpuhastus.ee/ | [11552501](https://ariregister.rik.ee/est/company/11552501) | Tallinn | 8 | 81221 Hoonete muu puhastus ja tööstuslik puhastustegevus |
| [ ] | harjumaa | cleaning | osaühing FREHAME | frehame@frehame.ee | +372 6463360 | https://www.frehame.ee/ | [10040911](https://ariregister.rik.ee/est/company/10040911) | Pärnamäe | 7 | 81211 Hoonete üldpuhastus |
| [ ] | harjumaa | cleaning | TipTop teenindus | info@haldusexpert.ee | +372 5121352 | https://haldusexpert.ee/ | [11713559](https://ariregister.rik.ee/est/company/11713559) | Tallinn | 7 | 81211 Hoonete üldpuhastus |
| [ ] | harjumaa | cleaning | AllClean | info@allclean.ee | +372 5071378 | https://www.allclean.ee/ | [11532881](https://ariregister.rik.ee/est/company/11532881) | Tallinn | 5 | 81211 Hoonete üldpuhastus, 55203 Puhkekülad ja puhkelaagrid |
| [ ] | harjumaa | cleaning | Heakorrateenused | info@heakorrateenused.ee | +372 6622778 | https://kavex.ee/ | [11971936](https://ariregister.rik.ee/est/company/11971936) | Tallinn | 5 | 81211 Hoonete üldpuhastus |
| [ ] | harjumaa | cleaning | Hea puhastus | info@heapuhastus.ee |  | https://heapuhastus.ee/ | [14651623](https://ariregister.rik.ee/est/company/14651623) | Tallinn | 5 | 81211 Hoonete üldpuhastus |
| [ ] | harjumaa | vanrental | Juhiga rent | info@vaikebussirent.ee | +372 5549021 | https://vaikebussirent.ee/ | [14531072](https://ariregister.rik.ee/est/company/14531072) | Tallinn | 0 | 77111 Sõiduautode ja väikebusside (täismassiga kuni 3,5 t) renti |
| [ ] | harjumaa | vanrental | Premium Van | dmitri@caliente.ee | +372 56919589 |  | [12701274](https://ariregister.rik.ee/est/company/12701274) | Tallinn | 0 | 77111 Sõiduautode ja väikebusside (täismassiga alla 3,5 t) renti |
| [ ] | harjumaa | vanrental | Väikebussi rent juhiga | info@turundus.eu | +372 5549021 |  | [16756295](https://ariregister.rik.ee/est/company/16756295) | Tallinn | 0 | 77111 Sõiduautode ja väikebusside (täismassiga kuni 3,5 t) renti |
| [ ] | parnu | moving | Bambusa | kairekohtring@gmail.com | +372 5155165 |  | [14473765](https://ariregister.rik.ee/est/company/14473765) | Reiu | 2 | 49411 Kaubavedu maanteel, 49421 Kolimisteenused, 47921 Spetsiali |
| [ ] | parnu | moving | Kiviropka | kiviropkaoy@gmail.com | +372 58042209 |  | [14568626](https://ariregister.rik.ee/est/company/14568626) | Kilksama | 1 | 68201 Enda või renditud kinnisvara üürileandmine ja käitus, 8130 |
| [ ] | parnu | moving | Osaühing Primoral | marmorest@hot.ee | +372 5531199 |  | [10998382](https://ariregister.rik.ee/est/company/10998382) | Pärnu | 1 | 49421 Kolimisteenused |
| [ ] | parnu | moving | Energi | arletrobert@gmail.com |  |  | [14997818](https://ariregister.rik.ee/est/company/14997818) | Pärnu | 0 | 49421 Kolimisteenused |
| [ ] | parnu | moving | G&K Holdings | gabriel.einland.002@mail.ee | +372 53012440 |  | [14944762](https://ariregister.rik.ee/est/company/14944762) | Pärnu | 0 | 16281 Puidust tarbe- ja dekoratiivesemete jm puittoodete tootmin |
| [ ] | parnu | moving | osaühing ANDU PAPI | andres.pullat@gmail.com | +372 56614643 |  | [11323485](https://ariregister.rik.ee/est/company/11323485) | Viluvere | 0 | 49421 Kolimisteenused |
| [ ] | parnu | moving | R&D Kolimisteenus | d.kiviselg@gmail.com | +372 56279249 |  | [14808361](https://ariregister.rik.ee/est/company/14808361) | Ristiküla | 0 | 49421 Kolimisteenused |
| [ ] | parnu | cleaning | Ruudu Kinnisvarateenused | imre@ruudu.ee | +372 58377379 | https://www.ruudu.ee/ | [12500446](https://ariregister.rik.ee/est/company/12500446) | Pärnu | 4 | 81211 Hoonete üldpuhastus |
| [ ] | parnu | cleaning | OÜ Pipi Puhastus | pipipuhastus@gmail.com | +372 5293906 |  | [11948334](https://ariregister.rik.ee/est/company/11948334) | Pärnu | 25 | 81221 Hoonete muu puhastus ja tööstuslik puhastustegevus, 43351  |
| [ ] | parnu | cleaning | Pärnu Heakorrateenused | info@parnuheakorrateenused.ee | +372 56671830 |  | [14641582](https://ariregister.rik.ee/est/company/14641582) | Sauga | 14 | 81211 Hoonete üldpuhastus, 41001 Elamute ja mitteeluhoonete ehit |
| [ ] | parnu | cleaning | Riegler Kinnisvarahooldus | info@koristaja.ee | +372 4433280 |  | [14827329](https://ariregister.rik.ee/est/company/14827329) | Pärnu | 14 | 81211 Hoonete üldpuhastus, 81101 Hoonehalduse kombineeritud teen |
| [ ] | parnu | cleaning | OÜ Ideaalpuhastus | info@ideaalpuhastus.ee |  |  | [11657252](https://ariregister.rik.ee/est/company/11657252) | Pärnu | 10 | 68201 Enda või renditud kinnisvara üürileandmine ja käitus, 8121 |
| [ ] | parnu | cleaning | Timerita | info@timerita.com | +372 58588776 |  | [12210582](https://ariregister.rik.ee/est/company/12210582) | Pärnu | 9 | 81211 Hoonete üldpuhastus |
| [ ] | parnu | cleaning | AQmel Holdings | andrus.kumel@arem.ee | +372 56477207 |  | [12004262](https://ariregister.rik.ee/est/company/12004262) | Pärnu | 8 | 81211 Hoonete üldpuhastus |
| [ ] | parnu | cleaning | OÜ VL Puhastus | vlpuhastus@gmail.com | +372 5640645 |  | [11101425](https://ariregister.rik.ee/est/company/11101425) | Pärnu | 4 | 80091 Mujal liigitamata turvatöö, 81211 Hoonete üldpuhastus |
| [ ] | parnu | cleaning | OÜ Tuia | tuiakoristab@gmail.com |  |  | [14175111](https://ariregister.rik.ee/est/company/14175111) | Pärnu | 3 | 81211 Hoonete üldpuhastus, 55204 Külaliskorterid, 68201 Enda või |
| [ ] | parnu | cleaning | RRGR. Bygg | ragnarrahnel@outlook.com | +372 56600989 |  | [11685165](https://ariregister.rik.ee/est/company/11685165) | Pärnu | 3 | 81231 Muu puhastustegevus, 41001 Elamute ja mitteeluhoonete ehit |
| [ ] | parnu | cleaning | Webinar | ilse.aija@gmail.com | +372 58304597 |  | [16119002](https://ariregister.rik.ee/est/company/16119002) | Pärnu | 3 | 81211 Hoonete üldpuhastus |
| [ ] | parnu | cleaning | ARJ Teenus | liiviarukase@gmail.com |  |  | [17037289](https://ariregister.rik.ee/est/company/17037289) | Pärnu | 2 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | moving | A&T Veod | atveod@gmail.com | +372 56621718 |  | [12686391](https://ariregister.rik.ee/est/company/12686391) | Enno | 2 | 49421 Kolimisteenused |
| [ ] | tartu | moving | Kakumäe Autokoda | allankruusimaa@hot.ee | +372 5188788 |  | [12046616](https://ariregister.rik.ee/est/company/12046616) | Elva | 2 | 95311 Mootorsõidukite remont ja hooldus, 78201 Tööjõurent, 49421 |
| [ ] | tartu | moving | Nobe Nael | info@nobenael.ee | +372 5253875 |  | [14071179](https://ariregister.rik.ee/est/company/14071179) | Tartu | 1 | 49421 Kolimisteenused |
| [ ] | tartu | moving | OÜ Kiired Kolijad | marje.voobus@gmail.com |  |  | [12968209](https://ariregister.rik.ee/est/company/12968209) | Melliste | 1 | 49421 Kolimisteenused, 81301 Maastiku hooldus ja korrashoid |
| [ ] | tartu | moving | OÜ RK-CAR | korgesaarrainer@gmail.com | +372 58114087 |  | [16602072](https://ariregister.rik.ee/est/company/16602072) | Tartu | 1 | 49421 Kolimisteenused |
| [ ] | tartu | moving | OÜ Vedruvanker | vedruvanker@hot.ee | +372 5078598 |  | [10983765](https://ariregister.rik.ee/est/company/10983765) | Tartu | 1 | 49421 Kolimisteenused |
| [ ] | tartu | moving | Ricrone | raivo.ets1@gmail.com | +372 5020426 |  | [14145541](https://ariregister.rik.ee/est/company/14145541) | Raadi | 1 | 49421 Kolimisteenused, 49331 Sõitjate tellimusvedu koos juhiga |
| [ ] | tartu | moving | Shock Point | marekpeterson@hotmail.com |  |  | [12095750](https://ariregister.rik.ee/est/company/12095750) | Tartu | 1 | 49421 Kolimisteenused, 93291 Mujal liigitamata lõbustus- ja vaba |
| [ ] | tartu | moving | Takere | rauno.heitur@gmail.com |  |  | [16917812](https://ariregister.rik.ee/est/company/16917812) | Tartu | 1 | 43241 Muud ehituspaigaldustööd, 49421 Kolimisteenused, 81211 Hoo |
| [ ] | tartu | moving | Tamm Projekt | rivo.raamat.003@mail.ee | +372 53949526 |  | [14215069](https://ariregister.rik.ee/est/company/14215069) | Tartu | 1 | 49421 Kolimisteenused, 69109 Muud juriidilised toimingud, 82991  |
| [ ] | tartu | moving | Bauen | osayhingbauen@gmail.com | +372 54630901 |  | [14043645](https://ariregister.rik.ee/est/company/14043645) | Tartu | 0 | 49421 Kolimisteenused |
| [ ] | tartu | moving | Eltraks Estonia | arnold.kikas@gmail.com | +372 55667537 |  | [11349778](https://ariregister.rik.ee/est/company/11349778) | Tartu | 0 | 49421 Kolimisteenused |
| [ ] | tartu | trailer | Osaühing Baltic Trailer Rental | btr@btr.ee | +372 5019299 |  | [10880968](https://ariregister.rik.ee/est/company/10880968) | Tatra | 3 | 77121 Veokite rentimine ja kasutusrent |
| [ ] | tartu | cleaning | Terstaal | terstaal@terstaal.ee | +372 5027348 | https://terstaal.ee/ | [10851399](https://ariregister.rik.ee/est/company/10851399) | Päkste | 11 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | cleaning | K&R-Puhastus | info@krpuhastus.ee | +372 56604316 | https://krpuhastus.ee/ | [14350100](https://ariregister.rik.ee/est/company/14350100) | Aakaru | 3 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | cleaning | Arende | info@arende.ee | +372 55538532 | https://arende.ee/ | [16088876](https://ariregister.rik.ee/est/company/16088876) | Reola | 1 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | cleaning | 4rent | info@kodupunkt.eu | +372 51910811 | https://kodupunkt.eu/ | [16957645](https://ariregister.rik.ee/est/company/16957645) | Tartu | 0 | 81231 Muu puhastustegevus |
| [ ] | tartu | cleaning | Asum | info@asum.ee | +372 56685695 | https://asum.ee/ | [12485824](https://ariregister.rik.ee/est/company/12485824) | Tartu | 0 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | cleaning | Teket Põrand | info@teket.ee | +372 5515498 | https://teket.ee/ | [11540354](https://ariregister.rik.ee/est/company/11540354) | Tartu | 3 | 81211 Hoonete üldpuhastus, 47121 Muu spetsialiseerimata jaemüük, |
| [ ] | tartu | cleaning | Cleanmanagement | liis.jurjev@gmail.com | +372 5153588 | https://www.cleanmanagement.ee/ | [12630663](https://ariregister.rik.ee/est/company/12630663) | Tartu | 2 | 88911 Lapsehoiuteenus, 85599 Muu mujal liigitamata õpe, 81211 Ho |
| [ ] | tartu | cleaning | OÜ Randla | raili.randla@gmail.com |  | https://randla.ee/ | [17194907](https://ariregister.rik.ee/est/company/17194907) | Puhja | 0 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | cleaning | Cleanpro | info@arkaadia.ee | +372 6108555 |  | [12209372](https://ariregister.rik.ee/est/company/12209372) | Tartu | 38 | 81211 Hoonete üldpuhastus, 81101 Hoonehalduse kombineeritud teen |
| [ ] | tartu | cleaning | ELEGANTICO | myyk.enjoy@gmail.com | +372 5517009 |  | [14954430](https://ariregister.rik.ee/est/company/14954430) | Soinaste | 20 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | cleaning | OÜ Tiiu puhastus | treitav@gmail.com | +372 5099110 |  | [12654250](https://ariregister.rik.ee/est/company/12654250) | Tartu | 9 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | cleaning | Puhtad Lahendused | armarrandpalu@gmail.com | +372 53735888 |  | [14174502](https://ariregister.rik.ee/est/company/14174502) | Tartu | 8 | 81211 Hoonete üldpuhastus |
| [ ] | tartu | vanrental | OÜ Kaubikurent | mariosonn83@gmail.com |  |  | [16421666](https://ariregister.rik.ee/est/company/16421666) | Tartu | 0 | 77121 Veokite rentimine ja kasutusrent |

### Registry codes for existing rows that have none

| ok | row | matched by | register entry | proposed code |
|---|---|---|---|---|
| [ ] | `eliitserv` | name | Eliitserv OÜ | [14257671](https://ariregister.rik.ee/est/company/14257671) |
| [ ] | `espak-turi` | email_domain | Espak Haldus OÜ | [11959208](https://ariregister.rik.ee/est/company/11959208) |
| [ ] | `expertpuhastus` | phone | EXPERTPUHASTUS OÜ | [16671699](https://ariregister.rik.ee/est/company/16671699) |
| [ ] | `koduheaks` | phone | KoduHeaks OÜ | [17102360](https://ariregister.rik.ee/est/company/17102360) |
| [ ] | `kolimisroom` | email_domain | Kolimisrõõm OÜ | [12883393](https://ariregister.rik.ee/est/company/12883393) |
| [ ] | `kolimisteenused` | name | Kolimisteenused OÜ | [11306699](https://ariregister.rik.ee/est/company/11306699) |
| [ ] | `konteinerladu-ou` | name | Konteinerladu OÜ | [16353812](https://ariregister.rik.ee/est/company/16353812) |
| [ ] | `laverna` | email | LAVERNA PUHASTUSTÖÖDE Osaühing | [10925207](https://ariregister.rik.ee/est/company/10925207) |
| [ ] | `moppi` | phone | Moppi.ee Eesti OÜ | [14338004](https://ariregister.rik.ee/est/company/14338004) |
| [ ] | `niks` | email | DreamClean OÜ | [17169134](https://ariregister.rik.ee/est/company/17169134) |
| [ ] | `puhastustaht` | email | Puhastustäht OÜ | [16490319](https://ariregister.rik.ee/est/company/16490319) |
| [ ] | `tartu-kolimine` | phone | KUTO Tootmine OÜ | [11204875](https://ariregister.rik.ee/est/company/11204875) |
| [ ] | `vahi-minilaod` | name | Vahi Minilaod OÜ | [16392752](https://ariregister.rik.ee/est/company/16392752) |

Not proposed — several register companies share the contact: `ava-ekspress` (AVA-Ekspress osaühing, T&T Piano Transport OÜ, Töömees OÜ).
